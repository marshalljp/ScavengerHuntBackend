using Microsoft.EntityFrameworkCore;
using ScavengerHuntBackend.Models;

namespace ScavengerHuntBackend
{
    public class ScavengerHuntContext : DbContext
    {
        public ScavengerHuntContext(DbContextOptions<ScavengerHuntContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<Group> Groups { get; set; }
        public DbSet<Team> Teams { get; set; }
        public DbSet<Puzzle> Puzzles { get; set; }
        public DbSet<Submission> Submissions { get; set; }
        public DbSet<Leaderboard> Leaderboards { get; set; }
        public DbSet<ScavengerHuntSession> ScavengerHuntSessions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Group>()
                .HasMany(g => g.Teams)
                .WithOne(t => t.Group)
                .HasForeignKey(t => t.GroupId);

            modelBuilder.Entity<Team>()
                .HasMany(t => t.Users)
                .WithOne(u => u.Team)
                .HasForeignKey(u => u.TeamId);

            modelBuilder.Entity<ScavengerHuntSession>()
                .HasMany(s => s.Puzzles)
                .WithOne(p => p.ScavengerHuntSession)
                .HasForeignKey(p => p.ScavengerHuntSessionId);

            modelBuilder.Entity<Submission>()
                .HasOne(s => s.Team)
                .WithMany(t => t.Submissions)
                .HasForeignKey(s => s.TeamId);

            modelBuilder.Entity<Submission>()
                .HasOne(s => s.Puzzle)
                .WithMany()
                .HasForeignKey(s => s.PuzzleId);
        }
    }
}
