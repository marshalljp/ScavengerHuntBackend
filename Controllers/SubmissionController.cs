using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using ScavengerHuntBackend.Models;

namespace ScavengerHuntBackend.Controllers
{
    [Route("api/submissions")]
    [ApiController]
    //[Authorize]
    public class SubmissionController : ControllerBase
    {
        private readonly ScavengerHuntContext _context;

        public SubmissionController(ScavengerHuntContext context)
        {
            _context = context;
        }

        [HttpGet("team/{teamId}")]
        public async Task<IActionResult> GetTeamSubmissions(int teamId)
        {
            var submissions = await _context.Submissions
                .Where(s => s.TeamId == teamId)
                .Include(s => s.Puzzle)
                .ToListAsync();

            if (!submissions.Any()) return NotFound("No submissions found for this team.");

            return Ok(submissions);
        }

        [HttpPost("submit")]
        public async Task<IActionResult> SubmitAnswer([FromBody] Submission submission)
        {
            var puzzle = await _context.Puzzles.FindAsync(submission.PuzzleId);
            if (puzzle == null) return NotFound("Puzzle not found.");

            submission.SubmissionTime = DateTime.UtcNow;
            submission.IsCorrect = BCrypt.Net.BCrypt.Verify(submission.PuzzleId.ToString(), puzzle.AnswerHash);

            _context.Submissions.Add(submission);
            await _context.SaveChangesAsync();

            return Ok(new { correct = submission.IsCorrect });
        }
    }
}