using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using ScavengerHuntBackend.Models;
using ScavengerHuntBackend.Utils;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace ScavengerHuntBackend.Controllers
{
    [ApiController]
    [Authorize]
    public class LeaderboardController : ControllerBase
    {

        private readonly ScavengerHuntContext _context;
        private readonly IConfiguration _configuration;
        public LeaderboardController(ScavengerHuntContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        [HttpGet("leaderboard")]
        public async Task<IActionResult> GetGroupLeaderboard()
        {
            try
            {
                var connString = _configuration.GetConnectionString("DefaultConnection");
                // Dictionary to store per-puzzle details for each team (key: team_id).
                var teamPuzzleDetails = new Dictionary<int, List<object>>();
                // List to store overall leaderboard results.
                var leaderboard = new List<object>();

                using (MySqlConnection conn = new MySqlConnection(connString))
                {
                    await conn.OpenAsync();

                    // Query 1: Retrieve the individual puzzle scores per team.
                    string detailsQuery = @"
                        SELECT 
                          pp.team_id,
                          pp.puzzle_id,
                          p.Title,
                          MAX(
                            CASE 
                              WHEN pp.is_completed = 1 THEN pp.progress + 20
                              ELSE pp.progress 
                            END
                          ) AS puzzle_score
                        FROM puzzleprogress pp
                        JOIN puzzles p ON p.Id = pp.puzzle_id
                        GROUP BY pp.team_id, pp.puzzle_id, p.Title
                        ORDER BY pp.team_id, pp.puzzle_id;
                        ";

                    using (MySqlCommand detailsCmd = new MySqlCommand(detailsQuery, conn))
                    {
                        using (MySqlDataReader detailsReader = await detailsCmd.ExecuteReaderAsync())
                        {
                            while (await detailsReader.ReadAsync())
                            {
                                int teamId = detailsReader.GetInt32("team_id");
                                int puzzleId = detailsReader.GetInt32("puzzle_id");
                                int puzzleScore = detailsReader.GetInt32("puzzle_score");
                                string puzzleTitle = detailsReader.GetString("Title");

                                var puzzleDetail = new { puzzleId, puzzleScore, puzzleTitle };

                                if (!teamPuzzleDetails.ContainsKey(teamId))
                                {
                                    teamPuzzleDetails[teamId] = new List<object>();
                                }
                                teamPuzzleDetails[teamId].Add(puzzleDetail);
                            }
                        }
                    }

                    // Query 2: Retrieve the overall team scores along with team names.
                    string overallQuery = @"
                        SELECT t.Id AS team_id, t.Name AS team_name, SUM(progress_per_puzzle) AS team_score
                        FROM (
                            SELECT team_id, puzzle_id,
                                   MAX(
                                       CASE 
                                           WHEN is_completed = 1 THEN progress + 20
                                           ELSE progress 
                                       END
                                   ) AS progress_per_puzzle
                            FROM puzzleprogress
                            GROUP BY team_id, puzzle_id
                        ) AS per_puzzle
                        JOIN teams t ON t.Id = per_puzzle.team_id
                        GROUP BY t.Id, t.Name
                        ORDER BY team_score DESC;";

                    using (MySqlCommand overallCmd = new MySqlCommand(overallQuery, conn))
                    {
                        using (MySqlDataReader overallReader = await overallCmd.ExecuteReaderAsync())
                        {
                            while (await overallReader.ReadAsync())
                            {
                                int teamId = overallReader.GetInt32("team_id");
                                string teamName = overallReader.GetString("team_name");
                                int teamScore = overallReader.GetInt32("team_score");

                                // Retrieve puzzle details if available.
                                teamPuzzleDetails.TryGetValue(teamId, out var puzzles);

                                leaderboard.Add(new
                                {
                                    teamId,
                                    teamName,
                                    teamScore,
                                    puzzles = puzzles ?? new List<object>()
                                });
                            }
                        }
                    }
                }

                return Ok(new { success = true, leaderboard = leaderboard });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }


    }
}