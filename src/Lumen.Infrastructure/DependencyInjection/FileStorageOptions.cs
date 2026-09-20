namespace Lumen.Infrastructure.DependencyInjection;

public class FileStorageOptions
{
    public const string SectionName = "DocumentStorage";
    public string RootPath { get; set; } = "App_Data/Documents";
}
