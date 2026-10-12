namespace Lumen.IntegrationTests.Documents;

[Collection("Postgres collection")]
public class DemoFilingSeederTests(PostgresFixture fixture) : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("lumen-demo-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private async Task<string> PrepareAsync(string tenantName)
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "apple-10k-fy2025.htm"), Path.Combine(_folder, "aapl.html"));
        await File.WriteAllTextAsync(Path.Combine(_folder, "manifest.json"),
            $$"""{ "filings": [ { "tenant": "{{tenantName}}", "title": "Seeded Apple 10-K", "filingDate": "2025-09-27", "file": "aapl.html" } ] }""");
        return _folder;
    }

    private WebApplicationFactory<Program> FactoryWith(string folder) =>
        TestWebApplicationFactory.Create(fixture.ConnectionString).WithWebHostBuilder(b => b.UseSetting("Demo:FilingsPath", folder));

    [Fact]
    public async Task Startup_ingests_the_manifest_into_its_tenant_once()
    {
        var tenantName = $"Demo {Guid.NewGuid():N}";
        var (_, email, password) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, tenantName, Role.Admin);
        var (_, otherEmail, otherPassword) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, $"Other {Guid.NewGuid():N}", Role.Admin);
        var folder = await PrepareAsync(tenantName);

        using (var first = FactoryWith(folder))
        {
            using var client = first.CreateClient();
            await TestAuth.AuthenticateAsync(client, email, password);
            var documents = await client.GetFromJsonAsync<List<DocumentDto>>("/api/documents");
            var seeded = Assert.Single(documents!, d => d.Title == "Seeded Apple 10-K");
            Assert.False(string.IsNullOrEmpty(seeded.IngestProfile));

            using var other = first.CreateClient();
            await TestAuth.AuthenticateAsync(other, otherEmail, otherPassword);
            Assert.DoesNotContain(await other.GetFromJsonAsync<List<DocumentDto>>("/api/documents") ?? [], d => d.Title == "Seeded Apple 10-K");
        }

        using var second = FactoryWith(folder);
        using var again = second.CreateClient();
        await TestAuth.AuthenticateAsync(again, email, password);
        Assert.Single(await again.GetFromJsonAsync<List<DocumentDto>>("/api/documents") ?? [], d => d.Title == "Seeded Apple 10-K");
    }

    [Fact]
    public async Task An_unknown_tenant_stops_startup_and_names_it()
    {
        var folder = await PrepareAsync("No Such Tenant Corp");

        using var factory = FactoryWith(folder);
        var error = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(error);
        Assert.Contains("No Such Tenant Corp", error!.ToString());
    }

    [Fact]
    public async Task An_unknown_tenant_later_in_the_manifest_ingests_nothing()
    {
        var tenantName = $"Demo {Guid.NewGuid():N}";
        var (_, email, password) = await TestDataSeeder.SeedTenantWithUserAsync(fixture.ConnectionString, tenantName, Role.Admin);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "apple-10k-fy2025.htm"), Path.Combine(_folder, "aapl.html"));
        await File.WriteAllTextAsync(Path.Combine(_folder, "manifest.json"),
            $$"""{ "filings": [ { "tenant": "{{tenantName}}", "title": "Half Way Filing", "filingDate": "2025-09-27", "file": "aapl.html" }, { "tenant": "Misspelled Corp", "title": "Other Filing", "filingDate": "2025-09-27", "file": "aapl.html" } ] }""");

        using (var factory = FactoryWith(_folder))
        {
            var error = Record.Exception(() => factory.CreateClient());
            Assert.NotNull(error);
            Assert.Contains("Misspelled Corp", error!.ToString());
        }

        using var plain = TestWebApplicationFactory.Create(fixture.ConnectionString);
        using var client = plain.CreateClient();
        await TestAuth.AuthenticateAsync(client, email, password);
        Assert.DoesNotContain(await client.GetFromJsonAsync<List<DocumentDto>>("/api/documents") ?? [], d => d.Title == "Half Way Filing");
    }
}
