namespace ScavengerHuntBackend.Models
{
    public class Leaderboard
    {
        public int Id { get; set; }
        public int TeamId { get; set; }
        public Team Team { get; set; }
        public int Score { get; set; }
    }
}
