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

        var relativePath = await storage.SaveAsync(tenantId, "Acme Corp", documentId, "Q3 report", ".htm", content);

        Assert.Equal($"Acme-Corp_{tenantId}/Q3-report_{documentId}.htm", relativePath);
        Assert.True(File.Exists(Path.Combine(_root, $"Acme-Corp_{tenantId}", $"Q3-report_{documentId}.htm")));
    }

    [Fact]
    public async Task OpenReadAsync_reads_back_what_was_saved()
    {
        var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = _root }));
        var tenantId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("<html>hi</html>"));
        var relativePath = await storage.SaveAsync(tenantId, "Acme Corp", documentId, "Q3 report", ".htm", content);

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
            storage.SaveAsync(tenantId, "Acme Corp", documentId, "Q3 report", "/../x", content));
    }

    [Fact]
    public async Task SaveAsync_rejects_extension_with_backslash_traversal()
    {
        var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = _root }));
        var tenantId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("test"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            storage.SaveAsync(tenantId, "Acme Corp", documentId, "Q3 report", "..\\x", content));
    }

    [Fact]
    public async Task SaveAsync_accepts_valid_extensions()
    {
        var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = _root }));
        var tenantId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        using (var content = new MemoryStream(Encoding.UTF8.GetBytes("test")))
        {
            var result = await storage.SaveAsync(tenantId, "Acme Corp", documentId, "Q3 report", ".htm", content);
            Assert.Equal($"Acme-Corp_{tenantId}/Q3-report_{documentId}.htm", result);
        }

        using (var content = new MemoryStream(Encoding.UTF8.GetBytes("test")))
        {
            var result = await storage.SaveAsync(tenantId, "Acme Corp", Guid.NewGuid(), "Q3 report", ".pdf", content);
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
        await storage.SaveAsync(tenantId, "Acme Corp", documentId, "Q3 report", ".txt", content);

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

    [Theory]
    [InlineData("../../etc/passwd", "etc-passwd")]
    [InlineData("Apple 10-K/FY2025: draft", "Apple-10-K-FY2025-draft")]
    [InlineData("Отчёт", "unnamed")]
    [InlineData("   ", "unnamed")]
    public async Task SaveAsync_turns_any_name_into_a_safe_slug(string name, string expectedSlug)
    {
        var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = _root }));
        var tenantId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("test"));

        var relativePath = await storage.SaveAsync(tenantId, name, documentId, name, ".htm", content);

        Assert.Equal($"{expectedSlug}_{tenantId}/{expectedSlug}_{documentId}.htm", relativePath);
        Assert.True(File.Exists(Path.Combine(_root, relativePath)));
    }

    [Fact]
    public async Task SaveAsync_truncates_very_long_names()
    {
        var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = _root }));
        var tenantId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("test"));

        var relativePath = await storage.SaveAsync(tenantId, "Acme", documentId, new string('a', 500), ".htm", content);

        Assert.Equal($"Acme_{tenantId}/{new string('a', 50)}_{documentId}.htm", relativePath);
    }

    [Fact]
    public async Task DeleteAsync_removes_the_file()
    {
        var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = _root }));
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("test"));
        var relativePath = await storage.SaveAsync(Guid.NewGuid(), "Acme", Guid.NewGuid(), "Report", ".htm", content);

        await storage.DeleteAsync(relativePath);

        Assert.False(File.Exists(Path.Combine(_root, relativePath)));
    }

    [Theory]
    [InlineData("../x.txt")]
    [InlineData("/etc/passwd")]
    public async Task DeleteAsync_rejects_paths_outside_the_root(string relativePath)
    {
        var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = _root }));

        await Assert.ThrowsAsync<ArgumentException>(() => storage.DeleteAsync(relativePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
