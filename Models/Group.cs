namespace ScavengerHuntBackend.Models
{
    public class Group
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public List<Team> Teams { get; set; } = new List<Team>();
    }
}