namespace Lumen.IntegrationTests.Utils;

public static class TestDataSeeder
{
    private const string DefaultPassword = "Correct-Horse-Battery-1";

    public static async Task<(Guid TenantId, string Email, string Password)> SeedTenantWithUserAsync(
        string connectionString, string tenantName, Role role = Role.Member)
    {
        var (tenantId, email, password, _) = await SeedAsync(connectionString, tenantName, role, document: null);
        return (tenantId, email, password);
    }

    public static async Task<(Guid TenantId, string Email, string Password, Guid DocumentId)> SeedTenantWithUserAndDocumentAsync(
        string connectionString, string tenantName, string documentTitle, string documentContent, Role role = Role.Member)
    {
        var (tenantId, email, password, documentId) = await SeedAsync(connectionString, tenantName, role, (documentTitle, documentContent));
        return (tenantId, email, password, documentId!.Value);
    }

    private static async Task<(Guid TenantId, string Email, string Password, Guid? DocumentId)> SeedAsync(
        string connectionString, string tenantName, Role role, (string Title, string Content)? document)
    {
        var tenantId = Guid.NewGuid();
        var email = $"{Guid.NewGuid()}@lumen.test";

        await using var context = TestDbContextFactory.Create(connectionString, tenantId);
        context.Tenants.Add(new TenantEntity { Id = tenantId, Name = tenantName });

        var hasher = new PasswordHasher<User>();
        var user = new UserEntity { Id = Guid.NewGuid(), TenantId = tenantId, Email = email, Role = role };
        user.PasswordHash = hasher.HashPassword(new User { Id = user.Id, TenantId = tenantId, Email = email, Role = role }, DefaultPassword);
        context.Users.Add(user);

        Guid? documentId = null;
        if (document is { } doc)
        {
            documentId = Guid.NewGuid();
            context.Documents.Add(new DocumentEntity
            {
                Id = documentId.Value,
                TenantId = tenantId,
                Title = doc.Title,
                Content = doc.Content,
                RawFilePath = $"{tenantId}/{documentId.Value}.htm",
                FilingDate = DateOnly.FromDateTime(DateTime.UtcNow)
            });
        }

        await context.SaveChangesAsync();

        return (tenantId, email, DefaultPassword, documentId);
    }
}
