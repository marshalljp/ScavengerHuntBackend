using Microsoft.Extensions.Configuration;
using MySqlConnector;
using ScavengerHuntBackend.Models;
using System;
using System.Data;
using System.Security.Claims;
using static ScavengerHuntBackend.Services.NotificationController;

namespace ScavengerHuntBackend.Utils
{
    public static class CommonUtils
    {
        /// <summary>
        /// Extracts the user's email (from the "sub" claim) from the ClaimsPrincipal.
        /// </summary>
        public static string GetUserEmail(ClaimsPrincipal user)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            return user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }

        /// <summary>
        /// Retrieves the user's id from the database using their email.
        /// </summary>
        public static string GetUserID(ClaimsPrincipal user, IConfiguration configuration)
        {
            var email = GetUserEmail(user);
            if (string.IsNullOrEmpty(email))
                throw new ArgumentNullException(nameof(email), "Email claim is missing.");

            int userId;
            var connString = configuration.GetConnectionString("DefaultConnection");

            using (MySqlConnection conn = new MySqlConnection(connString))
            {
                conn.Open();
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandType = System.Data.CommandType.Text;
                    cmd.CommandText = "SELECT id FROM users WHERE email = @email";
                    cmd.Parameters.AddWithValue("@email", email);

                    object result = cmd.ExecuteScalar();
                    if (result == null || result == DBNull.Value)
                        throw new Exception("User not found.");

                    userId = Convert.ToInt32(result);
                }
            }

            return userId.ToString();
        }

        public static string IsTeamOwner(ClaimsPrincipal user, IConfiguration configuration)
        {
            var email = GetUserEmail(user);
            if (string.IsNullOrEmpty(email))
                throw new ArgumentNullException(nameof(email), "Email claim is missing.");

            int userId;
            var connString = configuration.GetConnectionString("DefaultConnection");
            var IsTeamOwner = 0;
            using (MySqlConnection conn = new MySqlConnection(connString))
            {
                conn.Open();
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandType = System.Data.CommandType.Text;
                    cmd.CommandText = "SELECT teamowner FROM users WHERE email = @email";
                    cmd.Parameters.AddWithValue("@email", email);

                    object result = cmd.ExecuteScalar();
                    if (result == null || result == DBNull.Value)
                        throw new Exception("User not found.");

                    IsTeamOwner = Convert.ToInt32(result);
                }
            }

            return IsTeamOwner.ToString();
        }

        public static string GetUserEmailByID(int userId, IConfiguration configuration)
        {

            var connString = configuration.GetConnectionString("DefaultConnection");
            string userEmail = "";

            using (MySqlConnection conn = new MySqlConnection(connString))
            {
                conn.Open();
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandType = System.Data.CommandType.Text;
                    cmd.CommandText = "SELECT Email FROM users WHERE id = @userId";
                    cmd.Parameters.AddWithValue("@userId", userId);

                    object result = cmd.ExecuteScalar();
                    if (result == null || result == DBNull.Value)
                        throw new Exception("User not found.");

                    userEmail = result.ToString();
                }
            }

            return userEmail;
        }
        public static string GetTeamUserID(ClaimsPrincipal user, IConfiguration configuration)
        {
            var email = GetUserEmail(user);
            if (string.IsNullOrEmpty(email))
                throw new ArgumentNullException(nameof(email), "Email claim is missing.");

            int userId;
            var connString = configuration.GetConnectionString("DefaultConnection");

            using (MySqlConnection conn = new MySqlConnection(connString))
            {
                conn.Open();
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandType = System.Data.CommandType.Text;
                    cmd.CommandText = "SELECT TeamId FROM users WHERE email = @email";
                    cmd.Parameters.AddWithValue("@email", email);

                    object result = cmd.ExecuteScalar();
                    if (result == null || result == DBNull.Value) { 
                        userId = 9999;
                    } else { 
                        userId = Convert.ToInt32(result);
                    }
                }
            }

            return userId.ToString();
        }

        public static bool AddNotification(ClaimsPrincipal user, IConfiguration _configuration, string message)
        {
            var email = CommonUtils.GetUserEmail(user);
            var userId = CommonUtils.GetUserID(user, _configuration);
            var teamId = CommonUtils.GetTeamUserID(user, _configuration);
            var notifications = new List<Notification>();
            var notificationIds = new List<int>();
            var connString = _configuration.GetConnectionString("DefaultConnection");
            using (var conn = new MySqlConnection(connString))
            {
                conn.OpenAsync();
                // Begin a transaction so that reading and writing is atomic.
                using (var transaction = conn.BeginTransaction())
                {
                    try
                    {
                        // Get all user IDs from the users table for the specified team.
                        var teamUserIds = new List<int>();
                        using (var selectCmd = conn.CreateCommand())
                        {
                            selectCmd.Transaction = transaction;
                            selectCmd.CommandType = CommandType.Text;
                            selectCmd.CommandText = "SELECT Id FROM users WHERE TeamId = @TeamId;";
                            selectCmd.Parameters.AddWithValue("@TeamId", teamId);

                            using (var reader = selectCmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    teamUserIds.Add(reader.GetInt32("Id"));
                                }
                            }
                        }

                        // For each user in the team, insert a notification.
                        foreach (var targetUserId in teamUserIds)
                        {
                            using (var insertCmd = conn.CreateCommand())
                            {
                                insertCmd.Transaction = transaction;
                                insertCmd.CommandType = CommandType.Text;
                                insertCmd.CommandText = "INSERT INTO notifications (user_id, message, action, teamUserId) VALUES (@UserId, @Message, 1, @teamUserId);";
                                insertCmd.Parameters.AddWithValue("@UserId", targetUserId);
                                insertCmd.Parameters.AddWithValue("@Message", message);
                                insertCmd.Parameters.AddWithValue("@teamUserId", userId);
                                insertCmd.ExecuteNonQuery();
                            }
                        }

                        // Optionally, update the user's team assignment in your users table.
                        // You might want to add logic here to assign the user to the team if that is part of your flow.

                        // Commit the transaction.
                        transaction.Commit();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        // Rollback if any error occurs.
                        transaction.Rollback();
                        return false;
                    }
                }
            }

        }

        public static bool GS_internal(ClaimsPrincipal user, IConfiguration _configuration, int id)
        {
            try
            {
                var email = CommonUtils.GetUserEmail(user);
                var userId = CommonUtils.GetUserID(user, _configuration);
                int teamId = Int32.Parse(CommonUtils.GetTeamUserID(user, _configuration));
                var connString = _configuration.GetConnectionString("DefaultConnection");

                // Flag to determine if any progress rows were returned.
                bool foundProgressRow = false;

                using (MySqlConnection conn = new MySqlConnection(connString))
                {
                    conn.Open();

                    // Query the progress values.
                    using (MySqlCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandType = CommandType.Text;
                        cmd.CommandText = @"
                    SELECT progress 
                    FROM scavengerhunt.puzzleprogress 
                    JOIN puzzlesdetails 
                      ON puzzlesdetails.puzzleidorder = puzzleprogress.puzzleidorder 
                    WHERE user_id = @userId 
                      AND requiredForSeed = 1";
                        cmd.Parameters.AddWithValue("@userId", userId);

                        using (var rdr = cmd.ExecuteReader())
                        {
                            while (rdr.Read())
                            {
                                foundProgressRow = true;
                                int progressIndex = rdr.GetOrdinal("progress");
                                // Check if the progress field is null.
                                if (rdr.IsDBNull(progressIndex))
                                {
                                    return false;
                                }

                                int progressValue = rdr.GetInt32(progressIndex);
                                // If any progress value is 0, progress is incomplete.
                                if (progressValue == 0)
                                {
                                    return false;
                                }
                            }
                        }
                    }

                    // If no rows were returned, then there are no progress records—so progress is incomplete.
                    if (!foundProgressRow)
                    {
                        return false;
                    }

                    // If progress exists and is complete (nonzero), retrieve the seed word.
                    string seed = null;
                    using (MySqlCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandType = CommandType.Text;
                        cmd.CommandText = "SELECT seed FROM scavengerhunt.seeds WHERE puzzle_id = @id";
                        cmd.Parameters.AddWithValue("@id", id);

                        using (var rdr = cmd.ExecuteReader())
                        {
                            if (rdr.Read())
                            {
                                seed = rdr["seed"]?.ToString();
                            }
                        }
                    }

                    conn.Close();

                    if (!string.IsNullOrEmpty(seed))
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                return false;
            }
        }
    }

    public class Notification
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Message { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool Seen { get; set; }
        public int Action { get; set; }
        public int TeamUserId { get; set; }
    }
}
