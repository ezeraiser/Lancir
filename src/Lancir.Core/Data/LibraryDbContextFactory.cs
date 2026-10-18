using Lancir.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lancir.Core.Data;

/// <summary>Used by EF Core design-time tools (dotnet ef migrations) to construct the context.</summary>
public sealed class LibraryDbContextFactory : IDesignTimeDbContextFactory<LibraryDbContext>
{
    public LibraryDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<LibraryDbContext>();
        optionsBuilder.UseSqlite($"Data Source={AppPaths.DatabasePath}");
        return new LibraryDbContext(optionsBuilder.Options);
    }
}
