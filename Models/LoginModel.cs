using System.ComponentModel.DataAnnotations;

namespace ScavengerHuntBackend.Models
{
    public class LoginRequest
    {
        [Required]
        public string Email { get; set; }

        [Required]
        public string PasswordHash { get; set; }
    }
}
