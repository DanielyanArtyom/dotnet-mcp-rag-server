# Building and testing Stage 3 before Azure is switched on

Sources current as of **2026-09-20**. Scope: how the embedding pipeline gets written, run and tested while there is no Azure OpenAI resource, and what changes on the day there is one. Model/package facts live in `embeddings-dotnet-and-azure-openai.md`; storage facts in `pgvector-and-dotnet.md`.

## The constraint

The user's instruction for Stage 3 is: prepare everything, they enable Azure afterwards, and only then do live tests. So the build must satisfy three things at once:

1. `dotnet build` + `dotnet test` are green with **no Azure credentials present**, on this machine and in CI — including the flagship tenant-isolation test, which is a required deliverable (`CLAUDE.md`).
2. Switching Azure on is **configuration only** — no code edit, no recompile, no test rewrite.
3. Tests must be **deterministic**. A real embedding service is a network call with per-call cost, rate limits, and no guarantee of bit-identical vectors across model versions — none of that belongs in a unit or integration test's assertions.

Since the whole point of `IEmbeddingGenerator<string, Embedding<float>>` is that it is an interface (see the other file), points 1 and 2 reduce to: which implementation gets registered, decided at startup from config.

## Options for the non-Azure implementation

### A. Deterministic in-process fake (recommended for tests)

A class implementing `IEmbeddingGenerator<string, Embedding<float>>` that derives each vector from a hash of the input text. Microsoft's own docs publish a minimal implementation of the interface for exactly this shape of need — theirs fills the vector with `Random.Shared.NextSingle()`, which is the one detail to change, since a test needs the same text to embed to the same vector every run:

```csharp
public sealed class SampleEmbeddingGenerator(Uri endpoint, string modelId)
    : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly EmbeddingGeneratorMetadata _metadata = new("SampleEmbeddingGenerator", endpoint, modelId);

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
        return [.. from value in values
            select new Embedding<float>(
                Enumerable.Range(0, 384).Select(_ => Random.Shared.NextSingle()).ToArray())];
    }

    public object? GetService(Type serviceType, object? serviceKey) => /* metadata */ null;
    void IDisposable.Dispose() { }
}
```

[Use the IEmbeddingGenerator interface — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/ai/iembeddinggenerator) (`ms.date` 2025-12-11).

Properties: zero dependencies, zero network, microseconds per call, dimension count is whatever the config says (so the `vector(N)` column and the generator can't drift apart), and identical input → identical output, which lets a test assert real ranking behaviour (e.g. "the chunk whose text equals the query ranks first") instead of just "a row came back."

What it cannot do: say anything about retrieval *quality*. A hash-based vector has no semantics — "revenue" and "net sales" are as far apart as random strings. Quality is measured against the real model, by hand, after Azure is on; that is the Stage 4 eval-set job anyway.

### B. Local ONNX model

`SmartComponents.LocalEmbeddings` — now a wrapper over Semantic Kernel's ONNX-based BERT embedding support. Default model **bge-micro-v2**, MIT-licensed, quantized to **22.9 MiB**, **384-dimensional**, downloaded automatically at first build; ~0.5 ms to embed a 50-character string on an i9-11950H, ~6 ms to search 100,000 candidates. [smartcomponents/docs/local-embeddings.md](https://github.com/dotnet-smartcomponents/smartcomponents/blob/main/docs/local-embeddings.md). An independent alternative with the same idea and a direct `IEmbeddingGenerator` implementation is [elbruno.localembeddings](https://github.com/elbruno/elbruno.localembeddings) (targets net8.0/net10.0).

Properties: genuinely semantic vectors with no cloud account, which makes it possible to *see* retrieval work before Azure exists. Costs: a model download in the build, 384 dimensions ≠ 1,536 (so either the column dimension becomes configurable or local runs use a different database), and it is an extra dependency carried purely for dev convenience. There is at least one reported failure mode of the model file not being present in test projects — [smartcomponents issue #37](https://github.com/dotnet-smartcomponents/smartcomponents/issues/37).

### C. Ollama

`OllamaSharp` implements `IEmbeddingGenerator` directly and is the example used throughout Microsoft's own docs (`new OllamaApiClient(new Uri("http://localhost:11434/"), "phi3:mini")`). Requires a separately installed, separately running daemon — fine for the user's hands-on experiments, wrong for a test suite that must be green on a clean checkout.

### Recommendation

**A for tests, unconditionally** (deterministic, dependency-free, dimension-agnostic). **A or B for local dev before Azure** — B only if the user wants to *see* semantic search working before the Azure resource exists; that's a preference question, not a technical blocker, and it can be added later without touching anything but DI.

## Keeping re-runs cheap once Azure is on

`Microsoft.Extensions.AI` ships `DistributedCachingEmbeddingGenerator`, layered with `.UseDistributedCache(...)` on `EmbeddingGeneratorBuilder`; it keys on the input text plus `EmbeddingGenerationOptions` and serves partial batch hits (only uncached inputs hit the service). Same Learn page as above. For Lumen this matters during chunk-parameter tuning — the deferred Stage 2 checklist item is "upload the same filing at 250/60, then 400/100, then 150/40 and compare" — where overlapping chunk text would otherwise be re-embedded repeatedly. At $0.02/1M tokens the money is negligible; the value is latency and staying under TPM quota.

## Testcontainers: the image must carry the extension

The integration suite's `PostgresFixture` currently starts `postgres:17-alpine`, which has no `vector` extension, so the first migration containing `CREATE EXTENSION vector` will fail. The fix is one line — `new PostgreSqlBuilder().WithImage("pgvector/pgvector:pg17")` — and the same swap is needed in `docker-compose.yml`. Details and tag list: `pgvector-and-dotnet.md`. First run pulls a new image (~400 MB class), which is worth mentioning in `CLAUDE.md`'s commands section alongside the existing `postgres:17-alpine` note.

## What can genuinely be proven without Azure

- Upload → chunk → **embed → persist** wires end to end, and every chunk row gets a vector of the configured dimension.
- Batching: N chunks produce N vectors in input order, and a batch larger than the configured cap is split (the service ceiling is 2,048 inputs per call).
- The **raw-SQL vector query keeps tenant isolation** — two tenants, synthetic vectors, assert tenant B's search never returns tenant A's chunks, and (per the earlier research file's "make the test an argument, not a tautology") that the same query *without* the tenant predicate does leak.
- `DELETE /api/documents/{id}` removes vectors along with chunks (the deferred Stage 2 checklist item).
- Failure behaviour when the generator throws (quota, auth, transport): what the upload endpoint returns, and whether a partially-embedded document can be left behind.
- Dimension mismatch: a vector whose length ≠ the column's declared dimensions must fail loudly, not silently truncate.

## What must wait for the real resource

Anything about *quality* or *cost*: whether retrieved chunks are the right ones, whether 250/60 beats 400/100, real latency per filing, real token spend, and whether the v1 API endpoint path works against a live deployment. These are the user's hands-on experiments per `CLAUDE.md`'s working agreement — Claude prepares the switch, the user throws it.
