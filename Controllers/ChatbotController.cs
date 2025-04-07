using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using ScavengerHuntBackend.Utils;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace ScavengerHuntBackend.Controllers
{
    [Route("chatbot")]
    [ApiController]
    [Authorize]
    public class ChatbotController : ControllerBase
    {
        private readonly ScavengerHuntContext _context;
        private readonly IConfiguration _configuration;

        public ChatbotController(ScavengerHuntContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        [HttpGet("hint")]
        public async Task<IActionResult> GetHint([FromQuery] string puzzleId)
        {
            try
            {
                // Retrieve user info if needed.
                var email = CommonUtils.GetUserEmail(User);
                var userId = CommonUtils.GetUserID(User, _configuration);

                var connString = _configuration.GetConnectionString("DefaultConnection");
                string chosenHint = "";
                int hintFlagToUpdate = 0; // 1, 2, or 3, depending on which hint is selected

                using (MySqlConnection conn = new MySqlConnection(connString))
                {
                    await conn.OpenAsync();

                    // Query: Select the hint flags and corresponding text columns for the given puzzle.
                    using (MySqlCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandType = CommandType.Text;
                        cmd.CommandText = @"
                            SELECT hint1, hint2, hint3, hintText1, hintText2, hintText3 
                            FROM scavengerhunt.hints 
                            WHERE puzzleid = @id;";
                        cmd.Parameters.AddWithValue("@id", puzzleId);

                        using (var rdr = await cmd.ExecuteReaderAsync())
                        {
                            if (await rdr.ReadAsync())
                            {
                                // Read the flag values.
                                int flag1 = rdr.GetInt32(rdr.GetOrdinal("hint1"));
                                int flag2 = rdr.GetInt32(rdr.GetOrdinal("hint2"));
                                int flag3 = rdr.GetInt32(rdr.GetOrdinal("hint3"));

                                // Select the first available hint.
                                if (flag1 == 0)
                                {
                                    chosenHint = rdr["hintText1"].ToString();
                                    hintFlagToUpdate = 1;
                                }
                                else if (flag2 == 0)
                                {
                                    chosenHint = rdr["hintText2"].ToString();
                                    hintFlagToUpdate = 2;
                                }
                                else if (flag3 == 0)
                                {
                                    chosenHint = rdr["hintText3"].ToString();
                                    hintFlagToUpdate = 3;
                                }
                                else
                                {
                                    return NotFound("No hint available for this puzzle.");
                                }
                            }
                            else
                            {
                                return NotFound("No hints left found for this puzzle.");
                            }
                        }
                    }

                    // If a hint was selected, update its flag so it won’t be used again.
                    if (hintFlagToUpdate > 0)
                    {
                        using (MySqlCommand updateCmd = conn.CreateCommand())
                        {
                            updateCmd.CommandType = CommandType.Text;
                            updateCmd.CommandText = $"UPDATE scavengerhunt.hints SET hint{hintFlagToUpdate} = 1 WHERE puzzleid = @id;";
                            updateCmd.Parameters.AddWithValue("@id", puzzleId);
                            await updateCmd.ExecuteNonQueryAsync();
                        }
                    }
                }

                return Ok(new { success = true, hint = chosenHint });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }
    }
}
