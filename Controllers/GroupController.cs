using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Generic;
using ScavengerHuntBackend.Models;


namespace ScavengerHuntBackend.Controllers
{
    [Route("groups")]
    [ApiController]
    //[Authorize]
    public class GroupController : ControllerBase
    {
        private readonly ScavengerHuntContext _context;

        public GroupController(ScavengerHuntContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetAllGroups()
        {
            var groups = await _context.Groups.ToListAsync();
            return Ok(groups);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetGroupById(int id)
        {
            var group = await _context.Groups.Include(g => g.Teams).FirstOrDefaultAsync(g => g.Id == id);
            if (group == null) return NotFound("Group not found.");

            return Ok(group);
        }


        [HttpPost]
        public async Task<IActionResult> CreateGroup([FromBody] ScavengerHuntBackend.Models.Group group)
        {
            if (await _context.Groups.AnyAsync(g => g.Name == group.Name))
                return BadRequest("Group name already exists.");

            _context.Groups.Add(group);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetGroupById), new { id = group.Id }, group);
        }

    }
}
