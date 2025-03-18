using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

namespace ScavengerHuntBackend.Controllers
{
    [Route("api/leaderboard")]
    [ApiController]
    //[Authorize]
    public class LeaderboardController : ControllerBase
    {
        private readonly ScavengerHuntContext _context;

        public LeaderboardController(ScavengerHuntContext context)
        {
            _context = context;
        }

        [HttpGet("group/{groupId}")]
        public async Task<IActionResult> GetGroupLeaderboard(int groupId)
        {
            var leaderboard = await _context.Leaderboards
                .Where(l => l.Team.GroupId == groupId)
                .OrderByDescending(l => l.Score)
                .Include(l => l.Team)
                .ToListAsync();

            return Ok(leaderboard);
        }
    }
}