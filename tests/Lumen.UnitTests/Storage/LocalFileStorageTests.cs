namespace Lumen.UnitTests.Storage;

public class LocalFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lumen-tests-" + Guid.NewGuid());

    [Fact]
    public async Task SaveAsync_writes_the_file_under_tenant_and_document_scoped_path()
    {
        var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = _root }));
        var tenantId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("<html>hi</html>"));

        var relativePath = await storage.SaveAsync(tenantId, documentId, ".htm", content);

        Assert.Equal($"{tenantId}/{documentId}.htm", relativePath);
        Assert.True(File.Exists(Path.Combine(_root, tenantId.ToString(), $"{documentId}.htm")));
    }

    [Fact]
    public async Task OpenReadAsync_reads_back_what_was_saved()
    {
        var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = _root }));
        var tenantId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("<html>hi</html>"));
        var relativePath = await storage.SaveAsync(tenantId, documentId, ".htm", content);

        await using var readBack = await storage.OpenReadAsync(relativePath);
        using var reader = new StreamReader(readBack);
        var text = await reader.ReadToEndAsync();

        Assert.Equal("<html>hi</html>", text);
    }

    [Fact]
    public async Task SaveAsync_rejects_extension_with_path_traversal()
    {
        var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = _root }));
        var tenantId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("test"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            storage.SaveAsync(tenantId, documentId, "/../x", content));
    }

    [Fact]
    public async Task SaveAsync_rejects_extension_with_backslash_traversal()
    {
        var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = _root }));
        var tenantId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("test"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            storage.SaveAsync(tenantId, documentId, "..\\x", content));
    }

    [Fact]
    public async Task SaveAsync_accepts_valid_extensions()
    {
        var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = _root }));
        var tenantId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        using (var content = new MemoryStream(Encoding.UTF8.GetBytes("test")))
        {
            var result = await storage.SaveAsync(tenantId, documentId, ".htm", content);
            Assert.Equal($"{tenantId}/{documentId}.htm", result);
        }

        using (var content = new MemoryStream(Encoding.UTF8.GetBytes("test")))
        {
            var result = await storage.SaveAsync(tenantId, Guid.NewGuid(), ".pdf", content);
            Assert.EndsWith(".pdf", result);
        }
    }

    [Fact]
    public async Task OpenReadAsync_rejects_parent_directory_traversal()
    {
        var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = _root }));
        var tenantId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("test"));
        await storage.SaveAsync(tenantId, documentId, ".txt", content);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            storage.OpenReadAsync("../otherTenant/file.txt"));
    }

    [Fact]
    public async Task OpenReadAsync_rejects_rooted_paths()
    {
        var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = _root }));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            storage.OpenReadAsync("/etc/passwd"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
