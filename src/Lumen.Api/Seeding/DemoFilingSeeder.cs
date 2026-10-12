using System.Diagnostics;

namespace Lumen.Api.Seeding;

public class DemoFilingSeeder
{
    private readonly AppDbContext _db;
    private readonly CurrentTenant _currentTenant;
    private readonly IMediator _mediator;
    private readonly ILogger<DemoFilingSeeder> _logger;

    public DemoFilingSeeder(AppDbContext db, CurrentTenant currentTenant, IMediator mediator, ILogger<DemoFilingSeeder> logger)
    {
        _db = db;
        _currentTenant = currentTenant;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task SeedAsync(string folder, CancellationToken ct)
    {
        var filings = DemoManifest.Load(folder);
        var seeded = 0;

        var names = filings.Select(f => f.Tenant).Distinct().ToList();
        var tenantIds = await _db.Tenants.Where(t => names.Contains(t.Name)).ToDictionaryAsync(t => t.Name, t => t.Id, ct);
        foreach (var filing in filings)
        {
            if (!tenantIds.ContainsKey(filing.Tenant))
            {
                throw new InvalidOperationException($"Demo filing '{filing.Title}': tenant '{filing.Tenant}' does not exist (enable Database:SeedDemoData or create it first).");
            }
        }

        foreach (var filing in filings)
        {
            await TenantScopedWriter.RunAsync(_currentTenant, tenantIds[filing.Tenant], async () =>
            {
                if (await _db.Documents.AnyAsync(d => d.Title == filing.Title, ct))
                {
                    _logger.LogInformation("Demo filing {Title} already exists for {Tenant}; skipped.", filing.Title, filing.Tenant);
                    return;
                }

                var stopwatch = Stopwatch.StartNew();
                await using var stream = File.OpenRead(Path.Combine(folder, filing.File));
                var result = await _mediator.Send(new UploadDocumentCommand(filing.File, stream, filing.Title, filing.FilingDate), ct);
                seeded++;
                _logger.LogInformation("Seeded demo filing {Title} for {Tenant}: {Chunks} chunks in {Elapsed} ms.",
                    filing.Title, filing.Tenant, result.ChunkCount, stopwatch.ElapsedMilliseconds);
            });
        }

        _logger.LogInformation("Demo filings: {Seeded} of {Total} ingested, the rest already present.", seeded, filings.Count);
    }
}
