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
using System.Security.Claims;


namespace ScavengerHuntBackend.Controllers
{
    [Route("puzzle")]
    [ApiController]
    [Authorize] // Uncomment this once you enable authentication.
    public class PuzzlesController : ControllerBase
    {
        private readonly ScavengerHuntContext _context;
        private readonly IConfiguration _configuration;

        public PuzzlesController(ScavengerHuntContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        [HttpGet]
        public async Task<IActionResult> GetPuzzles()
        {
            try
            {
                int teamId = Int32.Parse(CommonUtils.GetTeamUserID(User, _configuration));
                int userId = Int32.Parse(CommonUtils.GetUserID(User, _configuration));
                var connString = _configuration.GetConnectionString("DefaultConnection");
                var puzzles = new List<Dictionary<string, object>>();

                using (MySqlConnection conn = new MySqlConnection(connString))
                {
                    await conn.OpenAsync();
                    using (MySqlCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandType = CommandType.Text;

                        if (teamId != 9999)
                        {
                            // Team-based query with completed-percentage progress
                            cmd.CommandText = @"
                           SELECT 
                                p.Title,
                                p.Id AS Id,

                                -- Progress = percentage of rows completed
                                CASE 
                                    WHEN COUNT(pp.id) = 0 THEN 0
                                    ELSE ROUND(SUM(pp.is_completed = 1) / COUNT(pp.id) * 100)
                                END AS progress,

                                -- Status based on is_completed
                                CASE
                                    WHEN COUNT(pp.id) = 0 THEN 'not-started'
                                    WHEN SUM(pp.is_completed = 1) = 0 THEN 'not-started'
                                    WHEN SUM(pp.is_completed = 1) > 0 AND SUM(pp.is_completed = 0) > 0 THEN 'in-progress'
                                    WHEN SUM(pp.is_completed = 1) = COUNT(pp.id) THEN 'completed'
                                    ELSE 'not-started'
                                END AS status,

                                -- Total score
                                COALESCE(SUM(pp.progress), 0) AS score

                            FROM scavengerhunt.puzzles AS p
                            LEFT JOIN scavengerhunt.puzzleprogress AS pp 
                                ON p.id = pp.puzzle_id
                                AND pp.user_id IN (SELECT id FROM users WHERE teamid = @teamId)
                            GROUP BY p.id, p.Title;";
                            cmd.Parameters.AddWithValue("@teamId", teamId);
                        }
                        else
                        {
                            // User-based query with completed-percentage progress
                            cmd.CommandText = @"
                            SELECT 
                                p.Title,
                                p.Id AS Id,

                                -- Progress = percentage of rows completed
                                CASE 
                                    WHEN COUNT(pp.id) = 0 THEN 0
                                    ELSE ROUND(SUM(pp.is_completed = 1) / COUNT(pp.id) * 100)
                                END AS progress,

                                -- Status based on is_completed
                                CASE
                                    WHEN COUNT(pp.id) = 0 THEN 'not-started'
                                    WHEN SUM(pp.is_completed = 1) = 0 THEN 'not-started'
                                    WHEN SUM(pp.is_completed = 1) > 0 AND SUM(pp.is_completed = 0) > 0 THEN 'in-progress'
                                    WHEN SUM(pp.is_completed = 1) = COUNT(pp.id) THEN 'completed'
                                    ELSE 'not-started'
                                END AS status,

                                -- Total score
                                COALESCE(SUM(pp.progress), 0) AS score

                            FROM scavengerhunt.puzzles AS p
                            LEFT JOIN scavengerhunt.puzzleprogress AS pp 
                                ON p.id = pp.puzzle_id AND pp.user_id = @userId
                            GROUP BY p.id, p.Title;
";
                            cmd.Parameters.AddWithValue("@userId", userId);
                        }

                        using (var rdr = await cmd.ExecuteReaderAsync())
                        {
                            while (await rdr.ReadAsync())
                            {
                                var puzzleDetail = new Dictionary<string, object>();
                                for (int i = 0; i < rdr.FieldCount; i++)
                                {
                                    string columnName = rdr.GetName(i);
                                    object columnValue = rdr.GetValue(i);
                                    puzzleDetail[columnName] = columnValue;
                                }
                                puzzles.Add(puzzleDetail);
                            }
                        }
                    }

                    await conn.CloseAsync();
                }

                return Ok(new { success = true, puzzles = puzzles });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // GET: api/puzzles/{id}
        // Returns details for a specific puzzle (with subpuzzles), excluding sensitive data like the correct answer.
        // SQL type field:
        // 0: Default, text field, submit answer
        // 1: Multiple Choice
        // 2: AR answer
        // 3: QR code scan order
        [HttpGet("{id}")]
        public async Task<IActionResult> GetPuzzle(int id)
        {
            try
            {
                var email = CommonUtils.GetUserEmail(User);
                var userId = CommonUtils.GetUserID(User, _configuration);
                int teamId = Int32.Parse(CommonUtils.GetTeamUserID(User, _configuration));
                var connString = _configuration.GetConnectionString("DefaultConnection");
                Dictionary<string, object> mainPuzzle = null;
                var subpuzzles = new List<Dictionary<string, object>>();
                var progressList = new List<Dictionary<string, object>>();

                using (MySqlConnection conn = new MySqlConnection(connString))
                {
                    await conn.OpenAsync();

                    // Query 1: Get main puzzle details (assume main puzzle has puzzleidorder = 0).
                    using (MySqlCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandType = CommandType.Text;
                        cmd.CommandText = "SELECT * FROM scavengerhunt.puzzlesdetails WHERE puzzleid = @id AND puzzleidorder = 0;";
                        cmd.Parameters.AddWithValue("@id", id);
                        using (var rdr = await cmd.ExecuteReaderAsync())
                        {
                            if (await rdr.ReadAsync())
                            {
                                mainPuzzle = new Dictionary<string, object>();
                                for (int i = 0; i < rdr.FieldCount; i++)
                                {
                                    string columnName = rdr.GetName(i);
                                    object columnValue = rdr.GetValue(i);
                                    mainPuzzle[columnName] = columnValue;
                                }
                            }
                        }
                    }

                    // Query 2: Get subpuzzle details (rows with puzzleidorder > 0).
                    using (MySqlCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandType = CommandType.Text;
                        cmd.CommandText = "SELECT * FROM scavengerhunt.puzzlesdetails WHERE puzzleid = @id AND puzzleidorder > 0 ORDER BY puzzleidorder;";
                        cmd.Parameters.AddWithValue("@id", id);
                        using (var rdr = await cmd.ExecuteReaderAsync())
                        {
                            while (await rdr.ReadAsync())
                            {
                                var subpuzzle = new Dictionary<string, object>();
                                for (int i = 0; i < rdr.FieldCount; i++)
                                {
                                    string columnName = rdr.GetName(i);
                                    object columnValue = rdr.GetValue(i);
                                    subpuzzle[columnName] = columnValue;
                                }
                                subpuzzles.Add(subpuzzle);
                            }
                        }
                    }

                    // Query 3: Get puzzle progress for the user.
                    using (MySqlCommand cmd = conn.CreateCommand())
                    {
                        if (teamId != 9999)
                        {
                            cmd.CommandText = "SELECT * FROM scavengerhunt.puzzleprogress WHERE team_id = @teamId";
                            cmd.Parameters.AddWithValue("@teamId", teamId);
                        }
                        else
                        {
                            cmd.CommandText = "SELECT * FROM scavengerhunt.puzzleprogress WHERE user_id = @teamId";
                            cmd.Parameters.AddWithValue("@teamId", userId);
                        }

                        using (var rdr = await cmd.ExecuteReaderAsync())
                        {
                            while (await rdr.ReadAsync())
                            {
                                var progressDetail = new Dictionary<string, object>();
                                for (int i = 0; i < rdr.FieldCount; i++)
                                {
                                    string columnName = rdr.GetName(i);
                                    object columnValue = rdr.GetValue(i);
                                    progressDetail[columnName] = columnValue;
                                }
                                progressList.Add(progressDetail);
                            }
                        }
                    }

                    await conn.CloseAsync();
                }

                if (mainPuzzle == null)
                {
                    return NotFound("Main puzzle not found.");
                }

                // Merge progress data into main puzzle if available.
                var puzzleIdStr = mainPuzzle["Id"]?.ToString();
                var progressRecord = progressList.FirstOrDefault(
                    pr => pr["puzzle_id"]?.ToString() == puzzleIdStr);
                if (progressRecord != null)
                {
                    foreach (var kv in progressRecord)
                    {
                        mainPuzzle[kv.Key] = kv.Value;
                    }
                }
                else
                {
                    mainPuzzle["status"] = "not-started";
                    mainPuzzle["progress"] = 0;
                }

                // Attach the subpuzzles array to the main puzzle.
                mainPuzzle["subpuzzles"] = subpuzzles;

                // Return the result with one puzzle object in the puzzles array.
                return Ok(new { success = true, puzzles = new List<Dictionary<string, object>> { mainPuzzle } });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }


        // POST: api/puzzles/submit
        // Submits an answer for a puzzle and records the submission.
        [HttpPost("submit")]
        public async Task<IActionResult> SubmitPuzzle([FromBody] Submission submission)
        {
            // Get user information (if needed)
            var email = CommonUtils.GetUserEmail(User);
            var userId = CommonUtils.GetUserID(User, _configuration);
            var teamId = CommonUtils.GetTeamUserID(User, _configuration);
            string storedHash = null;
            string message = null;
            var connString = _configuration.GetConnectionString("DefaultConnection");

            using (MySqlConnection conn = new MySqlConnection(connString))
            {
                await conn.OpenAsync();
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandType = CommandType.Text;
                    cmd.CommandText = @"
                        SELECT AnswerHash, message
                        FROM scavengerhunt.puzzlesdetails
                        WHERE puzzleid = @puzzleId
                          AND puzzleidorder = @subpuzzleId
                        LIMIT 1;";
                    cmd.Parameters.AddWithValue("@puzzleId", submission.puzzleId);
                    cmd.Parameters.AddWithValue("@subpuzzleId", submission.subpuzzleId);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            storedHash = reader["AnswerHash"].ToString();
                            message = reader["message"].ToString();
                        }
                        else
                        {
                            return NotFound("Puzzle or sub-puzzle not found.");
                        }
                    }
                }
            }
            // Normalize the answer before verifying
            var normalizedAnswer = submission.answer.Trim().ToLower();
            submission.IsCorrect = BCrypt.Net.BCrypt.Verify(normalizedAnswer, storedHash);

            _context.Submissions.Add(submission);
            // If the answer is correct, insert or update the puzzleprogress table.
            if (submission.IsCorrect)
            {
                using (MySqlConnection conn = new MySqlConnection(connString))
                {
                    await conn.OpenAsync();
                    using (MySqlCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                            UPDATE puzzleprogress AS pp
                            SET progress = @progress +
                                (CASE 
                                    WHEN (
                                        SELECT IFNULL(SUM(p.progress), 0)
                                        FROM (SELECT * FROM puzzleprogress) AS p
                                        WHERE p.puzzle_id = @puzzleId
                                          AND p.puzzleidorder = @puzzleidorder
                                          AND p.id <> pp.id
                                    ) = 0 THEN 10 ELSE 0 END),
                                is_completed = @is_completed,
                                status = @status,
                                team_id = @team_id
                            WHERE user_id = @userId
                              AND puzzle_id = @puzzleId
                              AND puzzleidorder = @puzzleidorder;";
                        cmd.Parameters.Clear();
                        cmd.Parameters.AddWithValue("@userId", userId);
                        cmd.Parameters.AddWithValue("@puzzleId", submission.puzzleId);
                        cmd.Parameters.AddWithValue("@puzzleidorder", submission.subpuzzleId);
                        cmd.Parameters.AddWithValue("@progress", 10);
                        cmd.Parameters.AddWithValue("@is_completed", 1);
                        cmd.Parameters.AddWithValue("@status", "completed");
                        cmd.Parameters.AddWithValue("@team_id", teamId);
                        await cmd.ExecuteNonQueryAsync();
                        if(CommonUtils.GS_internal(User, _configuration, submission.puzzleId))
                        {
                            string seed = null;
                            using (MySqlCommand cmd1 = conn.CreateCommand())
                            {
                                cmd1.CommandType = CommandType.Text;
                                cmd1.CommandText = "SELECT seed FROM scavengerhunt.seeds WHERE puzzle_id = @id";
                                cmd1.Parameters.AddWithValue("@id", submission.puzzleId);

                                using (var rdr1 = cmd1.ExecuteReader())
                                {
                                    if (rdr1.Read())
                                    {
                                        seed = rdr1["seed"]?.ToString();
                                    }
                                }
                            }

                            CommonUtils.AddNotification(User, _configuration, "You have unlocked an seed word: " + seed);
                        }
                    }
                }
                return Ok(new { correct = submission.IsCorrect, message = message });
            }

            //await _context.SaveChangesAsync();

            return Ok(new { correct = submission.IsCorrect });
        }

        [HttpGet("gs/{id}")]
        public async Task<IActionResult> GS(int id)
        {
            try
            {
                var userId = CommonUtils.GetUserID(User, _configuration);
                var connString = _configuration.GetConnectionString("DefaultConnection");

                bool hasRequiredRows = false;

                using (var conn = new MySqlConnection(connString))
                {
                    await conn.OpenAsync();

                    // --------------------------------------------------
                    // 1. Check required-for-seed completion for THIS puzzle
                    // --------------------------------------------------
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandType = CommandType.Text;
                        cmd.CommandText = @"
                    SELECT pp.is_completed
                    FROM scavengerhunt.puzzleprogress pp
                    JOIN scavengerhunt.puzzlesdetails pd
                        ON pd.puzzleidorder = pp.puzzleidorder
                    WHERE pp.user_id = @userId
                      AND pp.puzzle_id = @puzzleid
                      AND pd.requiredForSeed = 1;
                ";

                        cmd.Parameters.AddWithValue("@userId", userId);
                        cmd.Parameters.AddWithValue("@puzzleid", id);

                        using (var rdr = await cmd.ExecuteReaderAsync())
                        {
                            while (await rdr.ReadAsync())
                            {
                                hasRequiredRows = true;

                                int isCompleted = rdr.GetInt32(0);

                                // ? Any incomplete required step blocks the seed
                                if (isCompleted == 0)
                                {
                                    return Ok(new
                                    {
                                        success = false,
                                        message = "Required steps not completed"
                                    });
                                }
                            }
                        }
                    }

                    // ? No required-for-seed rows exist for this puzzle
                    if (!hasRequiredRows)
                    {
                        return Ok(new
                        {
                            success = false,
                            message = "No required steps found"
                        });
                    }

                    // --------------------------------------------------
                    // 2. All required steps complete ? fetch seed
                    // --------------------------------------------------
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandType = CommandType.Text;
                        cmd.CommandText = @"
                    SELECT seed
                    FROM scavengerhunt.seeds
                    WHERE puzzle_id = @puzzleid;
                ";

                        cmd.Parameters.AddWithValue("@puzzleid", id);

                        var seed = (await cmd.ExecuteScalarAsync())?.ToString();

                        if (!string.IsNullOrEmpty(seed))
                        {
                            return Ok(new
                            {
                                success = true,
                                seed = seed
                            });
                        }

                        return Ok(new
                        {
                            success = false,
                            message = "Seed not found"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }




    }


}
