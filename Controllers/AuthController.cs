using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using ScavengerHuntBackend.Models;
using MySqlConnector;
using System.Net;
using System.Xml;
using System.Data;
using ScavengerHuntBackend.Utils;
using Microsoft.AspNetCore.Identity.Data;
using System.Net.Mail;


namespace ScavengerHuntBackend.Controllers
{
    [Route("auth")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly ScavengerHuntContext _context;
        private readonly IConfiguration _configuration;

        public AuthController(ScavengerHuntContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] User user)
        {
            if (user == null)
                return BadRequest("User object is null.");

            // Validate email and password.
            if (string.IsNullOrEmpty(user.Email) || string.IsNullOrEmpty(user.PasswordHash))
                return BadRequest("Email and password are required.");

            // Ensure the user does not already exist.
            if (await _context.Users.AnyAsync(u => u.Email == user.Email))
                return BadRequest("User already exists.");

            // Hash the provided password.
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(user.PasswordHash);

            try
            {
                var connString = _configuration.GetConnectionString("DefaultConnection");
                using (MySqlConnection conn = new MySqlConnection(connString))
                {
                    using (MySqlCommand cmd = conn.CreateCommand())
                    {
                        conn.Open();

                        // Check if the access code exists and is not used.
                        cmd.CommandType = System.Data.CommandType.Text;
                        cmd.CommandText = "SELECT COUNT(*) FROM AccessCodes WHERE AccessCode = @AccessCode AND Used = 0";
                        cmd.Parameters.AddWithValue("@AccessCode", user.AccessCode);
                        int accessCodeCount = Convert.ToInt32(cmd.ExecuteScalar());
                        if(user.AccessCode == "21000000")
                        {
                            accessCodeCount = 1;
                        }
                        if (accessCodeCount > 0)
                        {
                            int teamId = 0;


                            // Insert the new user and capture the generated user ID.
                            cmd.CommandText = "INSERT INTO users (Email, PasswordHash, TeamID, Role) VALUES (@Email, @PasswordHash, @TeamID, @Role); SELECT LAST_INSERT_ID();";
                            cmd.Parameters.Clear();
                            cmd.Parameters.AddWithValue("@Email", user.Email);
                            cmd.Parameters.AddWithValue("@PasswordHash", user.PasswordHash);
                            cmd.Parameters.AddWithValue("@TeamID", teamId);
                            if (user.AccessCode == "21000000")
                            {
                                //Trail Role
                                cmd.Parameters.AddWithValue("@Role", 1);
                            }
                            else
                            {
                                //Guest Role
                                cmd.Parameters.AddWithValue("@Role", 2);
                            }
                            int newUserId = Convert.ToInt32(cmd.ExecuteScalar());

                            // ---
                            // Copy puzzle progress rows from the old table to the new table,
                            // replacing the user_id with the new user's id.
                            // NOTE: Adjust the source and destination table names as needed.
                            // Here we assume the old table is named "old_puzzleprogress" and the new table is "puzzleprogress".
                            // ---
                            cmd.CommandType = CommandType.Text;
                            cmd.CommandText =
                                "INSERT INTO puzzleprogress (user_id, puzzle_id, team_id, puzzleidorder) " +
                                "SELECT @NewUserId, puzzleid, @teamId, puzzleidorder " +
                                "FROM puzzlesdetails;";
                            cmd.Parameters.Clear();
                            cmd.Parameters.AddWithValue("@NewUserId", newUserId);
                            cmd.Parameters.AddWithValue("@teamId", teamId);
                            await cmd.ExecuteNonQueryAsync();
                            
                            if (user.AccessCode != "21000000")
                            {
                                // Mark the access code as used.
                                cmd.CommandText = "UPDATE AccessCodes SET Used = 1 WHERE AccessCode = @AccessCode";
                                cmd.Parameters.Clear();
                                cmd.Parameters.AddWithValue("@AccessCode", user.AccessCode);
                                cmd.ExecuteNonQuery(); ;
                            }



                            return Ok(new { success = true, message = "User registered successfully. Please login now!" });
                        }
                        else
                        {
                            return BadRequest("Invalid access code.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        public class ChangePasswordRequest
        {
            public string CurrentPassword { get; set; }
            public string NewPassword { get; set; }
        }

        [HttpPut("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.CurrentPassword) || string.IsNullOrEmpty(request.NewPassword))
                return BadRequest("Current and new password are required.");

            try
            {
                // Get user ID from JWT token
                int userId = Convert.ToInt32(CommonUtils.GetUserID(User, _configuration));

                var connString = _configuration.GetConnectionString("DefaultConnection");
                using (var conn = new MySqlConnection(connString))
                {
                    await conn.OpenAsync();

                    using (var cmd = conn.CreateCommand())
                    {
                        // STEP 1 — Get current stored password hash
                        cmd.CommandText = "SELECT PasswordHash FROM users WHERE Id = @Id";
                        cmd.Parameters.AddWithValue("@Id", userId);

                        var existingHashObj = await cmd.ExecuteScalarAsync();
                        if (existingHashObj == null)
                            return Unauthorized("User not found.");

                        string existingHash = existingHashObj.ToString();

                        // STEP 2 — Verify the current password
                        bool validPassword = BCrypt.Net.BCrypt.Verify(request.CurrentPassword, existingHash);
                        if (!validPassword)
                            return Unauthorized("Current password is incorrect.");

                        // STEP 3 — Hash new password
                        string newHashedPassword = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);

                        // STEP 4 — Update database
                        cmd.Parameters.Clear();
                        cmd.CommandText = "UPDATE users SET PasswordHash = @NewHash WHERE Id = @Id";
                        cmd.Parameters.AddWithValue("@NewHash", newHashedPassword);
                        cmd.Parameters.AddWithValue("@Id", userId);

                        await cmd.ExecuteNonQueryAsync();

                        return Ok(new { success = true, message = "Password updated successfully." });
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        public class ForgotPasswordRequest
        {
            public string Email { get; set; }
        }


        [HttpPost("forgotpassword")] 
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Email))
                return BadRequest("Email is required.");

            try
            {
                var connString = _configuration.GetConnectionString("DefaultConnection");

                using (var conn = new MySqlConnection(connString))
                {
                    await conn.OpenAsync();

                    using (var cmd = conn.CreateCommand())
                    {
                        // STEP 1 — Check if user exists
                        cmd.CommandText = "SELECT Id FROM users WHERE Email = @Email";
                        cmd.Parameters.AddWithValue("@Email", request.Email);

                        object userIdObj = await cmd.ExecuteScalarAsync();
                        if (userIdObj == null)
                        {
                            // Always return success to prevent email enumeration
                            return Ok(new { success = true, message = "If that email exists, a reset code has been sent." });
                        }

                        int userId = Convert.ToInt32(userIdObj);

                        // STEP 2 — Generate 4-digit code
                        Random rnd = new Random();
                        string code = rnd.Next(1000, 9999).ToString(); // 4-digit code
                        DateTime expiresAt = DateTime.UtcNow.AddMinutes(15); // code valid for 15 mins

                        // STEP 3 — Insert the code into the database
                        cmd.Parameters.Clear();
                        cmd.CommandText = @"
                    INSERT INTO PasswordResetTokens (UserId, Token, ExpiresAt)
                    VALUES (@UserId, @Token, @ExpiresAt);";

                        cmd.Parameters.AddWithValue("@UserId", userId);
                        cmd.Parameters.AddWithValue("@Token", code); // store 4-digit code in Token field
                        cmd.Parameters.AddWithValue("@ExpiresAt", expiresAt);

                        await cmd.ExecuteNonQueryAsync();

                        // STEP 4 — Send email with code
                        // TODO: Integrate your email service here
                        // Example: EmailService.SendEmail(request.Email, "Your reset code", $"Your 4-digit code is: {code}");

                        var mail = new MailMessage
                        {
                            From = new MailAddress("noreply@satoshisbeachhouse.com", "Satoshi's Trail"),
                            Subject = "Satoshi's Trail: Your Password Reset Code",
                            Body = $"Your 4-digit reset code is: {code}\n\nThis code expires in 15 minutes.",
                            IsBodyHtml = false
                        };

                        mail.To.Add(request.Email);

                        var client = new SmtpClient("mail.historia.network", 587)
                        {
                            EnableSsl = true,
                            UseDefaultCredentials = false,
                            Credentials = new NetworkCredential(
                                "noreply@historia.network",
                                "Je66oHy9iiptQ"
                            )
                        };

                        try
                        {
                            client.Send(mail);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Exception caught in CreateTestMessage4(): {0}", ex.ToString());
                        }


                        return Ok(new
                        {
                            success = true,
                            message = "If that email exists, a reset code has been sent.",
                            //testCode = code // REMOVE in production
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        public class ResetPasswordRequest
        {
            public string Email { get; set; }
            public string Code { get; set; }
            public string NewPassword { get; set; }
        }

        [HttpPost("resetpassword")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            if (request == null ||
                string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.Code) ||
                string.IsNullOrWhiteSpace(request.NewPassword))
            {
                return BadRequest(new { message = "Invalid request." });
            }

            try
            {
                var connString = _configuration.GetConnectionString("DefaultConnection");

                using (var conn = new MySqlConnection(connString))
                {
                    await conn.OpenAsync();

                    using (var tx = await conn.BeginTransactionAsync())
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;

                        // STEP 1 — Get user
                        cmd.CommandText = "SELECT Id FROM users WHERE Email = @Email";
                        cmd.Parameters.AddWithValue("@Email", request.Email);

                        var userIdObj = await cmd.ExecuteScalarAsync();
                        if (userIdObj == null)
                        {
                            return BadRequest(new { message = "Invalid reset code or expired." });
                        }

                        int userId = Convert.ToInt32(userIdObj);

                        // STEP 2 — Validate reset token
                        cmd.Parameters.Clear();
                        cmd.CommandText = @"
                    SELECT Id
                    FROM PasswordResetTokens
                    WHERE UserId = @UserId
                      AND Token = @Token
                      AND Used = 0
                      AND ExpiresAt >= UTC_TIMESTAMP()
                    ORDER BY Id DESC
                    LIMIT 1;
                ";

                        cmd.Parameters.AddWithValue("@UserId", userId);
                        cmd.Parameters.AddWithValue("@Token", request.Code);

                        var tokenIdObj = await cmd.ExecuteScalarAsync();
                        if (tokenIdObj == null)
                        {
                            return BadRequest(new { message = "Invalid reset code or expired." });
                        }

                        int tokenId = Convert.ToInt32(tokenIdObj);

                        // STEP 3 — Hash new password
                        string hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);

                        // STEP 4 — Update user password
                        cmd.Parameters.Clear();
                        cmd.CommandText = "UPDATE users SET PasswordHash = @Password WHERE Id = @UserId";
                        cmd.Parameters.AddWithValue("@Password", hashedPassword);
                        cmd.Parameters.AddWithValue("@UserId", userId);

                        await cmd.ExecuteNonQueryAsync();

                        // STEP 5 — Mark token as used
                        cmd.Parameters.Clear();
                        cmd.CommandText = "UPDATE PasswordResetTokens SET Used = 1 WHERE Id = @TokenId";
                        cmd.Parameters.AddWithValue("@TokenId", tokenId);

                        await cmd.ExecuteNonQueryAsync();

                        await tx.CommitAsync();

                        return Ok(new { success = true, message = "Password reset successful." });
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal server error." });
            }
        }


        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] User loginRequest)
        {
            var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == loginRequest.Email.ToLower());
            if (user == null || !BCrypt.Net.BCrypt.Verify(loginRequest.PasswordHash, user.PasswordHash))
                return Unauthorized("Invalid credentials.");

            var token = GenerateJwtToken(user);
            return Ok(new { Token = token });
        }

        private string GenerateJwtToken(User user)
        {
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Secret"]));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddHours(2),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
