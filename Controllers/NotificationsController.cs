using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using ScavengerHuntBackend.Utils;

namespace ScavengerHuntBackend.Services
{
    [Route("notifications")]
    [ApiController]
    [Authorize]
    public class NotificationController : ControllerBase
    {
        private readonly ScavengerHuntContext _context;
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public NotificationController(ScavengerHuntContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
        }

        /// <summary>
        /// Retrieves all unseen notifications for a specific user and marks them as seen.
        /// </summary>
        /// <param name="userId">The ID of the user for whom to fetch notifications.</param>
        /// <returns>A list of Notification objects.</returns>
        [HttpGet("unseen")]
        public IActionResult GetUnseenNotifications()
        {
            var email = CommonUtils.GetUserEmail(User);
            var userId = CommonUtils.GetUserID(User, _configuration);
            var notifications = new List<Notification>();
            var notificationIds = new List<int>();

            string selectSql = @"
                SELECT id, user_id, message, created_at, seen, action, teamUserId 
                FROM notifications 
                WHERE user_id = @UserId 
                ORDER BY created_at DESC";

            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                // Begin a transaction to ensure atomicity.
                using (var transaction = conn.BeginTransaction())
                {
                    // Retrieve unseen notifications.
                    using (var cmd = new MySqlCommand(selectSql, conn, transaction))
                    {
                        cmd.Parameters.AddWithValue("@UserId", userId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var notification = new Notification
                                {
                                    Id = reader.GetInt32("id"),
                                    UserId = reader.GetInt32("user_id"),
                                    Message = reader.GetString("message"),
                                    CreatedAt = reader.GetDateTime("created_at"),
                                    Seen = reader.GetBoolean("seen"),
                                    Action = reader.GetInt32("action"),
                                    TeamUserId = reader.GetInt32("TeamUserId")
                                };
                                notifications.Add(notification);
                                notificationIds.Add(notification.Id);
                            }
                        }
                    }

                    transaction.Commit();
                }
            }
            return Ok(notifications);
        }


    }

    /// <summary>
    /// Represents a notification record.
    /// </summary>
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
