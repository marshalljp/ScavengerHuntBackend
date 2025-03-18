using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using ScavengerHuntBackend.Utils;

namespace ScavengerHuntBackend.Services
{
    public class PuzzleService
    {
        private readonly ScavengerHuntContext _context;

        public PuzzleService(ScavengerHuntContext context)
        {
            _context = context;
        }

        public async Task<bool> ValidatePuzzleAnswer(int puzzleId, string submittedAnswer)
        {
            var puzzle = await _context.Puzzles.FindAsync(puzzleId);
            if (puzzle == null) return false;

            return HashingHelper.VerifyHash(submittedAnswer, puzzle.AnswerHash);
        }
    }
}