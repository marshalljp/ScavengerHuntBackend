using System.Collections.Generic;
using System.Threading.Tasks;

namespace ScavengerHuntBackend.Services
{
    public class ChatbotService
    {
        private readonly Dictionary<string, string> _hints = new()
        {
            { "1", "Think about the first Bitcoin block..." },
            { "2", "Consider how hashes work in cryptography." },
            { "3", "Look around you, the clue might be hidden in plain sight." }
        };

        public Task<string> GetHint(string puzzleId)
        {
            if (_hints.TryGetValue(puzzleId, out var hint))
            {
                return Task.FromResult(hint);
            }
            return Task.FromResult("No hint available for this puzzle.");
        }
    }
}