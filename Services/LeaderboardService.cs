using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ScavengerHuntBackend.Models;

namespace ScavengerHuntBackend.Services
{
    public class LeaderboardService
    {
        private readonly ScavengerHuntContext _context;

        public LeaderboardService(ScavengerHuntContext context)
        {
            _context = context;
        }

        public async Task<List<Leaderboard>> GetGroupLeaderboard(int groupId)
        {
            return await _context.Leaderboards
                .Where(l => l.Team.GroupId == groupId)
                .OrderByDescending(l => l.Score)
                .Include(l => l.Team)
                .ToListAsync();
        }

        public async Task UpdateScore(int teamId, int points)
        {
            var leaderboardEntry = await _context.Leaderboards.FirstOrDefaultAsync(l => l.TeamId == teamId);
            if (leaderboardEntry == null)
            {
                leaderboardEntry = new Leaderboard { TeamId = teamId, Score = points };
                _context.Leaderboards.Add(leaderboardEntry);
            }
            else
            {
                leaderboardEntry.Score += points;
            }
            await _context.SaveChangesAsync();
        }
    }
}