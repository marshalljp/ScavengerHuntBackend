using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

namespace ScavengerHuntBackend.Services
{
    public class TeamService
    {
        private readonly ScavengerHuntContext _context;

        public TeamService(ScavengerHuntContext context)
        {
            _context = context;
        }

        public async Task<bool> AddUserToTeam(int teamId, int userId)
        {
            var team = await _context.Teams.FindAsync(teamId);
            var user = await _context.Users.FindAsync(userId);

            if (team == null || user == null)
                return false;

            //user.TeamId = teamId;
            await _context.SaveChangesAsync();
            return true;
        }
    }
}
