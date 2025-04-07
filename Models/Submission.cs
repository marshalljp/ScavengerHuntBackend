namespace ScavengerHuntBackend.Models
{
    public class Submission
    {
        public int Id { get; set; }
        public int puzzleId { get; set; }
        public int subpuzzleId { get; set; }
        public string answer { get; set; }
        public bool IsCorrect { get; set; }
    }
}
