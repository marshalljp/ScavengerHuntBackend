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

                        if (accessCodeCount > 0)
                        {
                            int teamId = 0;

                            // If a team name is provided, insert a new team and get its ID.
                            if (!string.IsNullOrEmpty(user.Team))
                            {
                                cmd.CommandText = "INSERT INTO teams (Name) VALUES (@Team); SELECT LAST_INSERT_ID();";
                                cmd.Parameters.Clear();
                                cmd.Parameters.AddWithValue("@Team", user.Team);
                                teamId = Convert.ToInt32(cmd.ExecuteScalar());
                            }

                            // Insert the new user and capture the generated user ID.
                            cmd.CommandText = "INSERT INTO users (Email, PasswordHash, TeamID) VALUES (@Email, @PasswordHash, @TeamID); SELECT LAST_INSERT_ID();";
                            cmd.Parameters.Clear();
                            cmd.Parameters.AddWithValue("@Email", user.Email);
                            cmd.Parameters.AddWithValue("@PasswordHash", user.PasswordHash);
                            cmd.Parameters.AddWithValue("@TeamID", teamId);
                            int newUserId = Convert.ToInt32(cmd.ExecuteScalar());

                            // ---
                            // Copy puzzle progress rows from the old table to the new table,
                            // replacing the user_id with the new user's id.
                            // NOTE: Adjust the source and destination table names as needed.
                            // Here we assume the old table is named "old_puzzleprogress" and the new table is "puzzleprogress".
                            // ---
                            cmd.CommandText =
                                "INSERT INTO puzzleprogress (user_id, puzzle_id, team_id) " +
                                "SELECT @NewUserId, id, @teamId " +
                                "FROM puzzles";
                            cmd.Parameters.Clear();
                            cmd.Parameters.AddWithValue("@NewUserId", newUserId);
                            cmd.Parameters.AddWithValue("@teamId", teamId);
                            cmd.ExecuteNonQuery();

                            // Mark the access code as used.
                            cmd.CommandText = "UPDATE AccessCodes SET Used = 1 WHERE AccessCode = @AccessCode";
                            cmd.Parameters.Clear();
                            cmd.Parameters.AddWithValue("@AccessCode", user.AccessCode);
                            cmd.ExecuteNonQuery();

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
