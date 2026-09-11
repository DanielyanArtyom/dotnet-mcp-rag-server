namespace Lumen.UnitTests.Domain;

public class DocumentTests
{
    [Fact]
    public void Document_holds_the_values_it_was_given()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var document = new Document
        {
            Id = id,
            TenantId = tenantId,
            Title = "Test title",
            Content = "Test content"
        };

        Assert.Equal(id, document.Id);
        Assert.Equal(tenantId, document.TenantId);
        Assert.Equal("Test title", document.Title);
        Assert.Equal("Test content", document.Content);
    }
}
