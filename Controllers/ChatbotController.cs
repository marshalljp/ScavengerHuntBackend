using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ScavengerHuntBackend.Controllers
{
    [Route("api/chatbot")]
    [ApiController]
    //[Authorize]
    public class ChatbotController : ControllerBase
    {
        [HttpGet("hint")]
        public async Task<IActionResult> GetHint([FromQuery] string puzzleId)
        {
            var hints = new Dictionary<string, string>
            {
                { "1", "Think about the first Bitcoin block..." },
                { "2", "Consider how hashes work in cryptography." },
                { "3", "Look around you, the clue might be hidden in plain sight." }
            };

            if (!hints.ContainsKey(puzzleId))
                return NotFound("No hint available for this puzzle.");

            return Ok(new { hint = hints[puzzleId] });
        }
    }
}