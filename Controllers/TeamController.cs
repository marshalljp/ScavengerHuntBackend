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
using System.Collections.Generic;
using System.Security.Claims;

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

        // -------------------------------
        // Request Models
        // -------------------------------
        public class JoinTeamRequest { public int TeamId { get; set; } }
        public class ApproveTeamRequest { public int NewTeamUserId { get; set; } }
        public class KickTeamRequest { public int UserId { get; set; } }

        // -------------------------------
        // GET /teams/teams
        // -------------------------------
        [HttpGet("teams")]
        public async Task<IActionResult> ListTeams()
        {
            // Get current user ID from JWT (since [Authorize] is on the class)
            var connString = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new MySqlConnection(connString);
           conn.Open();

            int currentUserId = Convert.ToInt32(CommonUtils.GetUserID(User, _configuration));
            var teams = new List<Dictionary<string, object>>(); // mutable!

            try
            {
                // Step 1: Load teams
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT Id, Name FROM teams;";
                    using var r = await cmd.ExecuteReaderAsync();
                    while (await r.ReadAsync())
                    {
                        var team = new Dictionary<string, object>
                        {
                            ["id"] = r.GetInt32("Id"),
                            ["name"] = r.IsDBNull("Name") ? null : r.GetString("Name"),
                            ["members"] = new List<object>(),
                            ["ownerId"] = 0,
                            ["maxMembers"] = 10
                        };
                        teams.Add(team);
                    }
                }

                // Step 2: Load users in teams
                var users = new List<Dictionary<string, object>>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                SELECT Id, Name, Email, TeamOwner, TeamApproval, TeamId
                FROM users
                WHERE TeamId IS NOT NULL AND TeamId <> 9999;";
                    using var r = await cmd.ExecuteReaderAsync();
                    while (await r.ReadAsync())
                    {
                        users.Add(new Dictionary<string, object>
                        {
                            ["id"] = r.GetInt32("Id"),
                            ["name"] = r.IsDBNull("Name") ? null : r.GetString("Name"),
                            ["email"] = r.IsDBNull("Email") ? null : r.GetString("Email"),
                            ["isOwner"] = r.GetBoolean("TeamOwner"),
                            ["approval"] = r.GetInt32("TeamApproval"),
                            ["teamId"] = r.GetInt32("TeamId")
                        });
                    }
                }

                // Step 3: Fill members and ownerId (now mutable dictionaries ? allowed)
                foreach (var team in teams)
                {
                    var teamUsers = users.Where(u => (int)u["teamId"] == (int)team["id"]).ToList();

                    var owner = teamUsers.FirstOrDefault(u => (bool)u["isOwner"]);
                    team["ownerId"] = owner != null ? (int)owner["id"] : 0;

                    var memberList = (List<object>)team["members"];
                    foreach (var u in teamUsers)
                    {
                        memberList.Add(new
                        {
                            id = u["id"],
                            name = u["name"],
                            email = u["email"],
                            role = (bool)u["isOwner"] ? "Owner" : ((int)u["approval"] == 1 ? "Member" : "Pending")
                        });
                    }
                }

                // Find current user's approved team
                var currentTeam = teams.FirstOrDefault(t =>
                    ((List<object>)t["members"]).Any(m =>
                        m.GetType().GetProperty("id")!.GetValue(m) is int mid &&
                        mid == currentUserId &&
                        (m.GetType().GetProperty("role")!.GetValue(m)?.ToString() == "Owner" ||
                         m.GetType().GetProperty("role")!.GetValue(m)?.ToString() == "Member")));

                return Ok(new
                {
                    teams,
                    currentUserId,     // this is the key line
                    currentTeam
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }


        // -------------------------------
        // Owner-only checks for approve/reject/kick
        // -------------------------------
        private async Task<bool> IsTeamOwner(MySqlConnection conn, MySqlTransaction transaction, int userId, int teamId)
        {
            using (var ownerCheckCmd = conn.CreateCommand())
            {
                ownerCheckCmd.Transaction = transaction;
                ownerCheckCmd.CommandType = CommandType.Text;
                ownerCheckCmd.CommandText = "SELECT COUNT(1) FROM users WHERE Id = @UserId AND TeamId = @TeamId AND TeamOwner = 1;";
                ownerCheckCmd.Parameters.AddWithValue("@UserId", userId);
                ownerCheckCmd.Parameters.AddWithValue("@TeamId", teamId);
                var count = Convert.ToInt32(await ownerCheckCmd.ExecuteScalarAsync());
                return count > 0;
            }
        }

        // -------------------------------
        // POST /teams/join
        // -------------------------------
        [HttpPost("join")]
        public async Task<IActionResult> JoinTeam([FromBody] JoinTeamRequest request)
        {
            var email = CommonUtils.GetUserEmail(User);
            var userId = CommonUtils.GetUserID(User, _configuration);
            int teamId = request.TeamId;
            var connString = _configuration.GetConnectionString("DefaultConnection");

            using (var conn = new MySqlConnection(connString))
            {
                await conn.OpenAsync();
                using (var transaction = await conn.BeginTransactionAsync())
                {
                    try
                    {
                        // Check if team has any members
                        bool isFirstMember = false;
                        using (var memberCountCmd = conn.CreateCommand())
                        {
                            memberCountCmd.Transaction = transaction;
                            memberCountCmd.CommandType = CommandType.Text;
                            memberCountCmd.CommandText = "SELECT COUNT(*) FROM users WHERE TeamId = @TeamId;";
                            memberCountCmd.Parameters.AddWithValue("@TeamId", teamId);
                            int count = Convert.ToInt32(await memberCountCmd.ExecuteScalarAsync());
                            isFirstMember = count == 0;
                        }

                        if (isFirstMember)
                        {
                            // Make this user the owner immediately
                            using (var assignOwnerCmd = conn.CreateCommand())
                            {
                                assignOwnerCmd.Transaction = transaction;
                                assignOwnerCmd.CommandType = CommandType.Text;
                                assignOwnerCmd.CommandText = "UPDATE users SET TeamId = @TeamId, TeamOwner = 1, TeamApproval = 1 WHERE Id = @UserId;";
                                assignOwnerCmd.Parameters.AddWithValue("@TeamId", teamId);
                                assignOwnerCmd.Parameters.AddWithValue("@UserId", userId);
                                await assignOwnerCmd.ExecuteNonQueryAsync();
                            }

                            await transaction.CommitAsync();
                            return new JsonResult(new { success = true, message = "You have joined the team as the owner!" });
                        }
                        else
                        {
                            // Existing logic: request join and notify owner
                            using (var reqCmd = conn.CreateCommand())
                            {
                                reqCmd.Transaction = transaction;
                                reqCmd.CommandType = CommandType.Text;
                                reqCmd.CommandText = "UPDATE users SET TeamId = @TeamId, TeamApproval = 1 WHERE Id = @UserId;";
                                reqCmd.Parameters.AddWithValue("@TeamId", teamId);
                                reqCmd.Parameters.AddWithValue("@UserId", userId);
                                await reqCmd.ExecuteNonQueryAsync();
                            }

                            // Notify team owner
                            int ownerId = 0;
                            using (var ownerCmd = conn.CreateCommand())
                            {
                                ownerCmd.Transaction = transaction;
                                ownerCmd.CommandType = CommandType.Text;
                                ownerCmd.CommandText = "SELECT Id FROM users WHERE TeamId = @TeamId AND TeamOwner = 1 LIMIT 1;";
                                ownerCmd.Parameters.AddWithValue("@TeamId", teamId);
                                var result = await ownerCmd.ExecuteScalarAsync();
                                ownerId = result != null ? Convert.ToInt32(result) : 0;
                            }

                            if (ownerId != 0)
                            {
                                using (var notifyCmd = conn.CreateCommand())
                                {
                                    notifyCmd.Transaction = transaction;
                                    notifyCmd.CommandType = CommandType.Text;
                                    notifyCmd.CommandText = "INSERT INTO notifications (user_id, message, action, teamUserId) VALUES (@UserId, @Message, 1, @teamUserId);";
                                    notifyCmd.Parameters.AddWithValue("@UserId", ownerId);
                                    notifyCmd.Parameters.AddWithValue("@Message", $"User {email} requested to join your team!");
                                    notifyCmd.Parameters.AddWithValue("@teamUserId", userId);
                                    await notifyCmd.ExecuteNonQueryAsync();
                                }
                            }

                            await transaction.CommitAsync();
                            return new JsonResult(new { success = true, message = "Your request to join this team has been sent." });
                        }
                    }
                    catch (System.Exception ex)
                    {
                        await transaction.RollbackAsync();
                        return StatusCode(500, $"Internal server error: {ex.Message}");
                    }
                }
            }
        }

        // -------------------------------
        // POST /teams/approve
        // -------------------------------
        [HttpPost("approve")]
        public async Task<IActionResult> TeamRequestApprove([FromBody] ApproveTeamRequest request)
        {
            var ownerId = CommonUtils.GetUserID(User, _configuration);
            var teamId = CommonUtils.GetTeamUserID(User, _configuration);
            int newUserId = request.NewTeamUserId;
            var email = CommonUtils.GetUserEmailByID(newUserId, _configuration);
            var connString = _configuration.GetConnectionString("DefaultConnection");

            using (var conn = new MySqlConnection(connString))
            {
                await conn.OpenAsync();
                using (var transaction = await conn.BeginTransactionAsync())
                {
                    try
                    {
                        if (!await IsTeamOwner(conn, transaction, Convert.ToInt32(ownerId), Convert.ToInt32(teamId)))
                            return StatusCode(403, new { error = "Only the team owner can approve members." });

                        // Approve member
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = transaction;
                            cmd.CommandType = CommandType.Text;
                            cmd.CommandText = "UPDATE users SET TeamId = @TeamId, TeamApproval = 1 WHERE Id = @UserId;";
                            cmd.Parameters.AddWithValue("@TeamId", teamId);
                            cmd.Parameters.AddWithValue("@UserId", newUserId);
                            await cmd.ExecuteNonQueryAsync();

                            cmd.CommandText = "DELETE FROM notifications WHERE action = 1 AND teamUserId = @NewUserId;";
                            cmd.Parameters.AddWithValue("@NewUserId", newUserId);
                            await cmd.ExecuteNonQueryAsync();
                        }

                        // Notify all team members
                        var teamUserIds = new List<int>();
                        using (var selectCmd = conn.CreateCommand())
                        {
                            selectCmd.Transaction = transaction;
                            selectCmd.CommandType = CommandType.Text;
                            selectCmd.CommandText = "SELECT Id FROM users WHERE TeamId = @TeamId;";
                            selectCmd.Parameters.AddWithValue("@TeamId", teamId);
                            using (var reader = await selectCmd.ExecuteReaderAsync())
                                while (await reader.ReadAsync())
                                    teamUserIds.Add(reader.GetInt32("Id"));
                        }

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

                        await transaction.CommitAsync();
                        return Ok(new { success = true, message = "Team Join Request Approved" });
                    }
                    catch (System.Exception ex)
                    {
                        await transaction.RollbackAsync();
                        return StatusCode(500, $"Internal server error: {ex.Message}");
                    }
                }
            }
        }

        // -------------------------------
        // POST /teams/reject
        // -------------------------------
        [HttpPost("reject")]
        public async Task<IActionResult> TeamRequestReject([FromBody] ApproveTeamRequest request)
        {
            var ownerId = CommonUtils.GetUserID(User, _configuration);
            var teamId = CommonUtils.GetTeamUserID(User, _configuration);
            int newUserId = request.NewTeamUserId;
            var email = CommonUtils.GetUserEmailByID(newUserId, _configuration);
            var connString = _configuration.GetConnectionString("DefaultConnection");

            using (var conn = new MySqlConnection(connString))
            {
                await conn.OpenAsync();
                using (var transaction = await conn.BeginTransactionAsync())
                {
                    try
                    {
                        if (!await IsTeamOwner(conn, transaction, Convert.ToInt32(ownerId), Convert.ToInt32(teamId)))
                            return StatusCode(403, new { error = "Only the team owner can reject members." });

                        // Reject member
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = transaction;
                            cmd.CommandType = CommandType.Text;
                            cmd.CommandText = "UPDATE users SET TeamId = NULL WHERE Id = @UserId;";
                            cmd.Parameters.AddWithValue("@UserId", newUserId);
                            await cmd.ExecuteNonQueryAsync();

                            cmd.CommandText = "DELETE FROM notifications WHERE action = 1 AND teamUserId = @NewUserId;";
                            cmd.Parameters.AddWithValue("@NewUserId", newUserId);
                            await cmd.ExecuteNonQueryAsync();
                        }

                        // Notify team members
                        var teamUserIds = new List<int>();
                        using (var selectCmd = conn.CreateCommand())
                        {
                            selectCmd.Transaction = transaction;
                            selectCmd.CommandType = CommandType.Text;
                            selectCmd.CommandText = "SELECT Id FROM users WHERE TeamId = @TeamId;";
                            selectCmd.Parameters.AddWithValue("@TeamId", teamId);
                            using (var reader = await selectCmd.ExecuteReaderAsync())
                                while (await reader.ReadAsync())
                                    teamUserIds.Add(reader.GetInt32("Id"));
                        }

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

                        await transaction.CommitAsync();
                        return Ok(new { success = true, message = "Team Join Request Rejected" });
                    }
                    catch (System.Exception ex)
                    {
                        await transaction.RollbackAsync();
                        return StatusCode(500, $"Internal server error: {ex.Message}");
                    }
                }
            }
        }


        // -------------------------------
        // POST /teams/kick
        // -------------------------------
        [HttpPost("kick")]
        public async Task<IActionResult> KickTeamMember([FromBody] KickTeamRequest request)
        {
            var ownerId = CommonUtils.GetUserID(User, _configuration);
            var teamId = CommonUtils.GetTeamUserID(User, _configuration);
            int targetUserId = request.UserId;
            var email = CommonUtils.GetUserEmailByID(targetUserId, _configuration);
            var connString = _configuration.GetConnectionString("DefaultConnection");

            using (var conn = new MySqlConnection(connString))
            {
                await conn.OpenAsync();
                using (var transaction = await conn.BeginTransactionAsync())
                {
                    try
                    {
                        // Only owner can kick
                        if (!await IsTeamOwner(conn, transaction, Convert.ToInt32(ownerId), Convert.ToInt32(teamId)))
                            return StatusCode(403, new { error = "Only the team owner can kick members." });

                        // Cannot kick self
                        if (targetUserId == Convert.ToInt32(ownerId))
                            return StatusCode(400, new { error = "Owner cannot kick themselves." });

                        // Remove user from team
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = transaction;
                            cmd.CommandType = CommandType.Text;
                            cmd.CommandText = "UPDATE users SET TeamId = 9999, TeamOwner = 0, TeamApproval = 0 WHERE Id = @UserId AND TeamId = @TeamId;";
                            cmd.Parameters.AddWithValue("@UserId", targetUserId);
                            cmd.Parameters.AddWithValue("@TeamId", teamId);
                            await cmd.ExecuteNonQueryAsync();
                        }

                        // Delete related notifications
                        using (var delNotif = conn.CreateCommand())
                        {
                            delNotif.Transaction = transaction;
                            delNotif.CommandType = CommandType.Text;
                            delNotif.CommandText = "DELETE FROM notifications WHERE teamUserId = @UserId;";
                            delNotif.Parameters.AddWithValue("@UserId", targetUserId);
                            await delNotif.ExecuteNonQueryAsync();
                        }

                        // Notify remaining members
                        var teamUserIds = new List<int>();
                        using (var selectCmd = conn.CreateCommand())
                        {
                            selectCmd.Transaction = transaction;
                            selectCmd.CommandType = CommandType.Text;
                            selectCmd.CommandText = "SELECT Id FROM users WHERE TeamId = @TeamId;";
                            selectCmd.Parameters.AddWithValue("@TeamId", teamId);
                            using (var reader = await selectCmd.ExecuteReaderAsync())
                                while (await reader.ReadAsync())
                                    teamUserIds.Add(reader.GetInt32("Id"));
                        }

                        foreach (var uid in teamUserIds)
                        {
                            using (var insertCmd = conn.CreateCommand())
                            {
                                insertCmd.Transaction = transaction;
                                insertCmd.CommandType = CommandType.Text;
                                insertCmd.CommandText = "INSERT INTO notifications (user_id, message) VALUES (@UserId, @Message);";
                                insertCmd.Parameters.AddWithValue("@UserId", uid);
                                insertCmd.Parameters.AddWithValue("@Message", $"User {email} has been removed from your team.");
                                await insertCmd.ExecuteNonQueryAsync();
                            }
                        }

                        await transaction.CommitAsync();
                        return Ok(new { success = true, message = $"User {email} has been kicked from the team." });
                    }
                    catch (System.Exception ex)
                    {
                        await transaction.RollbackAsync();
                        return StatusCode(500, $"Internal server error: {ex.Message}");
                    }
                }
            }
        }


        // -------------------------------
        // POST /teams/leave
        // -------------------------------
        [HttpPost("leave")]
        public async Task<IActionResult> LeaveTeam()
        {
            var userId = CommonUtils.GetUserID(User, _configuration);
            var teamId = CommonUtils.GetTeamUserID(User, _configuration);
            var connString = _configuration.GetConnectionString("DefaultConnection");

            using (var conn = new MySqlConnection(connString))
            {
                await conn.OpenAsync();
                using (var transaction = await conn.BeginTransactionAsync())
                {
                    try
                    {
                        // Check if leaving user is owner
                        bool isOwner = false;
                        using (var ownerCheckCmd = conn.CreateCommand())
                        {
                            ownerCheckCmd.Transaction = transaction;
                            ownerCheckCmd.CommandText = @"
                        SELECT TeamOwner 
                        FROM users 
                        WHERE Id = @UserId AND TeamId = @TeamId;";
                            ownerCheckCmd.Parameters.AddWithValue("@UserId", userId);
                            ownerCheckCmd.Parameters.AddWithValue("@TeamId", teamId);
                            var result = await ownerCheckCmd.ExecuteScalarAsync();
                            isOwner = result != null && Convert.ToBoolean(result);
                        }

                        // Remove user from team
                        using (var leaveCmd = conn.CreateCommand())
                        {
                            leaveCmd.Transaction = transaction;
                            leaveCmd.CommandText = @"
                        UPDATE users 
                        SET TeamId = NULL, TeamOwner = 0 
                        WHERE Id = @UserId;";
                            leaveCmd.Parameters.AddWithValue("@UserId", userId);
                            await leaveCmd.ExecuteNonQueryAsync();
                        }

                        // Promote next member to owner if leaving user was owner
                        if (isOwner)
                        {
                            int? nextOwnerId = null;
                            using (var nextOwnerCmd = conn.CreateCommand())
                            {
                                nextOwnerCmd.Transaction = transaction;
                                nextOwnerCmd.CommandText = @"
                            SELECT Id 
                            FROM users 
                            WHERE TeamId = @TeamId 
                            ORDER BY Id 
                            LIMIT 1;";
                                nextOwnerCmd.Parameters.AddWithValue("@TeamId", teamId);
                                var nextOwnerResult = await nextOwnerCmd.ExecuteScalarAsync();
                                nextOwnerId = nextOwnerResult != null ? Convert.ToInt32(nextOwnerResult) : (int?)null;
                            }

                            if (nextOwnerId.HasValue)
                            {
                                using (var promoteCmd = conn.CreateCommand())
                                {
                                    promoteCmd.Transaction = transaction;
                                    promoteCmd.CommandText = @"
                                UPDATE users 
                                SET TeamOwner = 1 
                                WHERE Id = @UserId;";
                                    promoteCmd.Parameters.AddWithValue("@UserId", nextOwnerId.Value);
                                    await promoteCmd.ExecuteNonQueryAsync();
                                }
                            }
                        }

                        // Notify remaining members
                        var remainingUserIds = new List<int>();
                        using (var membersCmd = conn.CreateCommand())
                        {
                            membersCmd.Transaction = transaction;
                            membersCmd.CommandText = "SELECT Id FROM users WHERE TeamId = @TeamId;";
                            membersCmd.Parameters.AddWithValue("@TeamId", teamId);

                            using (var reader = await membersCmd.ExecuteReaderAsync())
                            {
                                while (await reader.ReadAsync())
                                {
                                    remainingUserIds.Add(reader.GetInt32("Id"));
                                }
                            }
                        }

                        foreach (var targetUserId in remainingUserIds)
                        {
                            using (var notifyCmd = conn.CreateCommand())
                            {
                                notifyCmd.Transaction = transaction;
                                notifyCmd.CommandText = @"
                            INSERT INTO notifications (user_id, message) 
                            VALUES (@UserId, @Message);";
                                notifyCmd.Parameters.AddWithValue("@UserId", targetUserId);
                                notifyCmd.Parameters.AddWithValue("@Message", $"User {userId} has left your team!");
                                await notifyCmd.ExecuteNonQueryAsync();
                            }
                        }

                        await transaction.CommitAsync();
                        return Ok(new { success = true, message = "You have left the team." });
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        return StatusCode(500, new { error = $"Internal server error: {ex.Message}" });
                    }
                }
            }
        }

    }
}
