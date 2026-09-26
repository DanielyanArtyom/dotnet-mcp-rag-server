namespace Lumen.IntegrationTests.Persistence;

[Collection("Postgres collection")]
public class VectorExtensionTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Migrations_enable_the_pgvector_extension()
    {
        await using var context = TestDbContextFactory.Create(fixture.ConnectionString);

        var installed = await context.Database
            .SqlQueryRaw<int>("""SELECT COUNT(*)::int AS "Value" FROM pg_extension WHERE extname = 'vector'""")
            .ToListAsync();

        Assert.Equal(1, installed.Single());
    }

    [Fact]
    public async Task A_vector_value_round_trips_through_the_connection()
    {
        await using var context = TestDbContextFactory.Create(fixture.ConnectionString);

        var distances = await context.Database
            .SqlQueryRaw<double>("""SELECT ('[1,0,0]'::vector <=> '[1,0,0]'::vector)::double precision AS "Value" """)
            .ToListAsync();

        Assert.Equal(0d, distances.Single(), precision: 6);
    }
}
