using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Api.Persistence;

/// <summary>
/// Gives EF tooling a deterministic way to scaffold the Control Plane schema
/// without booting the full MEM API or depending on developer-runtime settings.
/// </summary>
public sealed class MemDbContextFactory : IDesignTimeDbContextFactory<MemDbContext>
{
    public MemDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MemDbContext>()
            .UseSqlite("Data Source=mem-design-time.db")
            .Options;

        return new MemDbContext(options);
    }
}
