using Npgsql;

namespace Lumen.Infrastructure.Repositories;

public class ChunkSearchRepository : IChunkSearchRepository
{
    // Hand-written because a vector query is always ORDER BY <distance> LIMIT k, which EF Core cannot
    // compose over — so the global TenantId filter can never reach it. The tenant predicate is written
    // here by hand and placed before ORDER BY: filtering after ranking could return fewer than topK rows.
    // The per-document cap is soft: over-cap chunks sort last instead of being dropped, so a tenant with
    // a single document still gets topK results.
    private const string Sql = """
        WITH ranked AS (
            SELECT c."Id", c."DocumentId", (c."Embedding" <=> @query)::double precision AS dist
            FROM "DocumentChunks" c
            WHERE c."TenantId" = @tenantId
              AND c."EmbeddingModel" = @model
              AND (@documentId IS NULL OR c."DocumentId" = @documentId)
            ORDER BY dist, c."Id"
            LIMIT @candidates
        ),
        capped AS (
            SELECT r.*, row_number() OVER (PARTITION BY r."DocumentId" ORDER BY r.dist, r."Id") AS doc_rank
            FROM ranked r
        )
        SELECT c."Id" AS "ChunkId",
               c."DocumentId",
               d."Title" AS "DocumentTitle",
               d."FilingDate" AS "DocumentFilingDate",
               c."ChunkIndex",
               c."ChunkType",
               c."TokenCount",
               c."Text",
               r.dist AS "Distance",
               NULL::double precision AS "Score"
        FROM capped r
        JOIN "DocumentChunks" c ON c."Id" = r."Id" AND c."TenantId" = @tenantId
        JOIN "Documents" d ON d."Id" = c."DocumentId" AND d."TenantId" = @tenantId
        ORDER BY (@maxPerDocument IS NOT NULL AND r.doc_rank > @maxPerDocument), r.dist, c."Id"
        LIMIT @topK
        """;

    // Both branches carry their own tenant predicate: the lexical CTE is a second hand-written path the
    // global query filter cannot reach, and identical text in two tenants is exactly where it would leak.
    // The tsquery is built as an OR of the question's own lexemes: plainto/websearch_to_tsquery AND every
    // word and match nothing on real questions.
    private const string HybridSql = """
        WITH q AS (
            SELECT array_to_string(array(
                       SELECT quote_literal(l) FROM unnest(tsvector_to_array(to_tsvector('english', @queryText))) l),
                   ' | ') AS terms
        ),
        lexical AS (
            SELECT c."Id",
                   row_number() OVER (ORDER BY ts_rank_cd(c."SearchVector", q.terms::tsquery) DESC, c."Id") AS rnk
            FROM "DocumentChunks" c, q
            WHERE q.terms <> ''
              AND c."TenantId" = @tenantId
              AND c."EmbeddingModel" = @model
              AND (@documentId IS NULL OR c."DocumentId" = @documentId)
              AND c."SearchVector" @@ q.terms::tsquery
            ORDER BY rnk
            LIMIT @candidates
        ),
        semantic AS (
            SELECT c."Id",
                   row_number() OVER (ORDER BY c."Embedding" <=> @query, c."Id") AS rnk
            FROM "DocumentChunks" c
            WHERE c."TenantId" = @tenantId
              AND c."EmbeddingModel" = @model
              AND (@documentId IS NULL OR c."DocumentId" = @documentId)
            ORDER BY rnk
            LIMIT @candidates
        ),
        fused AS (
            SELECT COALESCE(l."Id", s."Id") AS "Id",
                   COALESCE(@lexicalWeight / (@rrfK + l.rnk), 0) + COALESCE(@vectorWeight / (@rrfK + s.rnk), 0) AS "Score"
            FROM lexical l
            FULL OUTER JOIN semantic s ON s."Id" = l."Id"
        ),
        capped AS (
            SELECT f."Id", f."Score",
                   row_number() OVER (PARTITION BY c."DocumentId" ORDER BY f."Score" DESC, f."Id") AS doc_rank
            FROM fused f
            JOIN "DocumentChunks" c ON c."Id" = f."Id" AND c."TenantId" = @tenantId
        )
        SELECT c."Id" AS "ChunkId",
               c."DocumentId",
               d."Title" AS "DocumentTitle",
               d."FilingDate" AS "DocumentFilingDate",
               c."ChunkIndex",
               c."ChunkType",
               c."TokenCount",
               c."Text",
               (c."Embedding" <=> @query)::double precision AS "Distance",
               f."Score"::double precision AS "Score"
        FROM capped f
        JOIN "DocumentChunks" c ON c."Id" = f."Id" AND c."TenantId" = @tenantId
        JOIN "Documents" d ON d."Id" = c."DocumentId" AND d."TenantId" = @tenantId
        ORDER BY (@maxPerDocument IS NOT NULL AND f.doc_rank > @maxPerDocument), f."Score" DESC, c."Id"
        LIMIT @topK
        """;

    private readonly AppDbContext _context;
    private readonly ICurrentTenant _currentTenant;
    private readonly SearchOptions _options;

    public ChunkSearchRepository(AppDbContext context, ICurrentTenant currentTenant, IOptions<SearchOptions> options)
    {
        _context = context;
        _currentTenant = currentTenant;
        _options = options.Value;
    }

    public async Task<List<ChunkSearchResult>> SearchAsync(
        float[] queryEmbedding,
        string queryText,
        string embeddingModel,
        int topK,
        Guid? documentId,
        CancellationToken ct = default)
    {
        var hybrid = _options.Mode == SearchMode.Hybrid;
        var parameters = new List<NpgsqlParameter>
        {
            new("query", new Vector(queryEmbedding)),
            new("tenantId", _currentTenant.TenantId),
            new("model", embeddingModel),
            new("topK", topK),
            new("documentId", NpgsqlDbType.Uuid) { Value = (object?)documentId ?? DBNull.Value },
            new("candidates", Math.Max(_options.Candidates, topK)),
            new("maxPerDocument", NpgsqlDbType.Integer) { Value = (object?)_options.MaxChunksPerDocument ?? DBNull.Value }
        };

        if (hybrid)
        {
            parameters.Add(new("queryText", queryText));
            parameters.Add(new("rrfK", (double)_options.RrfK));
            parameters.Add(new("lexicalWeight", _options.LexicalWeight));
            parameters.Add(new("vectorWeight", _options.VectorWeight));
        }

        var rows = await _context.Database
            .SqlQueryRaw<ChunkSearchRow>(hybrid ? HybridSql : Sql, parameters.ToArray<object>())
            .ToListAsync(ct);

        return rows.Select(r => new ChunkSearchResult
        {
            ChunkId = r.ChunkId,
            DocumentId = r.DocumentId,
            DocumentTitle = r.DocumentTitle,
            DocumentFilingDate = r.DocumentFilingDate,
            ChunkIndex = r.ChunkIndex,
            ChunkType = (ChunkType)r.ChunkType,
            TokenCount = r.TokenCount,
            Text = r.Text,
            Distance = r.Distance,
            Score = r.Score
        }).ToList();
    }
}
