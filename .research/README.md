# .research

Рабочие исследования и черновики для проекта Lumen. Не часть продукта — в `.gitignore`. Формируется по ходу этапов; каждый файл — отдельная тема, чтобы не листать один гигантский документ.

- [vector-db-sql-server-vs-postgres.md](vector-db-sql-server-vs-postgres.md) — сравнение SQL Server 2025 native VECTOR vs PostgreSQL+pgvector, почему выбран Postgres.
- [ef-core-raw-sql-tenant-isolation.md](ef-core-raw-sql-tenant-isolation.md) — как EF Core global query filters ведут себя с `FromSqlRaw` vs `SqlQuery<T>`/`ExecuteSqlRaw`/Dapper — база под интеграционный тест изоляции тенантов.
- [mcp-csharp-sdk.md](mcp-csharp-sdk.md) — официальный MCP C# SDK: пакеты, версии, `AddMcpServer()`/`MapMcp()`, атрибуты тулов.
- [mcp-client-auth.md](mcp-client-auth.md) — авторизация remote MCP-серверов в Claude Desktop vs Claude Code, почему демо-клиент — Code + Inspector, а не Desktop.
- [roadmap.md](roadmap.md) — **что осталось сделать**: шаги текущего этапа, этапы 6–7 и все отложенные «вернуться позже» с прошлых этапов с триггерами.

Файлы выше — кросс-этапные (решались до/поперёк конкретных этапов). Начиная с этапа 1, материалы по конкретному этапу лежат в своей подпапке:

- [stage-1/design.md](stage-1/design.md) — дизайн-спека этапа 1 (skeleton: Onion Architecture, multi-tenancy, auth, repository+UnitOfWork, error handling); содержит amendment-секцию наверху с отличиями от того, что реально построено.
- [stage-1/plan.md](stage-1/plan.md) — пошаговый implementation-план этапа 1 (13 задач), выполненный и прошедший ревью; тоже с amendment-секцией наверху.
- [stage-1/progress.md](stage-1/progress.md) — итоговый нарратив: что сделано и как, включая пост-имплементационный проход по стилю/архитектуре и реальные инциденты по пути. Постоянная запись — в отличие от рабочего SDD-леджера в `.superpowers/sdd/`, который эфемерен.

- [stage-2/sec-edgar-filing-formats.md](stage-2/sec-edgar-filing-formats.md) — что EDGAR реально отдаёт по 10-K/10-Q: форматы (inline XBRL HTML, plain text, PDF только как courtesy-копия), структура accession-папки, официальные API (`data.sec.gov` submissions/XBRL/full-text search), насколько "грязный" HTML и почему, лицензирование/rate-limit.
- [stage-2/chunking-strategies.md](stage-2/chunking-strategies.md) — ландшафт стратегий чанкинга для RAG: fixed-size+overlap, sentence/paragraph-aware ("recursive"), semantic (embedding-based), table-aware; конкретные цифры дефолтов из LangChain/LlamaIndex/Azure AI Search; что писать в метаданные чанка.
- [stage-2/dotnet-parsing-and-chunking-libraries.md](stage-2/dotnet-parsing-and-chunking-libraries.md) — конкретные NuGet-пакеты для .NET 10: HTML (`HtmlAgilityPack`/`AngleSharp`), PDF (`PdfPig`), XBRL (нет доминирующей опции), чанкинг (`Microsoft.SemanticKernel.Text.TextChunker`, `Microsoft.Extensions.DataIngestion` preview), токенизация (`Microsoft.ML.Tokenizers`), sentence-boundary (`PragmaticSegmenterNet`).
- [stage-2/chunking-testing-and-debugging.md](stage-2/chunking-testing-and-debugging.md) — как проверять качество чанкинга до появления эмбеддингов: дамп чанков в JSONL/SQLite + эвристики (распределение токенов, чистота границ), визуальные инструменты (ChunkViz, text-split-explorer), а также как это стыкуется с eval-циклом Stage 4 (RAGAS, Azure AI Foundry evaluators).
- [stage-2/design.md](stage-2/design.md) — дизайн-спека этапа 2 (upload-based ingestion, `IDocumentReader` абстракция для HTML/PDF, чанкер поверх `DocumentElement`, хранение raw-файлов на диске, новые API-эндпоинты).
- [stage-2/plan.md](stage-2/plan.md) — пошаговый implementation-план этапа 2 (14 задач), сверен с реальным текущим кодом перед стартом; сверху — секция "Pre-implementation corrections".
- [stage-2/progress.md](stage-2/progress.md) — итог этапа 2: что построено, цифры на реальном Apple 10-K, дефекты, пойманные ревью, открытые вопросы. [stage-2/sdd-ledger.md](stage-2/sdd-ledger.md) — полный SDD-леджер (все findings, rulings).

- [stage-3/embeddings-dotnet-and-azure-openai.md](stage-3/embeddings-dotnet-and-azure-openai.md) — клиентская половина этапа 3: пакеты `Microsoft.Extensions.AI` / `.AI.OpenAI` и их версии, почему Azure OpenAI v1 API снимает зависимость от `Azure.AI.OpenAI`, `IEmbeddingGenerator` и его middleware, характеристики моделей (1536/3072 измерений, 8192 токенов, батч 2048), цена и что это значит в цифрах Stage 2.
- [stage-3/pgvector-and-dotnet.md](stage-3/pgvector-and-dotnet.md) — серверная половина: лимиты pgvector (хранение до 16000 измерений, индекс только до 2000), операторы расстояния, exact vs HNSW и `hnsw.ef_search`, ловушка «фильтр применяется после сканирования индекса» и раздел Multitenancy самого pgvector; пакеты `Pgvector*`, отсутствие нативной поддержки в Npgsql EF Core 10, проводка миграции и Docker-образ с расширением.
- [stage-3/dev-and-test-without-azure.md](stage-3/dev-and-test-without-azure.md) — как строить и тестировать этап 3 до включения Azure: детерминированный fake-генератор vs локальный ONNX vs Ollama, кэширование эмбеддингов, смена образа Postgres в Testcontainers, что реально доказуемо без провайдера и что придётся ждать.
- [stage-3/design.md](stage-3/design.md) — дизайн-спека этапа 3 (провайдер по конфигу Fake/AzureOpenAI, `vector(1536)` NOT NULL + имя модели в `DocumentChunks`, эмбеддинг внутри загрузки по принципу «всё-или-ничего», `POST /api/search` на сыром SQL с ручным тенант-предикатом, тесты).
- [stage-3/plan.md](stage-3/plan.md) — пошаговый implementation-план этапа 3 (8 задач), сверен с реальным кодом до старта; сверху — секция «Pre-implementation corrections» и Global Constraints (включая запрет на `git commit`).

- [stage-4/context-assembly-and-grounded-prompting.md](stage-4/context-assembly-and-grounded-prompting.md) — генеративная половина `/api/ask`: формат блоков источников (XML, не JSON), порядок и lost-in-the-middle, склейка перекрывающихся соседних чанков, бюджет токенов (o200k vs cl100k), промпт «только из источников + [n] + фраза-отказ» с серверной проверкой цитат, prompt injection в меру, форматы таблиц и починка заголовков на ингесте, почему не порог по cosine перед LLM, форма ответа (по образцу On Your Data / OpenAI file search).
- [stage-4/rag-eval-methodology.md](stage-4/rag-eval-methodology.md) — методика eval для этапа 4: `Microsoft.Extensions.AI.Evaluation` 10.10.0 (какие эвалюаторы, входы, кэш, `aieval`) и вердикт «библиотека для judge, остальное — своё»; метки-якоря вместо ID чанков (переживают перечанкинг) + lint меток; hit@k/MRR/ранг первого попадания; числа детерминированно, проза — LLM-judge, отказы и ловушка чужого тенанта; статистика при n=25 (McNemar, перестановочный тест, повторы); holdout; формат лога прогонов (run.json + results.jsonl).
- [stage-4/hybrid-search-postgres-fts-rrf.md](stage-4/hybrid-search-postgres-fts-rrf.md) — гибридный поиск на Postgres: встроенный FTS на этом корпусе (stored-колонка `tsvector` + GIN, `english` vs `simple`, как парсер режет `9,220`/`10-K`/`CET1`, ловушка AND в `plainto`/`websearch_to_tsquery` — 0 совпадений на реальном вопросе, OR из лексем, `ts_rank_cd` без IDF), BM25-расширения (`pg_textsearch` vs ParadeDB `pg_search`: лицензии, arm64, образы, глобальная статистика поверх тенантов), RRF (Cormack 2009, k=60, веса) одним SQL с тенант-предикатом в обеих CTE (проверено на живой БД), миграция через shadow-property `HasComputedColumnSql(stored: true)`, ожидаемый прирост по вендорским цифрам.
- [stage-4/generation-meai-chat-and-models.md](stage-4/generation-meai-chat-and-models.md) — генерация для `/api/ask`: `IChatClient` из уже подключённых MEAI 10.10.0 (`AsIChatClient()` = Chat Completions, DI/middleware, fake-клиент), какие `ChatOptions` реально уходят в API (`top_k` у OpenAI/Azure нет вовсе; Seed/Stop теряются на Responses), какие mini/полноразмерные модели можно развернуть сейчас (gpt-4.1-mini/4o-mini — Deprecated, всё дешёвое — reasoning; `temperature` только при `reasoning_effort: none`), цены из Azure Retail Prices API, `Usage` для логирования стоимости, structured output; рекомендация — `gpt-6-luna` (+ `gpt-6-sol` для сравнения) с обязательной пробой temperature вживую.
- [stage-4/design.md](stage-4/design.md) — дизайн-спека этапа 4a: single-turn `/api/ask` с цитатами `[n]` поверх векторного поиска, `GET /api/settings`, eval-сет с якорными метками, консольный харнесс `tools/Lumen.Eval` (режимы retrieval/full, LLM-судья, `compare` с McNemar). Гибрид и починка таблиц — 4b, отдельным дизайном после baseline.
- [stage-4/plan.md](stage-4/plan.md) — пошаговый implementation-план этапа 4a (9 задач, TDD), сверен с реальным кодом и установленным API `Microsoft.Extensions.AI` 10.10.0; сверху — Global Constraints, «Pre-implementation corrections» (11 пунктов, переопределяют текст задач) и Review Focus. **Одобрен пользователем, выполнен (2026-10-03, Tasks 1–9; 267 тестов зелёные).**
- [stage-4/sdd-ledger.md](stage-4/sdd-ledger.md) — SDD-леджер реализации этапа 4a: пре-флайт таблица, все rulings (включая отступления от текста плана в пользу спеки), отложенные minor-находки ревью и их триаж финальным ревью.
- [stage-4/eval-candidates.md](stage-4/eval-candidates.md) — 47 кандидатов в eval-сет (acme 24 / globex 23, шесть категорий, предложенные holdout), написанные по исходным HTML до первого ответа системы; якоря проверены скриптом на тексте filing'ов; [eval-candidates.json](stage-4/eval-candidates.json) — та же выборка в формате `eval-set.json`. Ждут отбора и сверки пользователем.

- [stage-4b/design.md](stage-4b/design.md) — дизайн-спека этапа 4b: шесть отдельно измеряемых шагов за переключателями конфига (профиль ингеста на документе + проверка в харнессе, гибрид FTS+RRF, контекстный заголовок чанка, склейка коротких чанков, целые дробные числа, не больше 3 чанков из одного документа); дизайн одобрен в разговоре, письменная спека ждёт ревью.
- [stage-4b/plan.md](stage-4b/plan.md) — implementation-план 4b (8 задач, TDD), сверен с реальным кодом; 10 «Pre-implementation corrections» (переопределяют спеку: разрез прозы на границе раздела при включённых заголовках, мягкое ограничение по документу, `ChunkingContext` в чанкере и др.) и Review Focus. **Выполнен (2026-10-06, Tasks 1–8; 339 тестов зелёный: 193 unit, 77 eval-harness, 69 integration).** Лесенка измерений b0…b5 — на стороне пользователя.
- [stage-4b/sdd-ledger.md](stage-4b/sdd-ledger.md) — SDD-леджер реализации 4b: пре-флайт, все rulings, отложенные minor-находки и их триаж финальным ревью.

- [stage-5/mcp-csharp-sdk-current.md](stage-5/mcp-csharp-sdk-current.md) — C# MCP SDK 2.2.0 на 2026-10-09 (заменяет кросс-этапный `mcp-csharp-sdk.md`): спека 2026-07-28 и stateless по умолчанию, `MapMcp("/mcp").RequireAuthorization()`, **DI-scope тула = scope запроса только в stateless** (в stateful `ICurrentTenant` пуст), атрибуты тулов, structured output, `[Authorize]` на тулах, `AddCallToolFilter` для ошибок, встроенный `AddMcp()`/PRM.
- [stage-5/mcp-auth-and-clients.md](stage-5/mcp-auth-and-clients.md) — авторизация по спеке 2026-07-28 (необязательна; голый bearer без PRM допустим, PRM без authorization server невалиден), Claude Code (`--header`, `.mcp.json`, `headersHelper` для короткоживущих токенов, лимиты вывода, 60 с на запрос), Inspector v2 (CLI-режим, коды выхода), Desktop по-прежнему исключён, проверка `Origin` — на нашей стороне; варианты времени жизни токена A–E.
- [stage-5/rag-tool-design-over-mcp.md](stage-5/rag-tool-design-over-mcp.md) — как вендоры выставляют RAG через MCP (большинство — search + fetch, ответ пишет модель клиента; server-side answer — реже), tools vs resources, форма результата и лимиты Claude Code, описания/ошибки по гайду Anthropic, аннотации, prompt injection, варианты оценки A–G.
- [stage-5/design.md](stage-5/design.md) — дизайн-спека этапа 5: тулы `search_filings`/`list_filings` (+ `get_chunk_context` за `Mcp:ContextTool`) в Markdown, stateless как требование изоляции, JWT 60 мин + `headersHelper`, без PRM, Origin-middleware, фильтр ошибок тулов, тест изоляции через MCP с позитивным контролем, режим харнесса `--mode agent` (эксперименты M1/M2). Одобрен и выполнен (2026-10-09).
- [stage-5/plan.md](stage-5/plan.md) — implementation-план этапа 5 (10 задач, TDD), сверен с кодом и исходником SDK v2.2.0 (фильтр видит исключения тулов; `ServerInstructions`; `--tools ""` у `claude` 2.1.266); 10 «Pre-implementation corrections» и Review Focus. **Выполнен (2026-10-09, Tasks 1–10; 413 тестов зелёный: 230 unit, 96 eval-harness, 87 integration).** Замеры M1/M2 и ручные проверки — на стороне пользователя.
- [stage-5/sdd-ledger.md](stage-5/sdd-ledger.md) — SDD-леджер реализации этапа 5: пре-флайт таблица, rulings (в т.ч. «Always answer in English» в правилах агента, отключение хуков, прерывание прогона на ошибке CLI), отложенные minor-находки и их триаж финальным ревью; 425/425 тестов.
- [stage-5/testing-mcp-servers-dotnet.md](stage-5/testing-mcp-servers-dotnet.md) — тестирование in-process: `McpClient.CreateAsync` + `HttpClientTransport` поверх `WebApplicationFactory` (проверено спайком), stateless vs stateful в тестах, 401/clock skew, сырые POST'ы, набросок `Utils/TestMcp.cs` и теста изоляции через MCP.

- [stage-6/table-splitting-headers-and-captions.md](stage-6/table-splitting-headers-and-captions.md) — research 6B: где реально проваливаются таблицы (продолжения JPM без шапки и метки группы, подпись в прозе, сдвиг колонок из-за выброшенных пустых ячеек; Apple-вопросы из kickoff уже отвечены), как это делают Unstructured/Docling/LlamaIndex/MS sample-app/DataIngestion/edgartools (шапка только по `<th>`, которых в EDGAR нет), данные по форматам сериализации, варианты R/A/B/C/D/E.
- [stage-6/design.md](stage-6/design.md) — дизайн-спека 6B: сетка таблицы в `DocumentElement`, `TableFormatter` в чанкере (D ячейки, A′ шапка + метка группы, B подпись, C key-value), токены профиля только при включённом переключателе, `Chat:SourceContextHeader`, расширение eval-сета, лесенка t0…t4 (замеряет Claude). Одобрен пользователем 2026-10-11; поправки — в plan.md.
- [stage-6/plan.md](stage-6/plan.md) — implementation-план 6B (6 задач, TDD): сетка в reader'е, `TableLayout` (rowspan, склейка `$`/`%`, сноски, шапка, колонки), `TableFormatter`/`TableCaption`, чанкер с префиксами продолжений, `Chat:SourceContextHeader`, документы; 11 «Pre-implementation corrections» по прототипу на 4 filing'ах (выравнивание колонок 97.8–100 % таблиц с данными). **Одобрен и выполнен (2026-10-11, Tasks 1–6 + волна правок финального ревью; 488 тестов: 291 unit, 106 eval-harness, 91 integration).** Замеры t0…t4 — дальше.
- [stage-6/sdd-ledger.md](stage-6/sdd-ledger.md) — SDD-леджер 6B: пре-флайт, rulings, отложенные minor-находки и их триаж финальным ревью.

- [stage-6/run-anywhere-containers.md](stage-6/run-anywhere-containers.md) — research 6A: что сломается в контейнере (миграции/сидинг только в Development, секреты в dev-конфиге, запись под UID 1654, порт 8080), образы .NET 10 (chiseled/-extra, multi-arch), healthcheck без shell, `AddDbContextCheck`, миграции при старте под блокировкой Npgsql vs bundle, env/compose-интерполяция, NLog, volume, справка по Azure.
- [stage-6/design-6a.md](stage-6/design-6a.md) — дизайн-спека 6A: Dockerfile (chiseled-extra), `/health/live|ready`, флаги `Database:MigrateOnStartup`/`SeedDemoData` и `Demo:FilingsPath` (сидинг четырёх filing'ов из `samples/edgar` обычной загрузкой), валидация `Jwt:Secret`, `Swagger:Enabled`, `.env`, только stdout, volume `/data`, `tools/compose-smoke.sh`, раздел «How I'd deploy to Azure». Одобрен пользователем 2026-10-12.
- [stage-6/plan-6a.md](stage-6/plan-6a.md) — implementation-план 6A (6 задач): флаги старта и сидер без «Welcome», fail-fast секретов, `/health`, сидер filing'ов по манифесту, контейнер (NLog, Dockerfile, compose, `.env`, smoke в отдельном compose-проекте), документы. **Выполнен (2026-10-12; 513 тестов: 305 unit, 106 eval, 102 integration; смоук OK).**
- [stage-6/sdd-ledger-6a.md](stage-6/sdd-ledger-6a.md) — SDD-леджер 6A: пре-флайт, rulings (`${JWT_SECRET:-}`, общий dev-volume), отложенные minor-находки, цифры смоука.

- [stage-7/design.md](stage-7/design.md) — дизайн-спека Stage 7 (polish): GIF через Claude Code на Azure-копии API (:5219, база `lumen_demo`), скриншот Swagger, Mermaid (ingestion с таблицами, MCP-поток, Docker), README под 6B, средняя вычитка DECISIONS.md. Одобрена 2026-10-12.
- [stage-7/plan.md](stage-7/plan.md) — implementation-план Stage 7 (6 задач). **Выполнен (2026-10-12, native).**
- [stage-7/sdd-ledger.md](stage-7/sdd-ledger.md) — леджер исполнения Stage 7: rulings (скриншот с демо-сервера, `~/lumen-demo`, `env -i`, 38 с GIF), итоги финального ревью и отложенные minor.
- [discussion/stage-7-kickoff.md](discussion/stage-7-kickoff.md) — решения пользователя по объёму этапа, GIF, глубине вычитки, цифрам в Results.

### Этап 7 закрыт (2026-10-12)

README: GIF (Claude Code как Acme: 22,429, затем вопрос про JPMorgan — поиск возвращает только Apple), скриншот Swagger с реальным Azure-ответом, Results по текущим умолчаниям (35 вопросов = 30 + 5 табличных: 77 → 80 %), лесенка 6B, три диаграммы. DECISIONS.md: оглавление на 25 записей, без потери чисел. Медиа воспроизводимы скриптами в `tools/media/`. Все 7 этапов закрыты.

### Этап 6 закрыт (2026-10-12)

6B (таблицы): t2 по умолчанию, 80 % / MRR 0.565 против 77 % / 0.524 (без значимости). 6A (run anywhere): `docker compose up` поднимает API + Postgres с четырьмя filing'ами, образ 202 МБ, холодный старт 7.5 с. Деплой в Azure пропущен (раздел «How I'd deploy to Azure» в README). Решения — `DECISIONS.md` (5 записей от 2026-10-12). Следующий — Stage 7 (polish).

### Этап 5 закрыт (2026-10-11)

MCP-сервер `/mcp` (stateless — требование изоляции), тулы `search_filings`/`list_filings` (`get_chunk_context` выключен), JWT + `headersHelper`, харнесс `--mode agent`. Замеры: агент Claude Code + поиск 87 % = 87 % у `/api/ask` (3:3), M2 — тул соседних чанков не вызван ни разу. Решения — `DECISIONS.md` (5 записей от 2026-10-11); ход проверок и замеров — [discussion/stage-5-hands-on-findings.md](discussion/stage-5-hands-on-findings.md); что дальше — [roadmap.md](roadmap.md). Следующий — Stage 6 (деплой в Azure).

### Этап 4 закрыт (2026-10-07)

Итоговая конфигурация (вектор + контекстные заголовки + целые дробные числа, 10 источников, temperature 0.2) — значения по умолчанию в `appsettings.json`: accuracy 87 % против 70 % у baseline, 0 % ложных ответов; holdout 4/6. Все решения с цифрами — `DECISIONS.md`; ход замеров — [discussion/stage-4-hands-on-findings.md](discussion/stage-4-hands-on-findings.md). Следующий — Stage 5 (MCP).

### Stage 4a — точка входа для следующей сессии (обновлено 2026-10-03)

Состояние: реализация Stage 4a **выполнена** (`/api/ask`, `/api/settings`, секция `Chat`, харнесс `tools/Lumen.Eval`); `eval-set.json` пока пустой (`questions: []`). Сборка 0 warnings, 245 тестов зелёные.

Следующий шаг: пользователь идёт по [checklists/stage-4a-user-checklist.md](checklists/stage-4a-user-checklist.md) (Azure-deployment'ы, `Chat:*`, проба temperature, E0/E1), а Claude параллельно пишет ~40 кандидатов для eval-сета по исходным HTML из `../samples/edgar/`. Эксперименты Claude сам не запускает. Дизайн 4b (гибрид, починка таблиц) ждёт базовой линии E0/E1.

Предыстория этапа: [discussion/stage-4-kickoff.md](discussion/stage-4-kickoff.md) (рамка), [discussion/stage-3-hands-on-findings.md](discussion/stage-3-hands-on-findings.md) (Azure включён, ручная проверка Stage 3 пройдена), четыре research-файла выше.

Следующие этапы получат свои `.research/stage-N/` по той же логике.

> **2026-10-12:** четыре filing'а (Apple 10-K/10-Q, JPM 10-K/10-Q) переехали из `.research/stage-2/samples/` в отслеживаемую git папку `samples/edgar/` в корне репозитория (демо-сидинг Stage 6A). Папки `*_files` с картинками и `jpm-form424b2.htm` удалены. Старые ссылки в заметках заменены.

## discussion/

`.research/discussion/*.md` — сохранённые обсуждения этапов (решения, причины, наблюдения пользователя, чек-листы), чтобы их можно было перепроверить позже. По файлу на тему; не стенограмма, а журнал «вопрос → решение → почему → когда пересмотреть».

- [discussion/stage-2-design-decisions.md](discussion/stage-2-design-decisions.md) — таблица дизайн-решений этапа 2 (upload, единый `DocumentChunk`, `IDocumentReader`, PDF, AngleSharp, чанкер, размеры 250/60, что убрали и почему).
- [discussion/stage-2-hands-on-findings.md](discussion/stage-2-hands-on-findings.md) — ручная разведка пользователя на Apple/JPMorgan: «грязный» HTML, `tidy` и XBRL, ChunkViz-эксперименты, ошибка с 424B2.
- [discussion/stage-2-implementation-and-checklist.md](discussion/stage-2-implementation-and-checklist.md) — как шла реализация, дефекты, пойманные ревью, отложенное и чек-лист «что проверить руками».
- [discussion/stage-3-design-decisions.md](discussion/stage-3-design-decisions.md) — журнал дизайн-решений этапа 3 (12 решений: граница этапа, колонка vs таблица, переключатель провайдера, всё-или-ничего, строгость схемы, шов `IEmbeddingService`) и три находки research'а, изменившие дизайн.
- [discussion/stage-3-hands-on-findings.md](discussion/stage-3-hands-on-findings.md) — ручная проверка Stage 3 пользователем: Azure v1 на Foundry-ресурсе, семантический поиск, цифры загрузки при 400/100, гипотеза «250/60 оптимально» (до проверки eval'ом), видимые дефекты таблиц JPM.
- [discussion/stage-4-kickoff.md](discussion/stage-4-kickoff.md) — стартовое обсуждение этапа 4 (до research'а): Azure как блокер, mini-модель, совместное составление eval-сета, консольный харнесс, single-turn `/api/ask` без streaming, план экспериментов E0–E7, согласованный порядок работы.
- [discussion/stage-4-design-decisions.md](discussion/stage-4-design-decisions.md) — журнал дизайн-решений этапа 4 (полноразмерная модель, деление на 4a/4b, два режима харнесса, параметры только в конфиге + `/api/settings`, процесс составления eval-сета, бинарный LLM-судья на другом deployment'е).
- [discussion/stage-4-hands-on-findings.md](discussion/stage-4-hands-on-findings.md) — ручные наблюдения этапа 4a: квота free trial и выбор пары моделей (`gpt-4o` для ответов + `gpt-5-mini` судья), проба temperature (200), первое наблюдение «подпись таблицы оторвана от данных».
- [discussion/stage-4-incident-mixed-chunking.md](discussion/stage-4-incident-mixed-chunking.md) — инцидент: первый E0 посчитан на корпусе, нарезанном двумя конфигами (400/100 со Stage 3 и 250/60), а `/api/settings` показывал только текущий конфиг; как поймали (anchor lint → SELECT по размерам чанков), цифры до/после, уроки, кандидаты в 4b, черновик рассказа для интервью.
- [discussion/stage-5-hands-on-findings.md](discussion/stage-5-hands-on-findings.md) — ручные проверки этапа 5: Inspector UI v2 уходит в OAuth discovery без заголовка, Origin не отклонялся, CLI exit 3, изоляция по `documentId` на реальных данных, `lumen-token.sh`, Claude Code + `headersHelper` (восстановление после 401, модель видит Markdown и chunkId, ответы по-русски из-за пользовательского CLAUDE.md).
- [discussion/stage-5-design-decisions.md](discussion/stage-5-design-decisions.md) — что изменил research Stage 5 (stateless как требование изоляции, PRM без AS невалиден, `headersHelper`, Origin) и 11 решений brainstorming'а: только поиск, Markdown, токен 60 мин, `--mode agent`, тул соседних чанков за переключателем, без ресурсов и записи.
- [discussion/stage-6-kickoff.md](discussion/stage-6-kickoff.md) — старт этапа 6: деплой в Azure пропущен → «Run anywhere» (Dockerfile, compose, миграции/сидинг вне Development, `/health`, конфиг через env) + повтор шапки разрезанных таблиц; две реплики за nginx отложены; ключ в `0639d78` тестовый, не перевыпускать.
- [discussion/stage-4b-design-decisions.md](discussion/stage-4b-design-decisions.md) — решения пользователя для 4b после E1: гибрид первым, контекстный заголовок чанка, склейка коротких чанков, починка дробных чисел отдельным шагом, разнообразие по документам, параметры чанкинга на документе; один прогон на изменение.

## checklists/

`.research/checklists/*.md` — все чек-листы ручной проверки в одном месте (переехали сюда из `stage-N/` 2026-09-26 по просьбе пользователя), чтобы пройти по всем сразу в конце, не собирая их по папкам этапов. Имя файла хранит номер этапа, к которому он относится.

- [checklists/stage-2-user-checklist.md](checklists/stage-2-user-checklist.md) — чек-лист пользователя для ручной проверки этапа 2 (коммит, Swagger-прогон, качество чанков, JPMorgan, параметры, PDF, предупреждения тестов, открытые решения).
- [checklists/stage-2-checklist-after-stage-3.md](checklists/stage-2-checklist-after-stage-3.md) — пункты 5, 6 и 10 чек-листа этапа 2, отложенные пользователем на проверку после Stage 3 (параметры чанкинга, PDF, Delete/имена файлов/склейка ячеек/403 vs 404).
- [checklists/stage-3-user-checklist.md](checklists/stage-3-user-checklist.md) — чек-лист пользователя для ручной проверки этапа 3 (fake-провайдер, включение Azure, замеры, три отложенных пункта Stage 2); пользователь решил пройти его целиком в конце, а не по ходу этапа.
- [checklists/stage-4a-user-checklist.md](checklists/stage-4a-user-checklist.md) — чек-лист пользователя для этапа 4a (два chat-deployment'а, `Chat:*`/`Judge:*`, проба temperature, отбор eval-вопросов, E0 retrieval/full, калибровка судьи, разбор провалов E1).
- [checklists/stage-4b-user-checklist.md](checklists/stage-4b-user-checklist.md) — чек-лист пользователя для этапа 4b: лесенка b0…b5 (новая базовая линия, гибрид, заголовки, склейка, дроби, лимит на документ), точные команды user-secrets, когда нужен re-upload, метки прогонов и `compare`, откат неудачных шагов.
- [checklists/stage-5-user-checklist.md](checklists/stage-5-user-checklist.md) — чек-лист пользователя для этапа 5: Inspector UI/CLI, живая проверка `lumen-token.sh`, Claude Code с `headersHelper`, какой `Origin` шлёт Inspector, замеры M1 (агент + поиск) и M2 (+ `get_chunk_context`) против `final-full`.

## specification/

`.research/specification/<specName>.md` — спеки конвенций/решений, зафиксированные ПОСЛЕ того, как код уже написан и проверен на практике (в отличие от `design.md`/`plan.md`, которые пишутся ДО имплементации). Один файл — одна тема.

- [specification/csharp-project-conventions.md](specification/csharp-project-conventions.md) — конвенции C#-проекта, устоявшиеся по итогам пост-имплементационного ревью этапа 1 (DTO-слои, именование Interfaces, стиль конструкторов, GlobalUsings, папки Extensions/DependencyInjection/Constants, роутинг, комментарии).

## Порядок работы по этапу

Каждый этап проходит одну и ту же последовательность, прежде чем что-то коммитится:

1. **Research** — файлы верхнего уровня `.research/*.md` (кросс-этапные темы) или разведка внутри `.research/stage-N/`, если тема специфична для этапа.
2. **Planning + design** — `.research/stage-N/design.md` (что и почему) через brainstorming/грилинг, затем `.research/stage-N/plan.md` (как именно, по задачам) через writing-plans.
3. **Spec** — `.research/specification/<name>.md`, если по итогам имплементации/ревью устоялась конвенция, которую стоит зафиксировать отдельно от design/plan (design/plan остаются как есть — историческая запись того, что планировалось).
4. **Одобрение пользователя** — implementation не стартует, пока design.md/plan.md не одобрены явно.
5. **Implementation** — через `superpowers:subagent-driven-development` (или `executing-plans` для параллельной сессии), таск за таском, с ревью на каждом шаге и финальным ревью всей ветки.

Обсуждение этапа (решения и причины) сохраняется в `.research/discussion/*.md`.

Коммитит всё пользователь вручную — сессия Claude никогда не запускает `git commit` (см. `CLAUDE.md`).
