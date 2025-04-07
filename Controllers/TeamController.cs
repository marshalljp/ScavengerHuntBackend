using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using ScavengerHuntBackend.Models;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using System.Net;
using System.Data;
using Microsoft.AspNetCore.Authorization;
using ScavengerHuntBackend.Utils;

namespace ScavengerHuntBackend.Controllers
{
    [Route("teams")]
    [ApiController]
    [Authorize]
    public class TeamController : ControllerBase
    {
        private readonly ScavengerHuntContext _context;
        private readonly IConfiguration _configuration;
        public TeamController(ScavengerHuntContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }


        // Request model to accept the teamId from the POST body.
        public class JoinTeamRequest
        {
            public int TeamId { get; set; }

        }

        [HttpPost("join")]
        public async Task<IActionResult> JoinTeam([FromBody] JoinTeamRequest request)
        {
            // Get user details from claims or helper methods.
            var email = CommonUtils.GetUserEmail(User);
            var userId = CommonUtils.GetUserID(User, _configuration);
            int TeamId = request.TeamId;

            var connString = _configuration.GetConnectionString("DefaultConnection");

            using (var conn = new MySqlConnection(connString))
            {
                await conn.OpenAsync();
                // Begin a transaction so that reading and writing is atomic.
                using (var transaction = await conn.BeginTransactionAsync())
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
                            selectCmd.Parameters.AddWithValue("@TeamId", TeamId);

                            using (var reader = await selectCmd.ExecuteReaderAsync())
                            {
                                while (await reader.ReadAsync())
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
                                insertCmd.Parameters.AddWithValue("@Message", $"User {email} requested to join you team!");
                                insertCmd.Parameters.AddWithValue("@teamUserId", userId);
                                await insertCmd.ExecuteNonQueryAsync();
                            }
                        }

                        // Optionally, update the user's team assignment in your users table.
                        // You might want to add logic here to assign the user to the team if that is part of your flow.

                        // Commit the transaction.
                        await transaction.CommitAsync();
                    }
                    catch (Exception ex)
                    {
                        // Rollback if any error occurs.
                        await transaction.RollbackAsync();
                        return StatusCode(500, $"Internal server error: {ex.Message}");
                    }
                }
            }

            return new JsonResult(new { success = true, message = "Your request to join this team has been sent." });

        }

        public class ApproveTeamRequest
        {
            public int NewTeamUserId { get; set; }

        }

        // New method to approve a team join request.
        [HttpPost("approve")]
        public async Task<IActionResult> TeamRequestApprove([FromBody] ApproveTeamRequest request)
        {
            var userId = CommonUtils.GetUserID(User, _configuration);
            var teamId = CommonUtils.GetTeamUserID(User, _configuration);
            int NewTeamUserId = request.NewTeamUserId;
            var email = CommonUtils.GetUserEmailByID(NewTeamUserId, _configuration);
            var connString = _configuration.GetConnectionString("DefaultConnection");

            using (var conn = new MySqlConnection(connString))
            {
                await conn.OpenAsync();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandType = CommandType.Text;
                    cmd.CommandText = "UPDATE users SET TeamId = @TeamId WHERE Id = @UserId;";
                    cmd.Parameters.AddWithValue("@TeamId", teamId);
                    cmd.Parameters.AddWithValue("@UserId", NewTeamUserId);
                    cmd.ExecuteNonQuery();

                    cmd.CommandText =
                        "DELETE FROM notifications WHERE action = 1 AND teamUserId = @NewUserId";
                    cmd.Parameters.AddWithValue("@NewUserId", NewTeamUserId);
                    cmd.ExecuteNonQuery();

                    using (var transaction = await conn.BeginTransactionAsync())
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

                                using (var reader = await selectCmd.ExecuteReaderAsync())
                                {
                                    while (await reader.ReadAsync())
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
                                    insertCmd.CommandText = "INSERT INTO notifications (user_id, message) VALUES (@UserId, @Message);";
                                    insertCmd.Parameters.AddWithValue("@UserId", targetUserId);
                                    insertCmd.Parameters.AddWithValue("@Message", $"User {email} has joined your team!");
                                    await insertCmd.ExecuteNonQueryAsync();
                                }
                            }

                            // Optionally, update the user's team assignment in your users table.
                            // You might want to add logic here to assign the user to the team if that is part of your flow.

                            // Commit the transaction.
                            await transaction.CommitAsync();
                        }
                        catch (Exception ex)
                        {
                            // Rollback if any error occurs.
                            await transaction.RollbackAsync();
                            return StatusCode(500, $"Internal server error: {ex.Message}");
                        }
                    }
                    return Ok(new { success = true, message = "Team Join Request Approved" });
                }
            }
        }

        // New method to approve a team join request.
        [HttpPost("reject")]
        public async Task<IActionResult> TeamRequestReject([FromBody] ApproveTeamRequest request)
        {
   
            var userId = CommonUtils.GetUserID(User, _configuration);
            var teamId = CommonUtils.GetTeamUserID(User, _configuration);
            int NewTeamUserId = request.NewTeamUserId;
            var email = CommonUtils.GetUserEmailByID(NewTeamUserId, _configuration);
            var connString = _configuration.GetConnectionString("DefaultConnection");

            using (var conn = new MySqlConnection(connString))
            {
                await conn.OpenAsync();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandType = CommandType.Text;
                    // For a rejection, remove the join requester's team assignment by setting it to NULL.
                    cmd.CommandText = "UPDATE users SET TeamId = NULL WHERE Id = @UserId;";
                    cmd.Parameters.AddWithValue("@UserId", NewTeamUserId);
                    cmd.ExecuteNonQuery();

                    cmd.CommandText = "DELETE FROM notifications WHERE action = 1 AND teamUserId = @NewUserId";
                    cmd.Parameters.AddWithValue("@NewUserId", NewTeamUserId);
                    cmd.ExecuteNonQuery();

                    using (var transaction = await conn.BeginTransactionAsync())
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

                                using (var reader = await selectCmd.ExecuteReaderAsync())
                                {
                                    while (await reader.ReadAsync())
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
                                    insertCmd.CommandText = "INSERT INTO notifications (user_id, message) VALUES (@UserId, @Message);";
                                    insertCmd.Parameters.AddWithValue("@UserId", targetUserId);
                                    insertCmd.Parameters.AddWithValue("@Message", $"User {email} team join request denied!");
                                    await insertCmd.ExecuteNonQueryAsync();
                                }
                            }

                            // Commit the transaction.
                            await transaction.CommitAsync();
                        }
                        catch (Exception ex)
                        {
                            // Rollback if any error occurs.
                            await transaction.RollbackAsync();
                            return StatusCode(500, $"Internal server error: {ex.Message}");
                        }
                    }
                    return Ok(new { success = true, message = "Team Join Request Rejected" });
                }
            }
        }

    }
}