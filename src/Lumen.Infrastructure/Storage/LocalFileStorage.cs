namespace Lumen.Infrastructure.Storage;

public class LocalFileStorage : IFileStorage
{
    private readonly string _rootPath;
    private static readonly Regex ExtensionPattern = new(@"^(\.[A-Za-z0-9]{1,10})?$");

    public LocalFileStorage(IOptions<FileStorageOptions> options)
    {
        _rootPath = options.Value.RootPath;
    }

    public async Task<string> SaveAsync(Guid tenantId, Guid documentId, string extension, Stream content, CancellationToken ct = default)
    {
        if (!ExtensionPattern.IsMatch(extension))
            throw new ArgumentException($"Extension must match pattern '^\\.[A-Za-z0-9]{{1,10}}$' or be empty, got: {extension}", nameof(extension));

        var relativePath = $"{tenantId}/{documentId}{extension}";
        var absolutePath = Path.Combine(_rootPath, tenantId.ToString(), $"{documentId}{extension}");

        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        await using var fileStream = File.Create(absolutePath);
        await content.CopyToAsync(fileStream, ct);

        return relativePath;
    }

    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default)
    {
        var rootBuilder = new StringBuilder(Path.GetFullPath(_rootPath));
        if (rootBuilder[^1] != Path.DirectorySeparatorChar)
            rootBuilder.Append(Path.DirectorySeparatorChar);
        var root = rootBuilder.ToString();

        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!full.StartsWith(root, StringComparison.Ordinal))
            throw new ArgumentException($"Path traversal attempt detected: {relativePath}", nameof(relativePath));

        Stream stream = File.OpenRead(full);
        return Task.FromResult(stream);
    }
}
