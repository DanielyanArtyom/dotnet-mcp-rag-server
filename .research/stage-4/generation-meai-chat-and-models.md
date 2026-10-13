# Generation in .NET: MEAI `IChatClient` over Azure OpenAI v1, sampling params, and which chat model to deploy

Sources current as of **2026-10-01**. Scope: the generation half of Stage 4's `POST /api/ask` (single-turn, non-streaming). This note covers the client wiring, which `ChatOptions` reach the wire, which cheap and full-size models can be deployed on Azure today and what they cost, token usage, seed, and structured output. Retrieval, hybrid search and eval method are out of scope.

**Questions:**
1. How do we get an `IChatClient` from what Stage 3 already references? Covers packages, `AsIChatClient()`, DI with `AddChatClient`, middleware and a fake client for tests.
2. Which `ChatOptions` sampling params does the OpenAI adapter actually send, and which does it silently drop? Does OpenAI/Azure have a `top_k` at all? Does the adapter use Chat Completions or Responses by default?
3. Which mini-class and full-size models can be deployed, at what price and context window? Which of them reject `temperature`/`top_p`, and when do they retire?
4. How do we get token usage for cost logging, and how close to deterministic can the output get (seed)?
5. Does `GetResponseAsync<T>` structured output (for citations) work with these models on Azure v1?

---

## Recommendation

1. **Packages: nothing new is needed.** `Microsoft.Extensions.AI` 10.10.0 and `Microsoft.Extensions.AI.OpenAI` 10.10.0 (already in `Lumen.Infrastructure`) contain `AsIChatClient()`, `ChatClientBuilder`, `AddChatClient`, `UseLogging`, `UseOpenTelemetry`, `ConfigureOptions` and `GetResponseAsync<T>`. Build the chat client from the same `OpenAIClient` construction Stage 3 uses: `client.GetChatClient("<deployment>").AsIChatClient()`. A 10.10.1 patch exists (2026-09-25). Bump only if there's a reason.
2. **Use Chat Completions (the default `AsIChatClient()` on `ChatClient`), not Responses.** For a single-turn call with no tools, Chat Completions forwards strictly more of `ChatOptions` (Seed, StopSequences, penalties) and isn't marked `[Experimental]`. The Responses adapter silently drops Seed and StopSequences.
3. **Remove "top-k" from the list of *sampling* params.** Neither OpenAI's Chat Completions nor its Responses request schema has `top_k`, and the OpenAI .NET SDK has no such property. MEAI's `ChatOptions.TopK` exists for other providers, and the OpenAI adapter never reads it, so setting it is a silent no-op. The real "top-k" in Lumen is retrieval `topK` (already in `POST /api/search`). The tunable generation params are `temperature`, `top_p`, `max_output_tokens`, and on newer models `reasoning_effort`.
4. **Model choice. The landscape changed: every cheap model that is still open to new customers is a reasoning model.** `gpt-4.1-mini` and `gpt-4o-mini` are now *Deprecated*, which blocks any subscription that has never deployed them. What remains are reasoning models where `temperature` only works when reasoning is switched off (`reasoning_effort: none`).
   - **Default (mini-class): `gpt-6-luna` (2026-09-22) with `reasoning_effort = none`.** It costs **$0.10 in / $0.50 out per 1M** (short-context, Global Standard). That is the cheapest current general model, 7.5× cheaper than `gpt-5.4-mini` on input. OpenAI's own guide explicitly says GPT-6 Luna supports `none` and that `temperature`/`top_p` must only be removed when effort is *not* `none`. This makes it the one mini-class model where a primary source confirms temperature still works. Caveats: it is 9 days old, Azure's docs contradict themselves about it (see Uncertain), and its default effort is `medium`, so `none` must be sent explicitly.
   - **Fallback (mini-class): `gpt-5.4-mini` (2026-03-17).** It is GA until 2027-09-21 and costs $0.75 / $4.50, with `none` as the default effort. Azure's general text still says GPT-5-series reasoning models reject `temperature`, so treat temperature support as unproven until a live call shows otherwise.
   - **Full-size comparison: `gpt-6-sol` (2026-09-22)** at $2.00 / $10.00 short-context. It is the same family as Luna and also supports `none`, so mini vs. full compares *size* with everything else held constant (API shape, prompt, params). The alternative is `gpt-5.4` ($2.50 / $15.00).
   - **First thing to do once the deployments exist:** send one probe call per deployment with `temperature: 0.2` and `reasoning_effort: "none"`, and check for a 400. Experiment E5 (temperature/top-p stability) only makes sense on a model that passes this probe.
5. **Always pin `Reasoning.Effort` in config**, even for "mini" deployments. On a reasoning model, hidden reasoning tokens are billed as output and count against `MaxOutputTokens`, because the adapter sends it as `max_completion_tokens`, which includes reasoning tokens. Without pinning it, a `MaxOutputTokens: 500` answer can come back truncated or empty.
6. **Cost logging:** read `ChatResponse.Usage` (Input, Output, Cached, Reasoning token counts) and `ChatResponse.ModelId`. Log them per request next to the configured price per 1M. Stage 2's numbers give an estimate of about 2k input + ~300 output tokens per question at topK=5, which works out to roughly **$0.00035/question on `gpt-6-luna`** and **~$0.007 on `gpt-6-sol`**. A full 30-question × 3-run eval costs about $0.03 vs. ~$0.63.
7. **Citations: start with inline `[n]` markers parsed server-side, and keep `GetResponseAsync<T>` as an option.** Structured output works on Azure v1, but MEAI sends `strict` only when you opt in via `AdditionalProperties["strict"] = true`. Azure's structured-outputs page doesn't list the 5.4/6.x models yet (the reasoning page's feature table says ✅). Both points need a live check, and a plain-text answer is easier to eyeball during the eval loop.
8. **Fake for tests:** add a hand-written `FakeChatClient : IChatClient`, selected by `Chat:Provider = Fake` exactly like `FakeEmbeddingService`. It returns a deterministic answer built from the prompt (e.g. it echoes the `[n]` source markers it received) and fills in `Usage`. MEAI ships no public fake, and its own test double (`TestChatClient`) is test-internal and callback-based.

---

## Findings

### 1. Packages, client creation, DI, middleware, fake

**1.1 Versions already resolved.** `Lumen.Infrastructure.csproj` references `Microsoft.Extensions.AI` 10.10.0 and `Microsoft.Extensions.AI.OpenAI` 10.10.0. The latter's nuspec depends on `OpenAI` **2.13.0** and `Microsoft.Extensions.AI.Abstractions` 10.10.0 ([nuspec](https://api.nuget.org/v3-flatcontainer/microsoft.extensions.ai.openai/10.10.0/microsoft.extensions.ai.openai.nuspec)). `obj/project.assets.json` confirms `OpenAI/2.13.0` is what's restored. Newer versions exist: `OpenAI` 2.14.0 and `Microsoft.Extensions.AI.OpenAI` 10.10.1, published 2026-09-25 ([NuGet flat index — OpenAI](https://api.nuget.org/v3-flatcontainer/openai/index.json), [MEAI.OpenAI](https://api.nuget.org/v3-flatcontainer/microsoft.extensions.ai.openai/index.json)). Neither is needed for chat.

**1.2 `AsIChatClient` has two overloads** (source at tag `v10.10.0`, [OpenAIClientExtensions.cs](https://github.com/dotnet/extensions/blob/v10.10.0/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIClientExtensions.cs)):

```csharp
public static IChatClient AsIChatClient(this ChatClient chatClient) =>
    new OpenAIChatClient(chatClient);                       // Chat Completions

[Experimental(DiagnosticIds.Experiments.AIOpenAIResponses)]  // = "OPENAI001"
public static IChatClient AsIChatClient(this ResponsesClient responseClient, string? defaultModelId = null) =>
    new OpenAIResponsesChatClient(responseClient, defaultModelId);  // Responses API
```

So **"default" = Chat Completions**. You get Responses only by building a `ResponsesClient` and accepting an `OPENAI001` warning ([DiagnosticIds.cs](https://github.com/dotnet/extensions/blob/v10.10.0/src/Shared/DiagnosticIds/DiagnosticIds.cs) maps `AIOpenAIResponses` and `AIOpenAIReasoning` to `"OPENAI001"`). With Stage 3's existing client it's one line:

```csharp
IChatClient inner = openAiClient.GetChatClient(options.Deployment).AsIChatClient();
```

On Azure v1, the "model" is the **deployment name**. Microsoft's own v1 C# sample builds `new ChatClient(model: "<deployment>", authenticationPolicy: tokenPolicy, options: new OpenAIClientOptions { Endpoint = new Uri("https://YOUR-RESOURCE-NAME.openai.azure.com/openai/v1") })` ([Azure OpenAI v1 API — Learn](https://learn.microsoft.com/en-us/azure/foundry/openai/api-version-lifecycle), `ms.date` 2026-05-13). The same page says that "for Azure OpenAI models we recommend using the Responses API" and that `base_url` accepts both the `openai.azure.com` and `services.ai.azure.com` forms. That recommendation is general advice; Chat Completions is still a supported v1 path (it's what every reasoning-model and structured-output sample on Learn uses).

**1.3 DI.** `AddChatClient` registers `IChatClient` (singleton by default) and returns a `ChatClientBuilder` for chaining middleware. `AddKeyedChatClient` exists for registering two pipelines side by side, e.g. a mini and a full-size model ([ChatClientBuilderServiceCollectionExtensions.cs @ v10.10.0](https://github.com/dotnet/extensions/blob/v10.10.0/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/ChatClientBuilderServiceCollectionExtensions.cs)):

```csharp
public static ChatClientBuilder AddChatClient(this IServiceCollection serviceCollection,
    Func<IServiceProvider, IChatClient> innerClientFactory, ServiceLifetime lifetime = ServiceLifetime.Singleton)
```

The Learn page shows the pattern `builder.Services.AddChatClient(inner).UseDistributedCache();` and a factory form `AddChatClient(services => client.AsBuilder().UseDistributedCache().UseRateLimiting().UseOpenTelemetry().Build(services))` ([Use the IChatClient interface — Learn](https://learn.microsoft.com/en-us/dotnet/ai/ichatclient), `ms.date` 2026-03-13).

**1.4 Middleware that ships in `Microsoft.Extensions.AI` 10.10.0** ([ChatCompletion folder @ v10.10.0](https://github.com/dotnet/extensions/tree/v10.10.0/src/Libraries/Microsoft.Extensions.AI/ChatCompletion)):

- `UseLogging(ILoggerFactory?)`: `LoggingChatClient` logs "invoked/completed/failed" at **Debug**, and full messages, options and response at **Trace**. Its own doc comment says Trace "should never be enabled in a production environment" because it logs content ([LoggingChatClient.cs](https://github.com/dotnet/extensions/blob/v10.10.0/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/LoggingChatClient.cs)). It goes through `ILogger`, so NLog picks it up with no extra wiring.
- `UseOpenTelemetry(...)`: follows the OTel GenAI semantic conventions. `EnableSensitiveData` controls whether content is recorded (Learn page above). It needs an OTel SDK and exporter to see anything; Lumen has none yet, so it can wait.
- `ConfigureOptions(Action<ChatOptions>)`: applies default `ChatOptions` on every call. This is a clean place to apply `Chat:Temperature`, `Chat:ReasoningEffort` and so on from config (Learn page above).
- `UseDistributedCache`, `UseFunctionInvocation`, the chat reducer, and (experimental) routing/failover clients are not needed for `/ask`.
- Custom middleware: derive from `DelegatingChatClient`, or use `builder.Use(async (messages, options, next, ct) => …)` (Learn page above).

**1.5 Fake/test client.** Learn's "Implementation examples" section shows a minimal `SampleChatClient : IChatClient`. It implements `GetResponseAsync`, `GetStreamingResponseAsync`, `GetService` and `Dispose`, and returns `new ChatResponse(new ChatMessage(ChatRole.Assistant, "..."))` (Learn page above). dotnet/extensions' own test double is a callback-driven `TestChatClient` with a `GetResponseAsyncCallback` property, and it lives in the *test* project, not a package ([TestChatClient.cs](https://github.com/dotnet/extensions/blob/v10.10.0/test/Libraries/Microsoft.Extensions.AI.Abstractions.Tests/TestChatClient.cs)). For Lumen: a deterministic `FakeChatClient` in Infrastructure, selected by config like `FakeEmbeddingService`, and in unit tests a callback-style fake or a Moq of `IChatClient`. `ChatResponse.Usage` is settable (`UsageDetails` has public setters, see §4), so the fake can report token counts and cost logging can be tested.

### 2. Which `ChatOptions` reach the wire

**2.1 `ChatOptions` surface in 10.10.0** includes `Temperature`, `MaxOutputTokens`, `TopP`, `TopK`, `FrequencyPenalty`, `PresencePenalty`, `Seed`, `Reasoning` (`ReasoningOptions { Effort, Output }`, *not* marked experimental), `ResponseFormat`, `ModelId`, `StopSequences`, `Instructions`, `Tools`, `RawRepresentationFactory` and `AdditionalProperties` ([ChatOptions.cs @ v10.10.0](https://github.com/dotnet/extensions/blob/v10.10.0/src/Libraries/Microsoft.Extensions.AI.Abstractions/ChatCompletion/ChatOptions.cs)). `ReasoningEffort` values are `None, Low, Medium, High, ExtraHigh`. There's no `Max`; reaching `max` would need `RawRepresentationFactory`.

**2.2 Chat Completions adapter mapping** (`OpenAIChatClient.ToOpenAIOptions`, [OpenAIChatClient.cs @ v10.10.0](https://github.com/dotnet/extensions/blob/v10.10.0/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIChatClient.cs), lines ~601–686):

| `ChatOptions` | Sent as | Notes |
|---|---|---|
| `Temperature` | `temperature` | |
| `TopP` | `top_p` | |
| `MaxOutputTokens` | `max_completion_tokens` | SDK's `MaxOutputTokenCount` is `[CodeGenMember("MaxCompletionTokens")]`; the deprecated `max_tokens` is hidden ([ChatCompletionOptions.cs @ OpenAI_2.13.0](https://github.com/openai/openai-dotnet/blob/OpenAI_2.13.0/OpenAI/src/Custom/Chat/ChatCompletionOptions.cs)). Its doc: "including visible output tokens and, on applicable models, reasoning tokens". Correct for reasoning models, which reject `max_tokens`. |
| `FrequencyPenalty` / `PresencePenalty` | same names | |
| `Seed` | `seed` | Under `#pragma warning disable OPENAI001` in the adapter; the SDK's `Seed` is `[Experimental("OPENAI001")]`. |
| `Reasoning.Effort` | `reasoning_effort` | `None→none … ExtraHigh→"xhigh"`. |
| `StopSequences` | `stop` | |
| `ResponseFormat` | `response_format` | Text / `json_object` / `json_schema`; see §5. |
| `ModelId` | patched into `model` if set | Normally unset; the deployment comes from `GetChatClient(...)`. |
| **`TopK`** | **nothing** | Never read. The OpenAI SDK's `ChatCompletionOptions` has no top-k property at all ([generated ChatCompletionOptions.cs @ OpenAI_2.13.0](https://github.com/openai/openai-dotnet/blob/OpenAI_2.13.0/OpenAI/src/Generated/Models/Chat/ChatCompletionOptions.cs)). |

Every assignment is `result.X ??= options.X`. So a value set through `RawRepresentationFactory` wins, and an unset (`null`) option is simply not sent, which leaves the model's server-side default in effect.

**2.3 Responses adapter mapping** (`OpenAIResponsesChatClient`, [source @ v10.10.0](https://github.com/dotnet/extensions/blob/v10.10.0/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIResponsesChatClient.cs), lines ~975–1100): it maps only `MaxOutputTokens→max_output_tokens`, `Temperature`, `TopP`, `Reasoning`, `ResponseFormat→text.format` and `AllowBackgroundResponses`. **`Seed`, `StopSequences`, `FrequencyPenalty`, `PresencePenalty` and `TopK` are silently dropped.** That's consistent with the API: OpenAI's `CreateResponse` request schema has no `seed`, `stop` or penalties (see 2.4).

**2.4 `top_k` does not exist in the OpenAI API.** I parsed the official spec ([openai/openai-openapi `openapi.yaml`](https://github.com/openai/openai-openapi/blob/master/openapi.yaml), fetched 2026-10-01):
- `CreateChatCompletionRequest` properties: `audio, frequency_penalty, function_call, functions, logit_bias, logprobs, max_completion_tokens, max_tokens, messages, metadata, modalities, model, moderation, n, parallel_tool_calls, prediction, presence_penalty, prompt_cache_*, reasoning_effort, response_format, safety_identifier, seed, service_tier, stop, store, stream, stream_options, temperature, tool_choice, tools, top_logprobs, top_p, user, verbosity, web_search_options`. **No `top_k`.**
- `CreateResponse` properties: `… max_output_tokens, … reasoning, … temperature, text, … top_logprobs, top_p, truncation, user`. **No `top_k`, no `seed`, no `stop`.**
- `top_logprobs` is unrelated: it controls how many alternative tokens' log-probs come back, not sampling.

Azure v1 mirrors the same request shape (its spec is linked from the [v1 API page](https://learn.microsoft.com/en-us/azure/foundry/openai/api-version-lifecycle) → [azure-v1-v1-generated.json](https://github.com/Azure/azure-rest-api-specs/blob/main/specification/ai/data-plane/OpenAI.v1/azure-v1-v1-generated.json)). **This refutes "top-k" as a generation parameter in CLAUDE.md for this stack.** It was a reasonable generic-LLM assumption (Gemini, Anthropic and Ollama do have it), but not for OpenAI/Azure.

**2.5 Spec descriptions worth knowing** (same spec): `temperature` is in 0–2, default 1, and "We generally recommend altering this or `top_p` but not both". `top_p` is in 0–1, default 1, with the same advice. `stop` (Chat Completions) says "Not supported with latest reasoning models `o3` and `o4-mini`", up to 4 sequences. `seed` is **`deprecated: true`**, beta: "best effort to sample deterministically … Determinism is not guaranteed, and you should refer to the `system_fingerprint` response parameter".

### 3. Models deployable on Azure (as of 2026-10-01): capabilities, price, lifecycle

**3.1 Lifecycle rules matter more than they used to.** In the Foundry lifecycle policy ([Learn](https://learn.microsoft.com/en-us/azure/foundry/openai/concepts/model-retirements), `ms.date` 2026-07-24), **Deprecated** means: "Existing customers can continue to create and manage deployments. No longer available to new customers… 'Existing customer' is determined at the subscription level: whether that Azure subscription has ever deployed the specific model version." GA models are set to retire 18 months after launch and become Deprecated at 12 months. Retired models return `410 Gone`. Preview models get force-upgraded with ≥30 days' notice and are "not recommended for production".

**3.2 Retirement schedule** ([Learn](https://learn.microsoft.com/en-us/azure/foundry/openai/concepts/model-retirement-schedule), `ms.date` 2026-09-21), relevant rows:

| Model | Version | Lifecycle | Retires | Replacement |
|---|---|---|---|---|
| gpt-4o-mini | 2024-07-18 | **Deprecated** | 2027-04-14 | — |
| gpt-4.1-mini | 2025-04-14 | **Deprecated** | 2027-04-14 | — |
| gpt-4.1-nano | 2025-04-14 | **Deprecated** | 2026-10-14 | — |
| gpt-4.1 | 2025-04-14 | Deprecated | 2027-04-14 | — |
| o4-mini | 2025-04-16 | Deprecated | 2026-11-19 | gpt-5.6-terra |
| o3-mini | 2025-01-31 | Deprecated | 2026-11-19 | gpt-5.6-terra |
| gpt-5-mini / gpt-5-nano / gpt-5 | 2025-08-07 | GA | 2027-02-09 | — |
| gpt-5.4-mini / gpt-5.4-nano | 2026-03-17 | GA | 2027-09-21 | — |
| gpt-5.4 | 2026-03-05 | GA | 2027-09-02 | — |
| gpt-5.5 | 2026-04-24 | GA | 2027-10-26 | — |
| gpt-5.6-luna / -terra / -sol | 2026-07-09 | GA | 2028-01-11 | — |
| gpt-chat-latest | 2026-08-06 | Preview | 2026-12-02 | — |
| text-embedding-3-small | 1 | GA | 2028-02-09 | — |

**The GPT-6 family (`gpt-6-astra`, `gpt-6-sol`, `gpt-6-luna`, `gpt-6.1-sol`) is not in the schedule yet**, even though the models page lists them as available (3.3).

**3.3 Capabilities and context windows** ([Foundry Models sold by Azure — Learn](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure?pivots=azure-openai), `ms.date` 2026-09-21; [Reasoning models — Learn](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/reasoning), `ms.date` 2026-09-21):

| Model | Reasoning model? | Context (input / output) | Chat Completions | Structured outputs | Effort values incl. `none`? |
|---|---|---|---|---|---|
| gpt-6-luna (2026-09-22) | yes | 1,050,000 (922k / 128k) | ✅ | ✅ | ✅ (OpenAI: default `medium`) |
| gpt-6-sol (2026-09-22) | yes | 1,050,000 (922k / 128k) | ✅ | ✅ | ✅ |
| gpt-6-astra (2026-09-03) | yes | 1,050,000 (922k / 128k) | ✅ | ✅ | Azure table ✅; OpenAI says **no `none`** |
| gpt-5.6-luna/terra/sol | yes | 1,050,000 (922k / 128k) | ✅ (no tools unless `none`) | ✅ | ✅ |
| gpt-5.4-mini / -nano (2026-03-17) | yes | 400,000 (272k / 128k) | ✅ | ✅ | ✅ (OpenAI: default `none` for 5.4-mini) |
| gpt-5-mini / -nano (2025-08-07) | yes | 400,000 (272k / 128k) | ✅ | ✅ | ❌ (`minimal` is the lowest; `none` is listed only for 5.1+) |
| gpt-4.1-mini (2025-04-14) | **no** | 1,047,576 (300k on standard deployments) / 32,768 | ✅ | ✅ (chat completions) | n/a |
| gpt-4o-mini (2024-07-18) | **no** | 128,000 / 16,384 | ✅ | ✅ | n/a |

Footnote 7 on the reasoning page: "`gpt-5.6`, `gpt-5.5`, `gpt-5.4`, `gpt-5.2`, `gpt-5.1` … support `'None'` as a value for the `reasoning_effort` parameter … to generate responses without reasoning". The `reasoning_effort` row: "`minimal` works only with the original GPT-5 reasoning models." The GPT-6 family is available on Global Standard in all listed Americas and Europe regions ([region availability — Learn](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure-region-availability?pivots=standard), `ms.date` 2026-09-03).

**3.4 Which models reject `temperature` / `top_p`.** This is where the primary sources disagree, which is why the probe call is in the recommendation:
- Azure reasoning page, "Unsupported parameters" section: "Reasoning models other than GPT-6 Astra don't support the following parameters: `temperature`, `top_p`, `presence_penalty`, `frequency_penalty`, `logprobs`, `top_logprobs`, `logit_bias`, `max_tokens`". The **same page's GPT-6 feature table** marks `temperature` ✅ and `top_p` ✅ for **all four** GPT-6 models (astra, sol, luna, 6.1-sol). It has no such rows for GPT-5.x.
- Azure chat-completions how-to: "Reasoning models, such as the GPT-5 series … use `max_completion_tokens` instead of `max_tokens`, and they don't support `temperature`, `top_p`, or the penalty parameters." ([Learn](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/chatgpt), `ms.date` 2026-08-24.) This sentence makes no exception for `reasoning_effort: none`.
- OpenAI's "Using GPT-6" guide: "When reasoning effort is not `none`, remove `temperature`, `top_p`, and `top_logprobs`." Also: "GPT-6 Sol and GPT-6 Luna support `none`." and "GPT-6 Astra and GPT-6.1 Sol do not support `none`; use `low` instead." ([developers.openai.com/api/docs/guides/latest-model](https://developers.openai.com/api/docs/guides/latest-model), no page date.) So on GPT-6 Luna/Sol, temperature is allowed **only with effort `none`**. Astra/6.1-Sol can never take it per OpenAI, which contradicts Azure's "other than GPT-6 Astra" wording.
- OpenAI model pages: GPT-6 Luna "reasoning.effort supports none, low, medium (default), high, xhigh, and max", pricing $0.10 / $0.01 cached / $0.125 cache-write / $0.50 per 1M, knowledge cutoff May 18, 2026 ([gpt-6-luna](https://developers.openai.com/api/docs/models/gpt-6-luna)). GPT-5.4 mini: "Reasoning.effort supports: none (default), low, medium, high and xhigh", $0.75 / $0.075 / $4.50, cutoff Aug 31, 2025, structured outputs supported ([gpt-5.4-mini](https://developers.openai.com/api/docs/models/gpt-5.4-mini)). Neither page says anything explicit about temperature.
- **Non-reasoning models** (`gpt-4.1-mini`, `gpt-4o-mini`) accept `temperature`/`top_p` normally, but they're Deprecated (3.2), so a fresh subscription can't deploy them.

**What reasoning models take instead:** `reasoning_effort` (`none/minimal/low/medium/high/xhigh/max`, model-dependent), `verbosity` (`low/medium/high`) on GPT-5/6, and `max_completion_tokens` (Chat Completions) or `max_output_tokens` (Responses), never `max_tokens` (Azure reasoning page). MEAI exposes `Reasoning.Effort`. `verbosity` has no `ChatOptions` property and would need `RawRepresentationFactory`, which I haven't verified the 2.13.0 SDK surfaces.

**3.5 Prices, Azure Global Standard, East US 2, USD per 1M tokens.** These come from the **Azure Retail Prices API** (`https://prices.azure.com/api/retail/prices`, `serviceName eq 'Foundry Models'`, queried 2026-10-01). That is Microsoft's machine-readable source behind [azure.microsoft.com/pricing/details/azure-openai](https://azure.microsoft.com/pricing/details/azure-openai/). Meter names are shown so they can be re-checked. For the 5.5/5.6/6.x families only the "ShortCo" (short-context) standard meters are listed; long-context costs about 2×.

| Model | Input | Cached input | Output | Meter (effective from) |
|---|---|---|---|---|
| **gpt-6-luna** | **0.10** | 0.01 | **0.50** | `6-luna ShortCo Inp/Opt Std Gl` (2026-09-01) |
| gpt-5-nano | 0.05 | 0.005 | 0.40 | `GPT 5 Nano Inpt/outpt Glbl` (2025-08-01) |
| gpt-5.4-nano | 0.20 | 0.02 | 1.25 | `5.4 nano Inp/Opt Gl` (2026-03-01) |
| gpt-5.6-luna | 0.20 | 0.02 | 1.20 | `5.6 luna ShortCo Inp/Opt Std Gl` (2026-08-01) |
| gpt-5-mini | 0.25 | 0.025 | 2.00 | `GPT 5 Mini Inpt/outpt Glbl` (2025-08-01) |
| gpt-5.4-mini | 0.75 | 0.075 | 4.50 | `5.4 mini Inp/Opt Gl` (2026-03-01) |
| gpt-4o-mini *(Deprecated)* | 0.15 | 0.075 | 0.60 | `gpt-4o-mini-0718-Inp/Outp-glbl` (per-1K × 1000) |
| gpt-4.1-mini *(Deprecated)* | 0.40 | 0.10 | 1.60 | `gpt 4.1 mini Inp/Outp glbl` (per-1K × 1000) |
| o4-mini *(Deprecated, retires 2026-11-19)* | 1.10 | 0.275 | 4.40 | `o4-mini 0416 Inp/Outp glbl` |
| **gpt-6-sol** | **2.00** | 0.20 | **10.00** | `6-sol ShortCo Inp/Opt Std Gl` |
| gpt-5.4 | 2.50 | 0.25 | 15.00 | `5.4 inp/opt Gl` |
| gpt-5.6-sol | 4.00 | 0.40 | 20.00 | `5.6 sol ShortCo … Std Gl` |
| gpt-5.5 | 5.00 | 0.50 | 30.00 | `5.5 ShortCo inp/opt Gl` |
| gpt-6-astra | 10.00 | 1.00 | 50.00 | `6-astra ShortCo … Std Gl` |

The Retail API numbers for gpt-6-luna and gpt-5.4-mini match OpenAI's own model pages exactly (3.4), which cross-checks the API. Data Zone meters run about 10% higher (e.g. `6-luna ShortCo Inp Std DZ` = 0.11).

**3.6 Why `gpt-6-luna` over `gpt-5-nano`, which is cheaper on input:** `gpt-5-nano` doesn't support `none` effort (only `minimal`), so it is always a reasoning model and per Azure never accepts `temperature`. That makes E5 impossible on it, and its hidden reasoning tokens are billed at the output rate. `gpt-6-luna` with `none` is the only sub-$1 option where a primary source says temperature is honoured.

### 4. Token usage and determinism

**4.1 Usage mapping.** The Chat Completions adapter copies `ChatTokenUsage` into `ChatResponse.Usage` as `InputTokenCount`, `OutputTokenCount`, `TotalTokenCount`, `CachedInputTokenCount` (from `InputTokenDetails.CachedTokenCount`) and `ReasoningTokenCount` (from `OutputTokenDetails.ReasoningTokenCount`) ([OpenAIChatClient.cs @ v10.10.0](https://github.com/dotnet/extensions/blob/v10.10.0/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIChatClient.cs), `FromOpenAIUsage`). The same response also sets `ModelId` (the model string the service reports), `FinishReason` (use it to detect `Length` truncation), `ResponseId`, `CreatedAt` and `RawRepresentation` (the SDK `ChatCompletion`). `UsageDetails` has public setters and an `Add(UsageDetails)` method ([UsageDetails.cs @ v10.10.0](https://github.com/dotnet/extensions/blob/v10.10.0/src/Libraries/Microsoft.Extensions.AI.Abstractions/UsageDetails.cs)), so a fake can populate it. Cost = input × in-rate + output × out-rate, with reasoning tokens already inside `OutputTokenCount` on OpenAI's usage model. Price per 1M belongs in config, not code, because prices change (3.5).

**4.2 Seed.** It's only forwarded on the Chat Completions path (2.2/2.3), it's `[Experimental("OPENAI001")]` in the SDK, and it's **marked `deprecated: true`** (beta, best-effort) in OpenAI's spec (2.5). `system_fingerprint` is *not* surfaced on `ChatResponse`. The adapter puts it only on the OTel activity (`AddOpenAIResponseAttributes`), so read it via `((OpenAI.Chat.ChatCompletion)response.RawRepresentation).SystemFingerprint` if it's wanted in the eval log. Practical determinism comes from `temperature: 0` (on a model that accepts it) plus a fixed prompt and fixed retrieval, and seed is a minor extra. E5 should measure stability empirically (×3 runs) rather than assume it.

### 5. Structured output (`GetResponseAsync<T>`)

**5.1 What MEAI sends** ([ChatClientStructuredOutputExtensions.cs @ v10.10.0](https://github.com/dotnet/extensions/blob/v10.10.0/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/ChatClientStructuredOutputExtensions.cs)):
- It builds `ChatResponseFormat.ForJsonSchema<T>()`. If `T`'s schema isn't an object (e.g. `List<Citation>`), it wraps it in `{ "data": … }` because "all the real LLM providers today require an object schema as the root".
- When `useJsonSchemaResponseFormat` is null/true (the default), it sets `options.ResponseFormat` to that schema with no prompt augmentation. When it's `false`, it falls back to `json_object` and appends a "Respond with a JSON value conforming to the following schema" user message.
- It returns `ChatResponse<T>`, and `.Result` deserializes the text.

**5.2 What the OpenAI adapter does with it:** `json_schema` response format. The schema is first run through `StrictSchemaTransformCache` (`DisallowAdditionalProperties`, `RequireAllProperties`, `ConvertBooleanSchemas`, unsupported keywords moved into `description`). **However, the `strict` flag is sent only if `ChatOptions.AdditionalProperties["strict"] = true`.** `HasStrict` returns `null` otherwise, and the SDK then omits `strict` ([OpenAIClientExtensions.cs @ v10.10.0](https://github.com/dotnet/extensions/blob/v10.10.0/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIClientExtensions.cs); `ToOpenAIChatResponseFormat` in OpenAIChatClient.cs). Without `strict`, the schema is guidance rather than a guarantee.

**5.3 Azure support** ([Structured outputs — Learn](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/structured-outputs), `ms.date` 2026-08-24):
- Both Chat Completions (`response_format`) and Responses (`text.format`) support it. "The latest GA API, `v1`, also support[s] structured outputs."
- Rules: all fields required (emulate optional with `["type","null"]`); `additionalProperties: false`; ≤100 object properties and ≤5 nesting levels; root can't be `anyOf`.
- Unsupported keywords: string `minLength/maxLength/pattern/format`, number `minimum/maximum/multipleOf`, array `minItems/maxItems/uniqueItems/…`. MEAI's transform already handles required/additionalProperties.
- The "Supported models" list on that page includes `gpt-5-mini`, `gpt-5-nano`, `gpt-4.1-mini`, `gpt-4o-mini` and `o4-mini`, but **not** gpt-5.4-x, 5.5, 5.6 or 6.x. The reasoning page's feature tables mark Structured Outputs ✅ for every GPT-5.x and GPT-6 model, and OpenAI's model pages list `structured_outputs` for both gpt-5.4-mini and gpt-6-luna (3.4). The likely explanation is that the structured-outputs page's list is stale, but that's unverified.
- Not supported with parallel function calls (irrelevant: `/ask` has no tools).

**5.4 Relevance to citations.** A schema like `{ answer: string, citations: [{ sourceNumber: int, quote: string }] }` fits well within the limits. The trade-off is design-level, not a capability gap. Inline `[n]` markers keep the answer readable in the console eval harness, and they degrade gracefully (unparsed markers are still text). `GetResponseAsync<T>` + `strict` gives machine-checkable citations but changes the response contract.

---

## Uncertain / could not verify

1. **Does `temperature` actually work on `gpt-6-luna` / `gpt-6-sol` / `gpt-5.4-mini` with `reasoning_effort: none` on Azure?** OpenAI's GPT-6 guide implies yes for Luna/Sol. Azure's GPT-6 feature table says ✅. Azure's prose says reasoning models (other than Astra) don't support it. Nothing primary covers gpt-5.4-mini + `none` + temperature either way. **Settle it with one live call per deployment**, and record the result in `.research/discussion/`.
2. **GPT-6 lifecycle status.** The models page lists `gpt-6-luna`/`gpt-6-sol` without a "Preview" label, but they're missing from the retirement schedule, so there's no published retirement date. A GA model would get 18 months; check the deployment blade or the Models API (`lifecycleStatus`) when deploying.
3. **Short- vs long-context threshold** for GPT-5.5/5.6/6.x pricing isn't given on the Learn page ("see Azure OpenAI pricing") and isn't in the Retail API meter names. Lumen's ~2k-token prompts are almost certainly "short", but I didn't find the number.
4. **Whether this specific subscription counts as an "existing customer"** for `gpt-4.1-mini`/`gpt-4o-mini` (only if it ever deployed that exact version). The Foundry portal shows it at deploy time. If it does, `gpt-4.1-mini` ($0.40 / $1.60, non-reasoning, accepts temperature) would be a legitimate, if short-lived (retires 2027-04-14), baseline.
5. **Structured-output support for gpt-5.4-x / 6.x on Azure:** there's a doc inconsistency (5.3). Confirm live before relying on `strict`.
6. **`verbosity` via MEAI:** no first-class `ChatOptions` property. I didn't verify whether `ChatCompletionOptions` in OpenAI 2.13.0 exposes it for `RawRepresentationFactory`.
7. **Prices** came from the Retail Prices API (East US 2, Global Standard, retail USD). Other regions and Data Zone differ by about 10%. The human-readable pricing page is JS-rendered and wasn't read directly.
8. **Default TPM quota** for new chat deployments wasn't checked (same gap as Stage 3 embeddings). Some GPT-6 and 5.6 models need a quota request below Tier 5 ("Quota request required depending on quota tier", reasoning page availability table). Luna is listed as "No access request needed".
