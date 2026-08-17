using AssistantApi.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AssistantApi.Data;

/// <summary>Design-time factory for <c>dotnet ef migrations</c>. Connection string is never committed.</summary>
public sealed class AssistantDbContextFactory : IDesignTimeDbContextFactory<AssistantDbContext>
{
    public AssistantDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__AssistantDb")
                 ?? "Host=127.0.0.1;Port=5432;Database=assistant;Username=assistant;Password=design-time-only";
        var options = new DbContextOptionsBuilder<AssistantDbContext>()
            .UseNpgsql(cs)
            .Options;
        return new AssistantDbContext(options);
    }
}
