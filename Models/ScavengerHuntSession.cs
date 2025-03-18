namespace ScavengerHuntBackend.Models
{
    public class ScavengerHuntSession
    {
        public int Id { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public List<Puzzle> Puzzles { get; set; }
    }
}