using Lancir.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Lancir.Core.Data;

public sealed class LibraryDbContext : DbContext
{
    public LibraryDbContext(DbContextOptions<LibraryDbContext> options) : base(options)
    {
    }

    public DbSet<LibraryGame> Games => Set<LibraryGame>();

    public DbSet<PlaySession> PlaySessions => Set<PlaySession>();

    public DbSet<WatchedFolder> WatchedFolders => Set<WatchedFolder>();

    public DbSet<IgnoredExecutable> IgnoredExecutables => Set<IgnoredExecutable>();

    public DbSet<IgnoredFolder> IgnoredFolders => Set<IgnoredFolder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LibraryGame>(entity =>
        {
            entity.HasIndex(g => g.FolderPath).IsUnique();
        });

        modelBuilder.Entity<PlaySession>(entity =>
        {
            entity.HasIndex(s => new { s.GameId, s.StartedAtUtc });
        });

        modelBuilder.Entity<WatchedFolder>(entity =>
        {
            entity.HasIndex(f => f.Path).IsUnique();
        });

        modelBuilder.Entity<IgnoredExecutable>(entity =>
        {
            entity.HasIndex(e => e.ExecutablePath).IsUnique();
        });

        modelBuilder.Entity<IgnoredFolder>(entity =>
        {
            entity.HasIndex(f => f.FolderPath).IsUnique();
        });
    }
}
