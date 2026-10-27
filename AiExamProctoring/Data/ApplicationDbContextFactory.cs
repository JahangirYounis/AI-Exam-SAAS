using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
namespace AiExamProctoring.Data;

// EF tooling must not start AI services, seed users, or migrate a live database
// merely to inspect the model or generate a migration.
public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var root = Directory.GetCurrentDirectory();
        if (!File.Exists(Path.Combine(root, "appsettings.json")))
            root = Path.Combine(root, "Website", "AiExamProctoring");
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        var configuration = new ConfigurationBuilder().SetBasePath(root)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables().Build();
        var connection = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Configure ConnectionStrings__DefaultConnection before running EF tools.");
        return new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options);
    }
}
