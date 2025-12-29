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

        public class PuzzleInfo
        {
            public int PuzzleId { get; set; }
            public string PuzzleTitle { get; set; }
            public int PuzzleScore { get; set; }
            public string Status { get; set; }
            public int Progress { get; set; }
            public int PuzzleIdOrder { get; set; } // corrected casing
        }

        [HttpGet("leaderboard")]
        public async Task<IActionResult> GetGroupLeaderboard()
        {
            try
            {
                var connString = _configuration.GetConnectionString("DefaultConnection");
                var teamDict = new Dictionary<int, dynamic>();

                using (var conn = new MySqlConnection(connString))
                {
                    await conn.OpenAsync();

                    string query = @"
                        SELECT 
                            t.Id AS team_id,
                            t.Name AS team_name,
                            p.Id AS puzzle_id,
                            p.Title AS puzzle_title,
                            COALESCE(MIN(pd.puzzleidorder), 0) AS puzzleidorder,
                            CASE 
                                WHEN COUNT(pp.id) = 0 THEN 0
                                ELSE ROUND(SUM(pp.is_completed = 1) / COUNT(pp.id) * 100)
                            END AS progress,
                            CASE
                                WHEN COUNT(pp.id) = 0 THEN 'not-started'
                                WHEN SUM(pp.is_completed = 1) = 0 THEN 'not-started'
                                WHEN SUM(pp.is_completed = 1) > 0 AND SUM(pp.is_completed = 0) > 0 THEN 'in-progress'
                                WHEN SUM(pp.is_completed = 1) = COUNT(pp.id) THEN 'completed'
                                ELSE 'not-started'
                            END AS status,
                            COALESCE(SUM(pp.progress), 0) AS puzzle_score
                        FROM teams t
                        CROSS JOIN puzzles p
                        LEFT JOIN puzzleprogress pp 
                            ON pp.puzzle_id = p.Id
                            AND pp.user_id IN (SELECT id FROM users WHERE teamid = t.Id)
                        LEFT JOIN puzzlesdetails pd
                            ON pd.puzzleid = p.Id 
                            AND pd.puzzleidorder = pp.puzzleidorder  -- ? THIS FIXES THE DUPLICATION
                        GROUP BY t.Id, t.Name, p.Id, p.Title
                        ORDER BY t.Id, p.Id;
                                    ";

                    using (var cmd = new MySqlCommand(query, conn))
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            int teamId = reader.GetInt32("team_id");
                            string teamName = reader.GetString("team_name");

                            var puzzle = new PuzzleInfo
                            {
                                PuzzleId = reader.GetInt32("puzzle_id"),
                                PuzzleTitle = reader.GetString("puzzle_title"),
                                PuzzleScore = reader.GetInt32("puzzle_score"),
                                Status = reader.GetString("status"),
                                Progress = reader.GetInt32("progress"),
                                PuzzleIdOrder = reader.GetInt32("puzzleidOrder") // correct casing
                            };

                            if (!teamDict.ContainsKey(teamId))
                            {
                                teamDict[teamId] = new
                                {
                                    teamId,
                                    teamName,
                                    teamScore = 0,
                                    puzzles = new List<PuzzleInfo>()
                                };
                            }

                            var team = teamDict[teamId];
                            var puzzles = ((List<PuzzleInfo>)team.puzzles);
                            puzzles.Add(puzzle);

                            teamDict[teamId] = new
                            {
                                teamId,
                                teamName,
                                teamScore = team.teamScore + puzzle.PuzzleScore,
                                puzzles
                            };
                        }
                    }

                    // Filter teams with score > 0, order by teamScore descending,
                    // and order each team's puzzles by PuzzleIdOrder ascending
                    var leaderboard = teamDict.Values
                        .Where(t => t.teamScore > 0)
                        .Select(t => new
                        {
                            t.teamId,
                            t.teamName,
                            t.teamScore,
                            puzzles = ((List<PuzzleInfo>)t.puzzles)
                                        .OrderBy(p => p.PuzzleIdOrder)
                                        .ToList()
                        })
                        .OrderByDescending(t => t.teamScore)
                        .ToList();

                    return Ok(new { success = true, leaderboard });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }






    }
}