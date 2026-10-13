# Embeddings in .NET: Microsoft.Extensions.AI over Azure OpenAI

Sources current as of **2026-09-20**. Scope: the client half of Stage 3 — which packages generate embeddings, which model to deploy, what the service limits are, and what the code surface looks like. Storage/query side is in `pgvector-and-dotnet.md`; running without a real Azure resource is in `dev-and-test-without-azure.md`.

## Package/version summary

| Package | Latest | Date | Note |
|---|---|---|---|
| `Microsoft.Extensions.AI` | **10.10.0** (stable) | 2026-09-09 | Abstractions + middleware (caching, telemetry, DI). Targets net8/9/10. [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.AI) |
| `Microsoft.Extensions.AI.Abstractions` | 10.10.0 | 2026-09-09 | Pulled in transitively; only needed directly by library authors. |
| `Microsoft.Extensions.AI.OpenAI` | **10.10.0** (stable) | 2026-09-09 | Adapter over the `OpenAI` package. Depends on **`OpenAI >= 2.13.0`**. [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.AI.OpenAI) |
| `OpenAI` (official .NET SDK) | 2.14.0 | — | Latest in the 2.x line. [NuGet flat index](https://api.nuget.org/v3-flatcontainer/openai/index.json) |
| `Azure.AI.OpenAI` | **2.1.0 is the newest *stable*** (2024-12-06); everything after it is beta (`2.2.0-beta.*` … `2.9.0-beta.1`) | — | Companion library that wraps the same `OpenAI` package. Its stable release predates `OpenAI 2.13` by ~2 years. [NuGet](https://www.nuget.org/packages/Azure.AI.OpenAI), [flat index](https://api.nuget.org/v3-flatcontainer/azure.ai.openai/index.json) |
| `Azure.Identity` | — | — | Only needed for Microsoft Entra ID (managed identity) auth. |

**Consequence:** pairing `Microsoft.Extensions.AI.OpenAI 10.10.0` (wants `OpenAI >= 2.13.0`) with `Azure.AI.OpenAI 2.1.0` (built against `OpenAI 2.1.0`) means running `AzureOpenAIClient` against an `OpenAI` assembly 12 minor versions newer than the one it was compiled against — or taking a `2.x-beta` Azure package. The v1 API below removes the need to choose.

## Azure OpenAI v1 API: no Azure-specific client needed

**Since August 2025 Azure OpenAI exposes a v1 API that the plain OpenAI SDK can talk to directly**, and the initial v1 GA covers "a subset of the inference and authoring API capabilities … All GA features are supported for use in production." [Azure OpenAI v1 API — Microsoft Learn](https://learn.microsoft.com/en-us/azure/foundry/openai/api-version-lifecycle) (`ms.date` 2026-05-13, updated 2026-06-05).

What it changes:
- Endpoint shape: `https://<resource>.openai.azure.com/openai/v1/` (also accepts `https://<resource>.services.ai.azure.com/openai/v1/`).
- **`api-version` is no longer a required parameter.**
- Token-based auth with automatic refresh works from the OpenAI client itself — "OpenAI client support for token based authentication and automatic token refresh without the need to take a dependency on a separate Azure OpenAI client."
- Where OpenAI wants a model name, Azure wants the **deployment name**.

Official C# samples from that page, verbatim:

```csharp
// API key
OpenAIClient client = new(
    new ApiKeyCredential("{your-api-key}"),
    new OpenAIClientOptions()
    {
        Endpoint = new("https://YOUR-RESOURCE-NAME.openai.azure.com/openai/v1/"),
    })
```

```csharp
// Microsoft Entra ID (managed identity) — note the OPENAI001 experimental warning
#pragma warning disable OPENAI001

BearerTokenPolicy tokenPolicy = new(
    new DefaultAzureCredential(),
    "https://ai.azure.com/.default");
OpenAIClient client = new(
    authenticationPolicy: tokenPolicy,
    options: new OpenAIClientOptions()
    {
        Endpoint = new("https://YOUR-RESOURCE-NAME.openai.azure.com/openai/v1/"),
    })
```

Entra ID auth additionally needs the `Cognitive Services OpenAI User` role on the identity (same doc, Prerequisites).

The alternative — `Azure.AI.OpenAI` — still works and is what the Azure SDK readme documents (`AzureOpenAIClient(new Uri(...), new DefaultAzureCredential())`, then `GetEmbeddingClient("my-deployment")`), including `AzureOpenAIClientOptions.Audience` for sovereign clouds. [Azure OpenAI client library for .NET — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/overview/azure/ai.openai-readme?view=azure-dotnet) (page `ms.date` 2024-12-06). Its install instruction on that page is still `dotnet add package Azure.AI.OpenAI --prerelease`.

## The abstraction: `IEmbeddingGenerator<string, Embedding<float>>`

`Microsoft.Extensions.AI.Abstractions` defines `IEmbeddingGenerator<TInput,TEmbedding>`; `Embedding<float>` exposes `ReadOnlyMemory<float> Vector`. [Use the IEmbeddingGenerator interface — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/ai/iembeddinggenerator) (`ms.date` 2025-12-11).

```csharp
foreach (Embedding<float> embedding in
    await generator.GenerateAsync(["What is AI?", "What is .NET?"]))
{
    Console.WriteLine(string.Join(", ", embedding.Vector.ToArray()));
}

ReadOnlyMemory<float> vector = await generator.GenerateVectorAsync("What is AI?");
```

Batch call shape: `Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<TInput> values, EmbeddingGenerationOptions? options = null, CancellationToken ct = default)`. `GeneratedEmbeddings<T>` is an ordered collection — item *i* corresponds to input *i*.

**Bridging from the OpenAI SDK** (`Microsoft.Extensions.AI.OpenAI`, namespace `Microsoft.Extensions.AI`):

```csharp
public static IEmbeddingGenerator<string, Embedding<float>> AsIEmbeddingGenerator(
    this OpenAI.Embeddings.EmbeddingClient embeddingClient,
    int? defaultModelDimensions = default);
```

[OpenAIClientExtensions.AsIEmbeddingGenerator — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.openaiclientextensions.asiembeddinggenerator?view=net-11.0-pp) (updated 2026-08-13). So the chain is `OpenAIClient` → `.GetEmbeddingClient("<deployment>")` → `.AsIEmbeddingGenerator()`, and `defaultModelDimensions` is where a shortened embedding (the `dimensions` parameter) is set once, in wiring.

### Middleware pipeline

`EmbeddingGeneratorBuilder<TInput,TEmbedding>` layers delegating generators — caching and telemetry ship in the box:

```csharp
IEmbeddingGenerator<string, Embedding<float>> generator =
    new EmbeddingGeneratorBuilder<string, Embedding<float>>(inner)
        .UseDistributedCache(new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions())))
        .UseOpenTelemetry(sourceName: sourceName)
        .Build();
```

`DistributedCachingEmbeddingGenerator` keys on input value + `EmbeddingGenerationOptions`, and supports partial batch hits (only the missing inputs go to the service). Custom middleware derives from `DelegatingEmbeddingGenerator<TInput,TEmbedding>`; the docs' own worked example is a `RateLimitingEmbeddingGenerator` using `System.Threading.RateLimiting`. Same Learn page as above.

## Model facts (Azure OpenAI)

[Foundry Models sold by Azure — Embeddings — Microsoft Learn](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure) (`ms.date` 2026-09-04):

| Model | Max request (tokens) | Output dimensions | MTEB avg | MIRACL avg |
|---|---|---|---|---|
| `text-embedding-3-small` | 8,192 | **1,536** | 62.3 | 44.0 |
| `text-embedding-3-large` | 8,192 | **3,072** | 64.6 | 54.9 |
| `text-embedding-ada-002` (v2) | 8,192 | 1,536 | 61.0 | 31.4 |

- **Max array size per embeddings call: 2,048 inputs.** (Same page, note under the table.)
- Third-generation models support a **`dimensions` parameter** to shorten the vector; per OpenAI's MTEB testing, a 3-large shortened below 1,536 still scores slightly better than ada-002 at 1,536.
- Embedding models cannot be "upgraded" in place — switching model means re-embedding the whole corpus.
- Pricing (secondary source, verify at deployment time): `text-embedding-3-small` input **$0.02 per 1M tokens** on Azure, last verified 2026-08-06; Azure matches OpenAI's direct per-token rates for embedding models, and the 50% Batch-API discount is **not** available on Azure. [Helicone Azure cost page](https://www.helicone.ai/llm-cost/provider/azure/model/text-embedding-3-small). Treat as approximate — no primary Microsoft pricing page was read for this number.

### What this means for Lumen's numbers

Stage 2 measured the Apple 10-K FY2025 at **296 chunks, median 201 tokens, max 250** (`.research/stage-2/progress.md`). So one filing ≈ 296 inputs ≈ ~60k tokens ≈ **$0.0012** at $0.02/1M — i.e. re-embedding the whole corpus during tuning is financially free; the real costs are latency and rate limits, not dollars. All 296 chunks fit in a single 2,048-input batch call, and no chunk comes close to the 8,192-token per-input ceiling.

**Dimension choice interacts with pgvector's index limit** (see the other file): `vector` columns can be indexed only up to 2,000 dimensions, so `text-embedding-3-large`'s native 3,072 would be storable but not HNSW-indexable in Stage 4 without `halfvec` or a `dimensions`-shortened vector. `text-embedding-3-small` at 1,536 has no such problem.

## Open / unverified

- Default TPM quota for a new embeddings deployment was not found in a primary source; it varies by region and subscription type and must be read off the deployment page when the resource is created.
- Whether the v1 API's `/openai/v1/embeddings` path is covered by the "subset of capabilities" GA list was not confirmed on a per-endpoint basis — verify with a live call once the resource exists (this is the first thing to test when Azure is switched on).
