namespace Lumen.Api.DependencyInjection;

public class DatabaseOptions
{
    public const string SectionName = "Database";
    public bool MigrateOnStartup { get; set; }
    public bool SeedDemoData { get; set; }
}
