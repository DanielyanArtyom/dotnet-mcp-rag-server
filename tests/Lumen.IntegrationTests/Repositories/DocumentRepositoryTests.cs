namespace Lumen.IntegrationTests.Repositories;

[Collection("Postgres collection")]
public class DocumentRepositoryTests(PostgresFixture fixture)
{
    private static readonly IMapper Mapper = new MapperConfiguration(
        cfg => cfg.AddProfile<DomainEntityMappingProfile>(), NullLoggerFactory.Instance).CreateMapper();

    [Fact]
    public async Task Add_then_Complete_actually_persists_the_document()
    {
        var tenantId = Guid.NewGuid();

        await using var context = TestDbContextFactory.Create(fixture.ConnectionString, tenantId);
        context.Tenants.Add(new TenantEntity { Id = tenantId, Name = "Repository test tenant" });

        var repository = new DocumentRepository(context, Mapper);
        var unitOfWork = new UnitOfWork(context, repository);

        var document = new Document { Id = Guid.NewGuid(), Title = "Persisted?", Content = "It should be." };
        unitOfWork.Documents.Add(document);
        await unitOfWork.CompleteAsync();

        await using var verifyContext = TestDbContextFactory.Create(fixture.ConnectionString, tenantId);
        var fromDb = await verifyContext.Documents.FirstOrDefaultAsync(d => d.Id == document.Id);

        Assert.NotNull(fromDb);
        Assert.Equal("Persisted?", fromDb!.Title);
        Assert.Equal(tenantId, fromDb.TenantId);
    }
}
