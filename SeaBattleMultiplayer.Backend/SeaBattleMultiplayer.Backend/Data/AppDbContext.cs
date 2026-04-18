using Microsoft.EntityFrameworkCore;
using SeaBattleMultiplayer.Backend.Models;

namespace SeaBattleMultiplayer.Backend.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<GameRecord> GameRecords => Set<GameRecord>();
    public DbSet<GameParticipant> GameParticipants => Set<GameParticipant>();
    public DbSet<GameMove> GameMoves => Set<GameMove>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.HasIndex(u => u.Username).IsUnique();
            entity.Property(u => u.Username).IsRequired().HasMaxLength(50);
            entity.Property(u => u.PasswordHash).IsRequired();
        });

        modelBuilder.Entity<Friendship>(entity =>
        {
            entity.HasKey(f => f.Id);

            entity.HasOne(f => f.Requester)
                  .WithMany()
                  .HasForeignKey(f => f.RequesterId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(f => f.Addressee)
                  .WithMany()
                  .HasForeignKey(f => f.AddresseeId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(f => new { f.RequesterId, f.AddresseeId }).IsUnique();
        });

        modelBuilder.Entity<GameRecord>(entity =>
        {
            entity.HasKey(g => g.Id);
            entity.HasIndex(g => g.RoomId);
            entity.Property(g => g.RoomId).IsRequired().HasMaxLength(64);
            entity.Property(g => g.StateJson).IsRequired();
        });

        modelBuilder.Entity<GameParticipant>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.HasOne(p => p.GameRecord)
                  .WithMany(g => g.Participants)
                  .HasForeignKey(p => p.GameRecordId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(p => p.User)
                  .WithMany()
                  .HasForeignKey(p => p.UserId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.Property(p => p.FleetJson).IsRequired();
        });

        modelBuilder.Entity<GameMove>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.HasOne(m => m.GameRecord)
                  .WithMany(g => g.Moves)
                  .HasForeignKey(m => m.GameRecordId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.Property(m => m.Result).IsRequired().HasMaxLength(8);
        });
    }
}
