namespace ScavengerHuntBackend.Models
{
    public class Puzzle
    {
        public int Id { get; set; }
        public string Question { get; set; }
        public string AnswerHash { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int ScavengerHuntSessionId { get; set; }
        public ScavengerHuntSession ScavengerHuntSession { get; set; }
        public string Hint { get; set; }
        public int Points { get; set; }
    }
}
