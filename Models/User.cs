using System.ComponentModel.DataAnnotations;

namespace ScavengerHuntBackend.Models
{
    public class User
    {
        public int Id { get; set; }

        public string Email { get; set; }

        public string? PasswordHash { get; set; }

        public int? TeamId { get; set; }

        public string? AccessCode { get; set; }

        public string? Team { get; set; }

        public string? Name { get; set; }

        public string? Username { get; set; }

        public string? Bio { get; set; }

        public int PuzzlesCompleted { get; set; }

        public int GlobalRank { get; set; }

        public int TotalScore { get; set; }

        public bool IsEmailVerified { get; set; } = false;
        public string? EmailVerificationCode { get; set; }
        public DateTime? EmailVerificationExpires { get; set; }
    }

}
