using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

using ScavengerHuntBackend.Models;

namespace ScavengerHuntBackend.Controllers
{
    [Route("api/puzzles")]
    [ApiController]
    //[Authorize]
    public class PuzzleController : ControllerBase
    {
        private readonly ScavengerHuntContext _context;

        public PuzzleController(ScavengerHuntContext context)
        {
            _context = context;
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetPuzzle(int id)
        {
            var puzzle = await _context.Puzzles.FindAsync(id);
            if (puzzle == null) return NotFound("Puzzle not found.");
            return Ok(puzzle);
        }

        [HttpPost("submit")]
        public async Task<IActionResult> SubmitPuzzle([FromBody] Submission submission)
        {
            var puzzle = await _context.Puzzles.FindAsync(submission.PuzzleId);
            if (puzzle == null)
                return NotFound("Puzzle not found.");

            submission.SubmissionTime = DateTime.UtcNow;
            submission.IsCorrect = BCrypt.Net.BCrypt.Verify(submission.PuzzleId.ToString(), puzzle.AnswerHash);
            _context.Submissions.Add(submission);
            await _context.SaveChangesAsync();

            return Ok(new { correct = submission.IsCorrect });
        }
    }
}