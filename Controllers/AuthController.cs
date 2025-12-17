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
using System.Data;
using ScavengerHuntBackend.Utils;
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
                return BadRequest(new { success = false, message = "User object is null." });

            if (string.IsNullOrEmpty(user.Email) || string.IsNullOrEmpty(user.PasswordHash))
                return BadRequest(new { success = false, message = "Email and password are required." });

            if (await _context.Users.AnyAsync(u => u.Email.ToLower() == user.Email.ToLower()))
                return BadRequest(new { success = false, message = "User already exists." });

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(user.PasswordHash);

            var connString = _configuration.GetConnectionString("DefaultConnection");

            try
            {
                await using var conn = new MySqlConnection(connString);
                await conn.OpenAsync();

                await using var tx = await conn.BeginTransactionAsync();
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;

                // Validate access code
                cmd.CommandText = "SELECT COUNT(*) FROM AccessCodes WHERE AccessCode = @AccessCode AND Used = 0";
                cmd.Parameters.AddWithValue("@AccessCode", user.AccessCode);
                int accessCodeCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());

                if (user.AccessCode == "21000000")
                    accessCodeCount = 1;

                if (accessCodeCount <= 0)
                    return BadRequest(new { success = false, message = "Invalid access code." });

                int teamId = 0;

                // Generate email verification code
                Random rnd = new Random();
                string verificationCode = rnd.Next(1000, 9999).ToString();
                DateTime expiresAt = DateTime.UtcNow.AddMinutes(15);

                // Insert user
                cmd.Parameters.Clear();
                cmd.CommandText = @"
            INSERT INTO users 
            (Email, PasswordHash, TeamID, Role, IsEmailVerified, EmailVerificationCode, EmailVerificationExpires)
            VALUES
            (@Email, @PasswordHash, @TeamID, @Role, 0, @Code, @Expires);
            SELECT LAST_INSERT_ID();";

                cmd.Parameters.AddWithValue("@Email", user.Email);
                cmd.Parameters.AddWithValue("@PasswordHash", user.PasswordHash);
                cmd.Parameters.AddWithValue("@TeamID", teamId);
                cmd.Parameters.AddWithValue("@Role", user.AccessCode == "21000000" ? 1 : 2);
                cmd.Parameters.AddWithValue("@Code", verificationCode);
                cmd.Parameters.AddWithValue("@Expires", expiresAt);

                int newUserId = Convert.ToInt32(await cmd.ExecuteScalarAsync());

                // Copy puzzle progress
                cmd.Parameters.Clear();
                cmd.CommandText = @"
            INSERT INTO puzzleprogress (user_id, puzzle_id, team_id, puzzleidorder)
            SELECT @UserId, puzzleid, @TeamId, puzzleidorder
            FROM puzzlesdetails;";
                cmd.Parameters.AddWithValue("@UserId", newUserId);
                cmd.Parameters.AddWithValue("@TeamId", teamId);
                await cmd.ExecuteNonQueryAsync();

                // Mark access code as used (non-trail)
                if (user.AccessCode != "21000000")
                {
                    cmd.Parameters.Clear();
                    cmd.CommandText = "UPDATE AccessCodes SET Used = 1 WHERE AccessCode = @AccessCode";
                    cmd.Parameters.AddWithValue("@AccessCode", user.AccessCode);
                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();

                // Send verification email AFTER committing transaction
                await SendRegistrationEmail(user.Email, verificationCode);

                return Ok(new
                {
                    success = true,
                    message = "Registration successful. Please check your email for the verification code."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"Internal server error: {ex.Message}" });
            }
        }

        private async Task SendRegistrationEmail(string toEmail, string verificationCode)
        {
            var mail = new MailMessage
            {
                From = new MailAddress("noreply@satoshisbeachhouse.com", "Satoshi's Trail"),
                Subject = "Verify your email — Satoshi's Trail ???",
                Body = $@"Welcome to Satoshi's Trail!

Your 4-digit verification code is:

{verificationCode}

This code expires in 15 minutes.

— Satoshi's Trail",
                IsBodyHtml = false
            };

            mail.To.Add(toEmail);

            var client = new SmtpClient("mail.historia.network", 587)
            {
                EnableSsl = true,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential("noreply@historia.network", "Je66oHy9iiptQ")
            };

            await client.SendMailAsync(mail);
        }

        public class VerifyEmailRequest
        {
            public string Email { get; set; } = "";
            public string Code { get; set; } = "";
        }

        [HttpPost("verify-email")]
        public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Code))
                return BadRequest(new { success = false, message = "Email and verification code are required." });

            try
            {
                var connString = _configuration.GetConnectionString("DefaultConnection");

                using (var conn = new MySqlConnection(connString))
                {
                    await conn.OpenAsync();

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT Id, EmailVerificationCode, EmailVerificationExpires, IsEmailVerified
                            FROM users
                            WHERE Email = @Email
                            LIMIT 1;";
                        cmd.Parameters.AddWithValue("@Email", request.Email);

                        using var reader = await cmd.ExecuteReaderAsync();

                        if (!reader.Read())
                            return BadRequest(new { success = false, message = "Invalid verification attempt." });

                        int userId = reader.GetInt32("Id");
                        string? dbCode = reader["EmailVerificationCode"] as string;
                        DateTime? expiresAt = reader["EmailVerificationExpires"] as DateTime?;
                        bool isVerified = reader.GetBoolean("IsEmailVerified");

                        if (isVerified)
                            return Ok(new { success = true, message = "Email already verified." });

                        if (dbCode == null || expiresAt == null)
                            return BadRequest(new { success = false, message = "No active verification code found." });

                        if (DateTime.UtcNow > expiresAt.Value)
                            return BadRequest(new { success = false, message = "Verification code has expired." });

                        if (dbCode != request.Code)
                            return BadRequest(new { success = false, message = "Invalid verification code." });

                        reader.Close();

                        cmd.Parameters.Clear();
                        cmd.CommandText = @"
                            UPDATE users
                            SET IsEmailVerified = 1,
                                EmailVerificationCode = NULL,
                                EmailVerificationExpires = NULL
                            WHERE Id = @UserId;";
                        cmd.Parameters.AddWithValue("@UserId", userId);

                        await cmd.ExecuteNonQueryAsync();

                        return Ok(new { success = true, message = "Email successfully verified!" });
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"Internal server error: {ex.Message}" });
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
                return BadRequest(new { success = false, message = "Current and new password are required." });

            try
            {
                int userId = Convert.ToInt32(CommonUtils.GetUserID(User, _configuration));
                var connString = _configuration.GetConnectionString("DefaultConnection");

                using (var conn = new MySqlConnection(connString))
                {
                    await conn.OpenAsync();

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT PasswordHash FROM users WHERE Id = @Id";
                        cmd.Parameters.AddWithValue("@Id", userId);

                        var existingHashObj = await cmd.ExecuteScalarAsync();
                        if (existingHashObj == null)
                            return Unauthorized(new { success = false, message = "User not found." });

                        string existingHash = existingHashObj.ToString();

                        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, existingHash))
                            return Unauthorized(new { success = false, message = "Current password is incorrect." });

                        string newHashedPassword = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);

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
                return StatusCode(500, new { success = false, message = $"Internal server error: {ex.Message}" });
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
                return BadRequest(new { success = false, message = "Email is required." });

            try
            {
                var connString = _configuration.GetConnectionString("DefaultConnection");

                using (var conn = new MySqlConnection(connString))
                {
                    await conn.OpenAsync();

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Id FROM users WHERE Email = @Email";
                        cmd.Parameters.AddWithValue("@Email", request.Email);

                        var userIdObj = await cmd.ExecuteScalarAsync();
                        if (userIdObj == null)
                        {
                            // Always return success to prevent email enumeration
                            return Ok(new { success = true, message = "If that email exists, a reset code has been sent." });
                        }

                        int userId = Convert.ToInt32(userIdObj);
                        Random rnd = new Random();
                        string code = rnd.Next(1000, 9999).ToString();
                        DateTime expiresAt = DateTime.UtcNow.AddMinutes(15);

                        cmd.Parameters.Clear();
                        cmd.CommandText = @"
                            INSERT INTO PasswordResetTokens (UserId, Token, ExpiresAt)
                            VALUES (@UserId, @Token, @ExpiresAt);";
                        cmd.Parameters.AddWithValue("@UserId", userId);
                        cmd.Parameters.AddWithValue("@Token", code);
                        cmd.Parameters.AddWithValue("@ExpiresAt", expiresAt);

                        await cmd.ExecuteNonQueryAsync();

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
                            Credentials = new NetworkCredential("noreply@historia.network", "Je66oHy9iiptQ")
                        };

                        try { client.Send(mail); }
                        catch { }

                        return Ok(new
                        {
                            success = true,
                            message = "If that email exists, a reset code has been sent."
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"Internal server error: {ex.Message}" });
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
                return BadRequest(new { success = false, message = "Invalid request." });
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

                        cmd.CommandText = "SELECT Id FROM users WHERE Email = @Email";
                        cmd.Parameters.AddWithValue("@Email", request.Email);

                        var userIdObj = await cmd.ExecuteScalarAsync();
                        if (userIdObj == null)
                            return BadRequest(new { success = false, message = "Invalid reset code or expired." });

                        int userId = Convert.ToInt32(userIdObj);

                        cmd.Parameters.Clear();
                        cmd.CommandText = @"
                            SELECT Id FROM PasswordResetTokens
                            WHERE UserId = @UserId AND Token = @Token AND Used = 0 AND ExpiresAt >= UTC_TIMESTAMP()
                            ORDER BY Id DESC LIMIT 1;";
                        cmd.Parameters.AddWithValue("@UserId", userId);
                        cmd.Parameters.AddWithValue("@Token", request.Code);

                        var tokenIdObj = await cmd.ExecuteScalarAsync();
                        if (tokenIdObj == null)
                            return BadRequest(new { success = false, message = "Invalid reset code or expired." });

                        int tokenId = Convert.ToInt32(tokenIdObj);

                        string hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);

                        cmd.Parameters.Clear();
                        cmd.CommandText = "UPDATE users SET PasswordHash = @Password WHERE Id = @UserId";
                        cmd.Parameters.AddWithValue("@Password", hashedPassword);
                        cmd.Parameters.AddWithValue("@UserId", userId);
                        await cmd.ExecuteNonQueryAsync();

                        cmd.Parameters.Clear();
                        cmd.CommandText = "UPDATE PasswordResetTokens SET Used = 1 WHERE Id = @TokenId";
                        cmd.Parameters.AddWithValue("@TokenId", tokenId);
                        await cmd.ExecuteNonQueryAsync();

                        await tx.CommitAsync();

                        return Ok(new { success = true, message = "Password reset successful." });
                    }
                }
            }
            catch
            {
                return StatusCode(500, new { success = false, message = "Internal server error." });
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] User loginRequest)
        {
            if (string.IsNullOrEmpty(loginRequest.Email) || string.IsNullOrEmpty(loginRequest.PasswordHash))
            {
                return BadRequest(new { success = false, message = "Email and password are required." });
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == loginRequest.Email.ToLower());

            if (user == null || !BCrypt.Net.BCrypt.Verify(loginRequest.PasswordHash, user.PasswordHash))
            {
                return Ok(new { success = false, message = "Invalid email or password" });
            }

            if (!user.IsEmailVerified)
            {
                return Ok(new
                {
                    success = false,
                    requiresEmailVerification = true,
                    message = "Your email is not verified. Please check your inbox for the verification code."
                });
            }

            var token = GenerateJwtToken(user);

            return Ok(new { success = true, token = token, emailVerified = true, message = "Login successful." });
        }

        [HttpPost("resend-verification")]
        public async Task<IActionResult> ResendVerificationEmail([FromBody] ResendEmailRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Email))
                return BadRequest(new { success = false, message = "Email is required." });

            try
            {
                var connString = _configuration.GetConnectionString("DefaultConnection");

                using var conn = new MySqlConnection(connString);
                await conn.OpenAsync();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Id FROM Users WHERE Email = @Email";
                cmd.Parameters.AddWithValue("@Email", request.Email);

                var userIdObj = await cmd.ExecuteScalarAsync();
                if (userIdObj == null)
                    return BadRequest(new { success = false, message = "No user found with this email." });

                int userId = Convert.ToInt32(userIdObj);

                // Generate new 4-digit code
                var rnd = new Random();
                string newCode = rnd.Next(1000, 9999).ToString();
                DateTime expiresAt = DateTime.UtcNow.AddMinutes(15);

                // Update user record
                cmd.Parameters.Clear();
                cmd.CommandText = @"
            UPDATE Users
            SET EmailVerificationCode = @Code,
                EmailVerificationExpires = @Expires,
                IsEmailVerified = 0
            WHERE Id = @UserId";
                cmd.Parameters.AddWithValue("@Code", newCode);
                cmd.Parameters.AddWithValue("@Expires", expiresAt);
                cmd.Parameters.AddWithValue("@UserId", userId);

                await cmd.ExecuteNonQueryAsync();

                // Send the email asynchronously
                await SendRegistrationEmail(request.Email, newCode);

                return Ok(new { success = true, message = "Verification code resent. Please check your email." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"Internal server error: {ex.Message}" });
            }
        }

        // DTO for request
        public class ResendEmailRequest
        {
            public string Email { get; set; } = "";
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
