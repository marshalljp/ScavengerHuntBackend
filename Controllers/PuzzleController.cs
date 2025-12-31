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

                                    -- Progress = percentage of REAL subpuzzles completed (exclude puzzleidorder = 0)
                                    CASE 
                                        WHEN COUNT(CASE WHEN pp.puzzleidorder != 0 THEN pp.id END) = 0 THEN 0
                                        ELSE ROUND(
                                            SUM(CASE WHEN pp.puzzleidorder != 0 AND pp.is_completed = 1 THEN 1 ELSE 0 END) 
                                            / COUNT(CASE WHEN pp.puzzleidorder != 0 THEN pp.id END) * 100
                                        )
                                    END AS progress,

                                    -- Status based only on real subpuzzles
                                    CASE
                                        WHEN COUNT(CASE WHEN pp.puzzleidorder != 0 THEN pp.id END) = 0 THEN 'not-started'
                                        WHEN SUM(CASE WHEN pp.puzzleidorder != 0 AND pp.is_completed = 1 THEN 1 ELSE 0 END) = 0 THEN 'not-started'
                                        WHEN SUM(CASE WHEN pp.puzzleidorder != 0 AND pp.is_completed = 1 THEN 1 ELSE 0 END) > 0 
                                          AND SUM(CASE WHEN pp.puzzleidorder != 0 AND pp.is_completed = 0 THEN 1 ELSE 0 END) > 0 THEN 'in-progress'
                                        WHEN SUM(CASE WHEN pp.puzzleidorder != 0 AND pp.is_completed = 1 THEN 1 ELSE 0 END) = 
                                             COUNT(CASE WHEN pp.puzzleidorder != 0 THEN pp.id END) THEN 'completed'
                                        ELSE 'not-started'
                                    END AS status,

                                    -- Total score: only sum progress from real subpuzzles (exclude puzzleidorder = 0)
                                    COALESCE(SUM(CASE WHEN pp.puzzleidorder != 0 THEN pp.progress ELSE 0 END), 0) AS score

                                FROM scavengerhunt.puzzles AS p
                                LEFT JOIN scavengerhunt.puzzleprogress AS pp 
                                    ON p.id = pp.puzzle_id
                                    AND pp.user_id IN (SELECT id FROM users WHERE teamid = @teamId)
                                GROUP BY p.id, p.Title
                                ORDER BY p.id;";
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
            var userId = CommonUtils.GetUserID(User, _configuration);
            var teamId = CommonUtils.GetTeamUserID(User, _configuration);
            var connString = _configuration.GetConnectionString("DefaultConnection");

            string storedHash = null;
            string message = null;
            bool requiredForSeed = false;

            // Track if this subpuzzle has been solved by ANY team yet
            bool anyTeamHasSolvedThisSubpuzzle = false;
            // Track if OUR team has already scored points on this subpuzzle
            bool ourTeamAlreadyScoredThisSubpuzzle = false;

            using (MySqlConnection conn = new MySqlConnection(connString))
            {
                await conn.OpenAsync();

                // 1. Get puzzle details
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                SELECT AnswerHash, message, requiredForSeed
                FROM scavengerhunt.puzzlesdetails
                WHERE puzzleid = @puzzleId
                  AND puzzleidorder = @subpuzzleId
                LIMIT 1;";

                    cmd.Parameters.AddWithValue("@puzzleId", submission.puzzleId);
                    cmd.Parameters.AddWithValue("@subpuzzleId", submission.subpuzzleId);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (!await reader.ReadAsync())
                            return NotFound("Puzzle or sub-puzzle not found.");

                        storedHash = reader["AnswerHash"]?.ToString();
                        message = reader["message"]?.ToString();
                        requiredForSeed = Convert.ToBoolean(reader["requiredForSeed"]);
                    }
                }

                // 2. Check if ANY team has already solved this subpuzzle (for first-solve bonus)
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                SELECT EXISTS(
                    SELECT 1 
                    FROM puzzleprogress 
                    WHERE puzzle_id = @puzzleId 
                      AND puzzleidorder = @subpuzzleId 
                      AND progress > 0
                    LIMIT 1
                ) AS already_solved;";

                    cmd.Parameters.AddWithValue("@puzzleId", submission.puzzleId);
                    cmd.Parameters.AddWithValue("@subpuzzleId", submission.subpuzzleId);

                    anyTeamHasSolvedThisSubpuzzle = Convert.ToBoolean(await cmd.ExecuteScalarAsync());
                }

                // 3. Check if OUR team has already earned points on this subpuzzle
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                SELECT COALESCE(SUM(progress), 0) AS team_progress
                FROM puzzleprogress
                WHERE puzzle_id = @puzzleId
                  AND puzzleidorder = @subpuzzleId
                  AND user_id IN (
                      SELECT id FROM users WHERE teamid = @teamId
                  );";

                    cmd.Parameters.AddWithValue("@puzzleId", submission.puzzleId);
                    cmd.Parameters.AddWithValue("@subpuzzleId", submission.subpuzzleId);
                    cmd.Parameters.AddWithValue("@teamId", teamId);

                    ourTeamAlreadyScoredThisSubpuzzle = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
                }
            }

            // 4. Verify the answer
            var normalizedAnswer = submission.answer.Trim().ToLower();
            submission.IsCorrect = !string.IsNullOrEmpty(storedHash) &&
                                   BCrypt.Net.BCrypt.Verify(normalizedAnswer, storedHash);

            // 5. Always record the submission
            _context.Submissions.Add(submission);
            //await _context.SaveChangesAsync();

            // 6. Update puzzleprogress for this user
            using (MySqlConnection conn = new MySqlConnection(connString))
            {
                await conn.OpenAsync();
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
            UPDATE puzzleprogress
            SET progress = @progress,
                is_completed = @isCompleted,
                status = @status,
                team_id = @team_id
            WHERE user_id = @userId
              AND puzzle_id = @puzzleId
              AND puzzleidorder = @subpuzzleId;";

                    int newUserProgress = 0;
                    bool newIsCompleted = false;
                    string newStatus = "in-progress";

                    if (submission.IsCorrect)
                    {
                        newUserProgress = 10;

                        // First-to-solve global bonus: +10
                        if (!anyTeamHasSolvedThisSubpuzzle)
                        {
                            newUserProgress += 10; // Total 20
                        }

                        // Safety: if team already scored, award 0
                        if (ourTeamAlreadyScoredThisSubpuzzle)
                        {
                            newUserProgress = 0;
                        }

                        newIsCompleted = true;
                        newStatus = "completed";
                    }
                    else
                    {
                        newUserProgress = 0;

                        if (!requiredForSeed)
                        {
                            newIsCompleted = true;
                            newStatus = "completed";
                        }
                        // else: retry allowed ? stay incomplete
                    }

                    cmd.Parameters.AddWithValue("@userId", userId);
                    cmd.Parameters.AddWithValue("@puzzleId", submission.puzzleId);
                    cmd.Parameters.AddWithValue("@subpuzzleId", submission.subpuzzleId);
                    cmd.Parameters.AddWithValue("@progress", newUserProgress);
                    cmd.Parameters.AddWithValue("@isCompleted", newIsCompleted ? 1 : 0);
                    cmd.Parameters.AddWithValue("@status", newStatus);
                    cmd.Parameters.AddWithValue("@team_id", teamId);

                    await cmd.ExecuteNonQueryAsync();
                }
            }

            // 7. Seed unlock
            if (submission.IsCorrect && CommonUtils.GS_internal(User, _configuration, submission.puzzleId))
            {
                using (MySqlConnection conn = new MySqlConnection(connString))
                {
                    await conn.OpenAsync();
                    using (MySqlCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT seed FROM scavengerhunt.seeds WHERE puzzle_id = @id";
                        cmd.Parameters.AddWithValue("@id", submission.puzzleId);

                        var seed = await cmd.ExecuteScalarAsync();
                        if (seed != null)
                        {
                            CommonUtils.AddNotification(User, _configuration, "You have unlocked a seed word: " + seed);
                        }
                    }
                }
            }

            // Optional: Tell client if they got the bonus
            bool gotFirstSolveBonus = submission.IsCorrect && !anyTeamHasSolvedThisSubpuzzle && !ourTeamAlreadyScoredThisSubpuzzle;

            return Ok(new
            {
                correct = submission.IsCorrect,
                message = message,
                bonus = gotFirstSolveBonus ? 10 : 0,
                totalPointsAwarded = gotFirstSolveBonus ? 20 : (submission.IsCorrect ? 10 : 0)
            });
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
