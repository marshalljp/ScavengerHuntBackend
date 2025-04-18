using System.ComponentModel.DataAnnotations;

namespace ScavengerHuntBackend.Models
{
    public class User
    {
        public int Id { get; set; }

        [Required]
        public string Email { get; set; }

        [Required]
        public string PasswordHash { get; set; }
        public string? AccessCode { get; set; }
        public string? Team { get; set; }


    }
}
