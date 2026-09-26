using Lumen.Domain.Models;
using Npgsql;
using NpgsqlTypes;

namespace Lumen.Infrastructure.Repositories;

public class ChunkSearchRepository : IChunkSearchRepository
{
    // Hand-written because a vector query is always ORDER BY <distance> LIMIT k, which EF Core cannot
    // compose over — so the global TenantId filter can never reach it. The tenant predicate is written
    // here by hand and placed before ORDER BY: filtering after ranking could return fewer than topK rows.
    private const string Sql = """
        SELECT c."Id" AS "ChunkId",
               c."DocumentId",
               d."Title" AS "DocumentTitle",
               c."ChunkIndex",
               c."ChunkType",
               c."TokenCount",
               c."Text",
               (c."Embedding" <=> @query)::double precision AS "Distance"
        FROM "DocumentChunks" c
        JOIN "Documents" d ON d."Id" = c."DocumentId" AND d."TenantId" = @tenantId
        WHERE c."TenantId" = @tenantId
          AND c."EmbeddingModel" = @model
          AND (@documentId IS NULL OR c."DocumentId" = @documentId)
        ORDER BY "Distance"
        LIMIT @topK
        """;

    private readonly AppDbContext _context;
    private readonly ICurrentTenant _currentTenant;

    public ChunkSearchRepository(AppDbContext context, ICurrentTenant currentTenant)
    {
        _context = context;
        _currentTenant = currentTenant;
    }

    public async Task<List<ChunkSearchResult>> SearchAsync(
        float[] queryEmbedding,
        string embeddingModel,
        int topK,
        Guid? documentId,
        CancellationToken ct = default)
    {
        var parameters = new NpgsqlParameter[]
        {
            new("query", new Vector(queryEmbedding)),
            new("tenantId", _currentTenant.TenantId),
            new("model", embeddingModel),
            new("topK", topK),
            new("documentId", NpgsqlDbType.Uuid) { Value = (object?)documentId ?? DBNull.Value }
        };

        var rows = await _context.Database
            .SqlQueryRaw<ChunkSearchRow>(Sql, parameters)
            .ToListAsync(ct);

        return rows.Select(r => new ChunkSearchResult
        {
            ChunkId = r.ChunkId,
            DocumentId = r.DocumentId,
            DocumentTitle = r.DocumentTitle,
            ChunkIndex = r.ChunkIndex,
            ChunkType = (ChunkType)r.ChunkType,
            TokenCount = r.TokenCount,
            Text = r.Text,
            Distance = r.Distance
        }).ToList();
    }
}
