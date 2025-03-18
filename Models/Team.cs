namespace ScavengerHuntBackend.Models
{
    public class Team
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int GroupId { get; set; }
        public Group Group { get; set; }
        public List<User> Users { get; set; }
        public List<Submission> Submissions { get; set; }
        public int Score { get; set; } // Added score tracking
    }
}
