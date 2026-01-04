using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using ScavengerHuntBackend.Models;
using ScavengerHuntBackend.Utils;
using System.Data;
using System.Threading.Tasks;

namespace ScavengerHuntBackend.Controllers
{
    [Route("users")]
    [ApiController]
    [Authorize]
    public class UserController : ControllerBase
    {

        private readonly ScavengerHuntContext _context;
        private readonly IConfiguration _configuration;

        private readonly IConfiguration _config;

        public UserController(ScavengerHuntContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        private MySqlConnection GetConnection()
        {
            return new MySqlConnection(_config.GetConnectionString("DefaultConnection"));
        }

        // -------------------------------------------------------------
        // GET /users/profile
        // -------------------------------------------------------------
        [HttpGet("profile")]
        public async Task<IActionResult> GetProfile()
        {
            try
            {
                int userId = Convert.ToInt32(CommonUtils.GetUserID(User, _configuration));
                string email = CommonUtils.GetUserEmailByID(userId, _configuration);

                var connString = _configuration.GetConnectionString("DefaultConnection");

                using (var conn = new MySqlConnection(connString))
                {
                    await conn.OpenAsync();

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandType = CommandType.Text;
                        cmd.CommandText = @"
                    SELECT 
                        Id, Email, Name, Username, Bio, 
                        PuzzlesCompleted, GlobalRank, TotalScore, 
                        TeamId, Team, role
                    FROM users 
                    WHERE Email = @Email
                    LIMIT 1;";

                        cmd.Parameters.AddWithValue("@Email", email);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (!await reader.ReadAsync())
                                return NotFound(new { success = false, message = "User not found" });

                            var user = new
                            {
                                Id = reader.GetInt32("Id"),
                                Email = reader.GetString("Email"),

                                Name = reader["Name"] != DBNull.Value
                                    ? reader.GetString("Name")
                                    : null,

                                Username = reader["Username"] != DBNull.Value
                                    ? reader.GetString("Username")
                                    : null,

                                Bio = reader["Bio"] != DBNull.Value
                                    ? reader.GetString("Bio")
                                    : null,

                                PuzzlesCompleted = reader.GetInt32("PuzzlesCompleted"),
                                GlobalRank = reader.GetInt32("GlobalRank"),
                                TotalScore = reader.GetInt32("TotalScore"),

                                TeamId = reader["TeamId"] != DBNull.Value
                                    ? reader.GetInt32("TeamId")
                                    : (int?)null,

                                Team = reader["Team"] != DBNull.Value
                                    ? reader.GetString("Team")
                                    : null,

                                Role = reader["Role"] != DBNull.Value
                                    ? reader.GetInt32("Role")
                                    : (int?)null,
                            };

                            return Ok(new { success = true, user });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error retrieving profile",
                    error = ex.Message
                });
            }
        }

        // -------------------------------------------------------------
        // PUT /users/profile
        // Allows: Name, Username, Bio, Email, PasswordHash, PuzzlesCompleted
        // TeamId is NOT changed here.
        // -------------------------------------------------------------
        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] User update)
        {
            var userId = CommonUtils.GetUserID(User, _configuration);
            if (userId == null)
                return Unauthorized(new { success = false, message = "Invalid user identity." });

            string email = CommonUtils.GetUserEmailByID(Convert.ToInt32(userId), _configuration);
            if (email == null)
                return Unauthorized(new { success = false, message = "User email not found." });

            var connString = _configuration.GetConnectionString("DefaultConnection");

            using (var conn = new MySqlConnection(connString))
            {
                await conn.OpenAsync();
                using (var transaction = await conn.BeginTransactionAsync())
                {
                    try
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = transaction;
                            cmd.CommandType = CommandType.Text;
                            cmd.CommandText = @"
                        UPDATE users SET
                            Name = @Name,
                            Email = @NewEmail,
                            Bio = @Bio
                        WHERE Email = @OldEmail;
                    ";

                            cmd.Parameters.AddWithValue("@Name", update.Name ?? "");
                            cmd.Parameters.AddWithValue("@Bio", update.Bio ?? "");
                            cmd.Parameters.AddWithValue("@NewEmail", update.Email ?? email);
                            cmd.Parameters.AddWithValue("@OldEmail", email);

                            int rows = await cmd.ExecuteNonQueryAsync();
                            if (rows == 0)
                            {
                                await transaction.RollbackAsync();
                                return NotFound(new { success = false, message = "User not found." });
                            }
                        }

                        await transaction.CommitAsync();
                        return Ok(new { success = true, message = "Profile updated successfully." });
                    }
                    catch (System.Exception ex)
                    {
                        await transaction.RollbackAsync();
                        return StatusCode(500, new { success = false, message = "Internal server error", error = ex.Message });
                    }
                }
            }
        }


    }
}
