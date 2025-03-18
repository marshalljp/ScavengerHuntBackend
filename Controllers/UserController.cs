using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

namespace ScavengerHuntBackend.Controllers
{
    [Route("api/users")]
    [ApiController]
    //[Authorize]
    public class UserController : ControllerBase
    {
        private readonly ScavengerHuntContext _context;

        public UserController(ScavengerHuntContext context)
        {
            _context = context;
        }

        [HttpGet("profile")]
        public async Task<IActionResult> GetProfile()
        {
            var email = User.Identity.Name;
            var user = await _context.Users.Include(u => u.Team).FirstOrDefaultAsync(u => u.Email == email);

            if (user == null)
                return NotFound("User not found.");

            return Ok(new
            {
                user.Id,
                user.Email,
                Team = user.Team != null ? new { user.Team.Id, user.Team.Name } : null
            });
        }

        [HttpPost("join-team/{teamId}")]
        public async Task<IActionResult> JoinTeam(int teamId)
        {
            var email = User.Identity.Name;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null) return NotFound("User not found.");

            var team = await _context.Teams.FirstOrDefaultAsync(t => t.Id == teamId);
            if (team == null) return NotFound("Team not found.");

            user.TeamId = teamId;
            await _context.SaveChangesAsync();

            return Ok("Successfully joined the team.");
        }
    }
}