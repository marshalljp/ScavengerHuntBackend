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
                            // For team-based queries: join using team_id and aggregate progress.
                            cmd.CommandText = @"
                                SELECT 
                                    p.Title,
                                    p.Id as Id,
                                    COALESCE(SUM(pp.progress), 0) AS progress,
                                    CASE 
                                        WHEN COUNT(pp.puzzle_id) = 0 THEN 'not-started'
                                        ELSE MAX(pp.status)
                                    END AS status
                                FROM scavengerhunt.puzzles AS p
                                LEFT JOIN scavengerhunt.puzzleprogress AS pp 
                                    ON p.id = pp.puzzle_id AND pp.team_id = @teamId
                                GROUP BY p.id, p.Title";
                            cmd.Parameters.AddWithValue("@teamId", teamId);
                        }
                        else
                        {
                            // For user-based queries: join using user_id and aggregate progress.
                            cmd.CommandText = @"
                                SELECT 
                                    p.Title,
                                    p.Id as Id,
                                    COALESCE(SUM(pp.progress), 0) AS progress,
                                    CASE 
                                        WHEN COUNT(pp.puzzle_id) = 0 THEN 'not-started'
                                        ELSE MAX(pp.status)
                                    END AS status
                                FROM scavengerhunt.puzzles AS p
                                LEFT JOIN scavengerhunt.puzzleprogress AS pp 
                                    ON p.id = pp.puzzle_id AND pp.user_id = @userId
                                GROUP BY p.id, p.Title";
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
                        cmd.Parameters.AddWithValue("@is_completed", 0);
                        cmd.Parameters.AddWithValue("@status", "in-progress");
                        cmd.Parameters.AddWithValue("@team_id", teamId);
                        await cmd.ExecuteNonQueryAsync();

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
                var email = CommonUtils.GetUserEmail(User);
                var userId = CommonUtils.GetUserID(User, _configuration);
                int teamId = Int32.Parse(CommonUtils.GetTeamUserID(User, _configuration));
                var connString = _configuration.GetConnectionString("DefaultConnection");

                // Flag to determine if any progress rows were returned.
                bool foundProgressRow = false;

                using (MySqlConnection conn = new MySqlConnection(connString))
                {
                    await conn.OpenAsync();

                    // Query the progress values.
                    using (MySqlCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandType = CommandType.Text;
                        cmd.CommandText = @"
                    SELECT progress 
                    FROM scavengerhunt.puzzleprogress 
                    JOIN puzzlesdetails 
                      ON puzzlesdetails.puzzleidorder = puzzleprogress.puzzleidorder 
                    WHERE user_id = @userId 
                      AND requiredForSeed = 1";
                        cmd.Parameters.AddWithValue("@userId", userId);

                        using (var rdr = await cmd.ExecuteReaderAsync())
                        {
                            while (await rdr.ReadAsync())
                            {
                                foundProgressRow = true;
                                int progressIndex = rdr.GetOrdinal("progress");
                                // Check if the progress field is null.
                                if (rdr.IsDBNull(progressIndex))
                                {
                                    return Ok(new { success = false, message = "Progress value missing" });
                                }

                                int progressValue = rdr.GetInt32(progressIndex);
                                // If any progress value is 0, progress is incomplete.
                                if (progressValue == 0)
                                {
                                    return Ok(new { success = false, message = "Progress incomplete" });
                                }
                            }
                        }
                    }

                    // If no rows were returned, then there are no progress records—so progress is incomplete.
                    if (!foundProgressRow)
                    {
                        return Ok(new { success = false, message = "Progress incomplete: no progress records found" });
                    }

                    // If progress exists and is complete (nonzero), retrieve the seed word.
                    string seed = null;
                    using (MySqlCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandType = CommandType.Text;
                        cmd.CommandText = "SELECT seed FROM scavengerhunt.seeds WHERE puzzle_id = @id";
                        cmd.Parameters.AddWithValue("@id", id);

                        using (var rdr = await cmd.ExecuteReaderAsync())
                        {
                            if (await rdr.ReadAsync())
                            {
                                seed = rdr["seed"]?.ToString();
                            }
                        }
                    }

                    await conn.CloseAsync();

                    if (!string.IsNullOrEmpty(seed))
                    {
                        return Ok(new { success = true, seed = seed });
                    }
                    else
                    {
                        return Ok(new { success = false, message = "Seed not found." });
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
