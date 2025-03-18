namespace ScavengerHuntBackend.Models
{
    public class Submission
    {
        public int Id { get; set; }
        public int TeamId { get; set; }
        public Team Team { get; set; }
        public int PuzzleId { get; set; }
        public Puzzle Puzzle { get; set; }
        public DateTime SubmissionTime { get; set; }
        public bool IsCorrect { get; set; }
    }
}
