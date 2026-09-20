namespace Lumen.Infrastructure.Storage;

public class LocalFileStorage : IFileStorage
{
    private const int MaxSlugLength = 50;
    private static readonly Regex ExtensionPattern = new(@"^(\.[A-Za-z0-9]{1,10})?$");
    private static readonly Regex NonAlphanumeric = new(@"[^A-Za-z0-9]+");

    private readonly string _rootPath;

    public LocalFileStorage(IOptions<FileStorageOptions> options)
    {
        _rootPath = options.Value.RootPath;
    }

    public async Task<string> SaveAsync(Guid tenantId, string tenantName, Guid documentId, string documentName, string extension, Stream content, CancellationToken ct = default)
    {
        if (!ExtensionPattern.IsMatch(extension))
            throw new ArgumentException($"Extension must match pattern '^\\.[A-Za-z0-9]{{1,10}}$' or be empty, got: {extension}", nameof(extension));

        // Names make the folder readable for a human; the ids keep it unique. Only [A-Za-z0-9-] survives
        // the slug, so a tenant or document name can never add a path separator.
        var folder = $"{Slugify(tenantName)}_{tenantId}";
        var fileName = $"{Slugify(documentName)}_{documentId}{extension}";
        var relativePath = $"{folder}/{fileName}";
        var absolutePath = Path.Combine(_rootPath, folder, fileName);

        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        await using var fileStream = File.Create(absolutePath);
        await content.CopyToAsync(fileStream, ct);

        return relativePath;
    }

    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default)
    {
        Stream stream = File.OpenRead(ResolveInsideRoot(relativePath));
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        File.Delete(ResolveInsideRoot(relativePath));
        return Task.CompletedTask;
    }

    private string ResolveInsideRoot(string relativePath)
    {
        var rootBuilder = new StringBuilder(Path.GetFullPath(_rootPath));
        if (rootBuilder[^1] != Path.DirectorySeparatorChar)
            rootBuilder.Append(Path.DirectorySeparatorChar);
        var root = rootBuilder.ToString();

        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!full.StartsWith(root, StringComparison.Ordinal))
            throw new ArgumentException($"Path traversal attempt detected: {relativePath}", nameof(relativePath));

        return full;
    }

    private static string Slugify(string name)
    {
        var slug = NonAlphanumeric.Replace(name ?? string.Empty, "-").Trim('-');
        if (slug.Length > MaxSlugLength)
            slug = slug[..MaxSlugLength].TrimEnd('-');

        return slug.Length == 0 ? "unnamed" : slug;
    }
}
