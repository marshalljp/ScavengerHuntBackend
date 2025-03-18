using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using ScavengerHuntBackend.Models;

namespace ScavengerHuntBackend
{
    public static class DbInitializer
    {
        public static async Task Initialize(ScavengerHuntContext context)
        {
            await context.Database.MigrateAsync();

            if (context.Users.Any()) return; // DB already seeded

            var adminUser = new User
            {
                Email = "admin@scavengerhunt.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin123")
            };

            context.Users.Add(adminUser);
            await context.SaveChangesAsync();
        }
    }
}
