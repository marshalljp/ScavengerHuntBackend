using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ScavengerHuntBackend.Models;

namespace ScavengerHuntBackend.Controllers
{
    [Route("api/teams")]
    [ApiController]
    //[Authorize]
    public class TeamController : ControllerBase
    {
        private readonly ScavengerHuntContext _context;

        public TeamController(ScavengerHuntContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetAllTeams()
        {
            var teams = await _context.Teams.Include(t => t.Group).ToListAsync();
            return Ok(teams);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetTeamById(int id)
        {
            var team = await _context.Teams.Include(t => t.Group).FirstOrDefaultAsync(t => t.Id == id);
            if (team == null) return NotFound("Team not found.");

            return Ok(team);
        }

        [HttpPost]
        public async Task<IActionResult> CreateTeam([FromBody] Team team)
        {
            if (await _context.Teams.AnyAsync(t => t.Name == team.Name))
                return BadRequest("Team name already exists.");

            _context.Teams.Add(team);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetTeamById), new { id = team.Id }, team);
        }

        [HttpPost("{teamId}/add-user/{userId}")]
        public async Task<IActionResult> AddUserToTeam(int teamId, int userId)
        {
            var team = await _context.Teams.FindAsync(teamId);
            var user = await _context.Users.FindAsync(userId);

            if (team == null || user == null)
                return NotFound("Team or user not found.");

            user.TeamId = teamId;
            await _context.SaveChangesAsync();

            return Ok("User added to team.");
        }
    }
}