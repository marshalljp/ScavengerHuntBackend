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
            var userId = CommonUtils.GetUserID(user, _configuration);
            var teamId = CommonUtils.GetTeamUserID(user, _configuration); // assuming this returns string or int directly
            var connString = _configuration.GetConnectionString("DefaultConnection");

            using var conn = new MySqlConnection(connString);
            conn.Open();  // ? FIX: Use synchronous Open()

            using var transaction = conn.BeginTransaction();
            try
            {
                var teamUserIds = new List<int>();

                // Get all user IDs in the same team
                using (var selectCmd = conn.CreateCommand())
                {
                    selectCmd.Transaction = transaction;
                    selectCmd.CommandText = "SELECT Id FROM users WHERE TeamId = @TeamId";
                    selectCmd.Parameters.AddWithValue("@TeamId", teamId);

                    using var reader = selectCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        teamUserIds.Add(reader.GetInt32("Id"));
                    }
                }

                // Insert notification for each team member
                foreach (var targetUserId in teamUserIds)
                {
                    using var insertCmd = conn.CreateCommand();
                    insertCmd.Transaction = transaction;
                    insertCmd.CommandText = @"
                INSERT INTO notifications (user_id, message, action, teamUserId) 
                VALUES (@UserId, @Message, 1, @teamUserId)";

                    insertCmd.Parameters.AddWithValue("@UserId", targetUserId);
                    insertCmd.Parameters.AddWithValue("@Message", message);
                    insertCmd.Parameters.AddWithValue("@teamUserId", userId);

                    insertCmd.ExecuteNonQuery();
                }

                transaction.Commit();
                return true;
            }
            catch
            {
                transaction.Rollback();
                return false;
            }
        }

        public static bool GS_internal(
            ClaimsPrincipal user,
            IConfiguration _configuration,
            int puzzleid
        )
        {
            try
            {
                var userId = CommonUtils.GetUserID(user, _configuration);
                var connString = _configuration.GetConnectionString("DefaultConnection");

                bool hasRequiredRows = false;

                using (var conn = new MySqlConnection(connString))
                {
                    conn.Open();

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                    SELECT pp.is_completed
                    FROM scavengerhunt.puzzleprogress pp
                    JOIN scavengerhunt.puzzlesdetails pd
                        ON pd.puzzleidorder = pp.puzzleidorder
                    WHERE pp.user_id = @userId
                      AND pp.puzzle_id = @puzzleid
                      AND pd.requiredForSeed = 1;
                ";

                        cmd.Parameters.AddWithValue("@userId", userId);
                        cmd.Parameters.AddWithValue("@puzzleid", puzzleid);

                        using (var rdr = cmd.ExecuteReader())
                        {
                            while (rdr.Read())
                            {
                                hasRequiredRows = true;

                                int isCompleted = rdr.GetInt32(0);

                                // ? Any incomplete step blocks the seed
                                if (isCompleted == 0)
                                    return false;
                            }
                        }
                    }

                    // ? No required-for-seed rows found
                    if (!hasRequiredRows)
                        return false;

                    // ? All required steps complete ? fetch seed
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                    SELECT seed
                    FROM scavengerhunt.seeds
                    WHERE puzzle_id = @puzzleid;
                ";

                        cmd.Parameters.AddWithValue("@puzzleid", puzzleid);

                        var seed = cmd.ExecuteScalar()?.ToString();
                        return !string.IsNullOrEmpty(seed);
                    }
                }
            }
            catch
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
