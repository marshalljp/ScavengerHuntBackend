using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using ScavengerHuntBackend.Models;
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
        /// Retrieves all notifications for the authenticated user.
        /// </summary>
        [HttpGet("all")]
        public IActionResult GetNotifications()
        {
            var userId = CommonUtils.GetUserID(User, _configuration);
            var notifications = new List<Notification>();

            string selectSql = @"
                SELECT id, user_id, message, created_at, seen, action, teamUserId 
                FROM notifications 
                WHERE user_id = @UserId 
                ORDER BY created_at DESC";

            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new MySqlCommand(selectSql, conn))
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
                                TeamUserId = reader.GetInt32("teamUserId")
                            };
                            notifications.Add(notification);
                        }
                    }
                }
            }

            return Ok(notifications);
        }

        private async Task<bool> IsTeamOwner(MySqlConnection conn, int userId, int teamId)
        {
            const string sql = @"
        SELECT COUNT(1)
        FROM users
        WHERE TeamId = @TeamId AND Id = @UserId
        LIMIT 1;";

            using (var cmd = new MySqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@TeamId", teamId);
                cmd.Parameters.AddWithValue("@UserId", userId);

                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(result) > 0;
            }
        }


        /// <summary>
        /// Approve a join team request.
        /// </summary>
        [HttpPost("approve")]
        public async Task<IActionResult> ApproveJoinTeam([FromBody] TeamUserIdRequest request)
        {
            var userId = CommonUtils.GetUserID(User, _configuration);

            if (request == null || request.TeamUserId <= 0)
            {
                return BadRequest(new { success = false, message = "Invalid team user ID" });
            }

            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    // 1. Get the teamId of the user requesting approval
                    int? teamId = await GetTeamIdOfUser(conn, request.TeamUserId);

                    if (teamId == null)
                    {
                        return BadRequest(new { success = false, message = "This user is not assigned to any team." });
                    }

                    // 2. Verify the current user is the owner of that team
                    if (!await IsTeamOwner(conn, Convert.ToInt32(userId), Convert.ToInt32(teamId)))
                    {
                        return StatusCode(403, new { error = "Only the team owner can approve members." });
                    }

                    // 3. Approve the join request
                    string updateSql = @"
                UPDATE users 
                SET TeamApproval = 1 
                WHERE id = @TeamUserId";

                    using (var cmd = new MySqlCommand(updateSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@TeamUserId", request.TeamUserId);

                        int rows = await cmd.ExecuteNonQueryAsync();
                        if (rows > 0)
                        {
                            return Ok(new { success = true, message = "Join request approved" });
                        }

                        return BadRequest(new { success = false, message = "Failed to approve join request" });
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        public class TeamUserIdRequest
        {
            public int TeamUserId { get; set; }
        }

        private async Task<int?> GetTeamIdOfUser(MySqlConnection conn, int userId)
        {
            string sql = "SELECT TeamId FROM users WHERE Id = @UserId LIMIT 1";

            using (var cmd = new MySqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@UserId", userId);

                var result = await cmd.ExecuteScalarAsync();
                if (result == null || result == DBNull.Value)
                    return null;

                return Convert.ToInt32(result);
            }
        }


        public class RejectJoinRequest
        {
            public int TeamUserId { get; set; }
        }

        /// <summary>
        /// Reject a join team request.
        /// </summary>
         [HttpPost("reject")]
        public async Task<IActionResult> RejectJoinTeam([FromBody] RejectJoinRequest request)

        {
            var callerUserId = CommonUtils.GetUserID(User, _configuration);
            if (request == null || request.TeamUserId <= 0)
            {
                return BadRequest(new { success = false, message = "Invalid team user ID" });
            }

            int teamUserId = request.TeamUserId;

            const string getTeamIdSql = @"SELECT TeamId FROM users WHERE Id = @TeamUserId LIMIT 1;";
            const string updateSql = @"UPDATE users SET TeamApproval = -1 WHERE Id = @TeamUserId;";

            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    // 1?? Get the teamId of the person being rejected
                    int? teamId = null;

                    using (var getCmd = new MySqlCommand(getTeamIdSql, conn))
                    {
                        getCmd.Parameters.AddWithValue("@TeamUserId", teamUserId);
                        var result = await getCmd.ExecuteScalarAsync();

                        if (result == null || result == DBNull.Value)
                        {
                            return BadRequest(new { success = false, message = "User not found" });
                        }

                        teamId = Convert.ToInt32(result);
                    }

                    // 2?? Check if the caller is the owner of this team
                    bool isOwner = await IsTeamOwner(conn, Convert.ToInt32(callerUserId), teamId.Value);
                    if (!isOwner)
                    {
                        return StatusCode(403, new { success = false, message = "Only the team owner can reject members." });
                    }

                    // 3?? Perform the rejection update
                    using (var cmd = new MySqlCommand(updateSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@TeamUserId", teamUserId);

                        int rows = await cmd.ExecuteNonQueryAsync();
                        if (rows > 0)
                        {
                            return Ok(new { success = true, message = "Join request rejected" });
                        }

                        return BadRequest(new { success = false, message = "Failed to reject join request" });
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Optional: mark notifications as seen.
        /// </summary>
        [HttpPost("markSeen")]
        public IActionResult MarkNotificationsSeen([FromBody] NotificationIdsRequest request)
        {
            if (request == null || request.NotificationIds == null || request.NotificationIds.Count == 0)
                return BadRequest(new { success = false, message = "No notifications provided" });

            int userId = Convert.ToInt32(CommonUtils.GetUserID(User, _configuration));

            const string updateSql = @"UPDATE notifications 
                               SET seen = 1 
                               WHERE id = @Id AND user_id = @UserId";

            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = new MySqlCommand(updateSql, conn))
                {
                    cmd.Parameters.Add("@Id", MySqlDbType.Int32);
                    cmd.Parameters.Add("@UserId", MySqlDbType.Int32).Value = userId;

                    foreach (var id in request.NotificationIds)
                    {
                        cmd.Parameters["@Id"].Value = id;
                        cmd.ExecuteNonQuery();
                    }
                }
            }

            return Ok(new { success = true, message = "Notifications marked as seen" });
        }

    }

    public class Notification
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Message { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool Seen { get; set; }
        public int Action { get; set; } // 1 = approve/deny buttons
        public int TeamUserId { get; set; }
    }
    public class NotificationIdsRequest
    {
        public List<int> NotificationIds { get; set; } = new();
    }

}
