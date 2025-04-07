using Microsoft.Extensions.Configuration;
using MySqlConnector;
using System;
using System.Security.Claims;

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

        public static string AddNotification(ClaimsPrincipal user, IConfiguration configuration)
        {
            string userId = "";
            var connString = configuration.GetConnectionString("DefaultConnection");
            using (MySqlConnection conn = new MySqlConnection(connString))
            {
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    conn.Open();

                    cmd.CommandText = "INSERT INTO notifications (user_id, message, action, teamUserId) VALUES (@UserId, @Message, 1, @teamUserId);";
                    //cmd.Parameters.AddWithValue("@UserId", targetUserId);
                    //cmd.Parameters.AddWithValue("@teamUserId", userId);
                    cmd.ExecuteNonQuery();
                }
            }

            return userId;
        }
    }
}
