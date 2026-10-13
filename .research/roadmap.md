# Lumen — roadmap: что осталось сделать и что не забыть

Обновлено 2026-10-13 (Stage 7 закрыт — все 7 этапов готовы; открыт один вопрос по `Jwt:Secret` в `appsettings.json`, см. Stage 7; дальше только отложенное ниже). Один файл на весь проект: текущий этап по шагам, следующие этапы и все отложенные «вернуться позже» с прошлых этапов — с указанием, откуда пункт взялся и что должно случиться, чтобы к нему вернуться. Отмечай `[x]` по ходу; закрытое не удаляй, чтобы история была видна.

Источники: `DECISIONS.md` (раздел «Would revisit when» каждой записи), `.research/discussion/*.md`, `.research/checklists/*.md`, `.research/stage-5/design.md` и `plan.md`.

---

## Stage 5 — MCP server ✅ закрыт 2026-10-11

Порядок этапа: research ✅ → дизайн ✅ → план ✅ → одобрение ✅ → реализация ✅ → **ручные проверки и замеры (пользователь)** → DECISIONS/README → закрытие.

- [x] Research: 4 заметки в `stage-5/` (SDK, auth + клиенты, дизайн тулов, тестирование).
- [x] Brainstorming → `stage-5/design.md`, журнал `discussion/stage-5-design-decisions.md`.
- [x] План `stage-5/plan.md` (10 задач).
- [x] **Пользователь одобрил план, исполнение — subagent-driven** (2026-10-09): исполнитель + ревью на каждую задачу, финальное ревью ветки. Без коммитов — всё в staging.
- [x] Реализация (Claude) — готова 2026-10-09, всё в staging, ждёт твоего коммита:
  - [x] Task 1 — `ContextHeader` в результатах поиска (vector + hybrid SQL)
  - [x] Task 2 — `GetChunkContextQuery` + выборка чанков по диапазону индексов
  - [x] Task 3 — пакет MCP SDK 2.2.0, `McpConstants`, Markdown-форматтер
  - [x] Task 4 — тулы `search_filings` / `list_filings` / `get_chunk_context`
  - [x] Task 5 — фильтр ошибок тулов, Origin-middleware, `McpOptions`
  - [x] Task 6 — подключение `/mcp`, `/api/settings.mcp`, интеграционные тесты, **тест изоляции через MCP**
  - [x] Task 7 — `tools/mcp/lumen-token.sh` для `headersHelper`
  - [x] Task 8 — парсеры харнесса (`stream-json`, блоки чанков)
  - [x] Task 9 — харнесс `--mode agent`
  - [x] Task 10 — CLAUDE.md, чек-лист пользователя
  - [x] Финальное ревью всей ветки (0 critical; 2 important + 9 minor исправлены одной волной; 425/425 тестов: unit 230, eval 106, integration 89; 0 warnings)
- [x] Чек-лист пройден (пользователь + Claude, 2026-10-11); M1: 87 % = 87 % у `/api/ask` (3:3), M2: `get_chunk_context` не вызван ни разу. Чек-лист `checklists/stage-5-user-checklist.md` (готов):
  - [x] Inspector UI — тулы работают; **какой `Origin` шлёт прокси Inspector** → поправить `Mcp:AllowedOrigins`, если нужно.
  - [x] Inspector CLI — без токена код 3; два тенанта видят только свои `documentId`.
  - [x] Claude Code — `headersHelper`; модель видит Markdown целиком; переподключение после истечения токена.
  - [x] **M1:** `--mode agent` (ContextTool off) vs `runs/20261007-094501_final-full` → нужен ли `ask` в MCP.
  - [x] **M2:** ContextTool on vs M1 → оставлять ли `get_chunk_context`.
- [x] DECISIONS.md — 5 записей (2026-10-11): stateless, search-only, `get_chunk_context` off, JWT + headersHelper, изоляция агентского eval. Было: только с цифрами M1/M2 и с наблюдением из Task 6 (Stateful → пустой тенант, тест падает):
  - [x] search-only vs `ask` в MCP (M1);
  - [x] тул соседних чанков (M2);
  - [x] stateless как требование изоляции, а не масштабирования;
  - [x] JWT 60 мин + `headersHelper` вместо PAT/OAuth; без PRM; Desktop исключён.
- [x] README: раздел «Connect from Claude Code (MCP)» — авторизация (тот же JWT, статический токен или `headersHelper`), команды `claude mcp add` / `add-json`, тулы, примеры вопросов, troubleshooting (DCR 404 = заголовок не дошёл); строка `/mcp` в таблице API (2026-10-11).
- [x] Решение пользователя (2026-10-11): авторизацию MCP **оставляем как есть** (JWT 60 мин + `headersHelper`); PAT — если понадобятся реальные пользователи раньше Stage 6, OAuth 2.1 (Entra ID / OpenIddict + PRM) — на Stage 6. Клиенты уже готовы к OAuth: без заголовка Claude Code и Inspector сами идут в discovery/DCR и получают 404.
- [x] README: Stage 5 в roadmap ✅, раздел про MCP (как подключиться, тулы, результат M1), строка в таблице «What's interesting here», если M1 даст историю.
- [x] `CLAUDE.md`: статус «Stage 5 complete» с цифрами; `.research/README.md` — «Этап 5 закрыт».

### Решения Stage 5, которые ждут результата замера

- [x] M1: Claude по сырым чанкам **не хуже** `/api/ask` (87 % = 87 %) → `ask_filings` не добавляем (DECISIONS 2026-10-11).
- [x] M2: выигрыша нет (0 вызовов, 0:0) → `get_chunk_context` выключен по умолчанию, код остаётся (DECISIONS 2026-10-11).

---

## Stage 6 — Run anywhere + таблицы (вместо деплоя в Azure)

Решение пользователя (2026-10-11): реальный деплой в Azure **пропускаем**. Stage 6 = (A) «Run anywhere» — проект поднимается в контейнере у кого угодно, без облака; (B) этап качества: строка заголовков в разрезанных таблицах. Порядок тот же (research → дизайн → план → одобрение → реализация). Обсуждение — `discussion/stage-6-kickoff.md`.

**Ключ Azure в коммите `0639d78` — тестовый (free trial), перевыпускать не нужно** (решение пользователя 2026-10-11). Пункт закрыт, больше не поднимать.

### A. Run anywhere ✅ закрыт 2026-10-12
- [x] Research, дизайн (`stage-6/design-6a.md`), план (`plan-6a.md`), реализация (6 задач + волна правок финального ревью).
- [x] Dockerfile (chiseled-extra, non-root, amd64+arm64), compose (healthcheck Postgres, `5119:8080`, volume `/data`, `/seed`), `.env.example`.
- [x] Флаги `Database:MigrateOnStartup`/`SeedDemoData`, `Demo:FilingsPath`; сидер filing'ов из `samples/edgar` (переехали из `.research`); «Welcome» удалены.
- [x] `/health/live|ready`; fail-fast `Jwt:Secret`/`ConnectionStrings:Default`; `Swagger:Enabled`; `Logs:FileMinLevel`.
- [x] `tools/compose-smoke.sh` — SMOKE OK (отдельный проект `lumen-smoke`). Цифры: образ 202 МБ (полный aspnet 294 МБ), холодный старт 7.5 с, тёплый 1.2 с.
- [x] README («Run with Docker», «How I'd deploy to Azure»), CLAUDE.md, 2 записи DECISIONS.md.
- [ ] Отложено: plain chiseled + invariant (−15 МБ) после прогона характеризационного теста; healthcheck-подкоманда API для двух реплик; таймаут ready-проверки; тест «Swagger выключен вне Development».
- [ ] Окружение этой машины: DNS Docker Desktop не резолвит nuget.org (чистая сборка образа падает на restore; обход `--add-host`); интеграционные тесты требуют `DOCKER_API_VERSION=1.43`.

### B. Разрезанные таблицы повторяют строку заголовков ✅ закрыт 2026-10-12
- [x] Research, дизайн, план, реализация (6 задач + волна правок финального ревью, subagent-driven).
- [x] +5 JPM-вопросов с ответом в продолжении таблицы (одобрены пользователем), eval-set 41.
- [x] Лесенка t0…t4 + holdout (Claude): t2 (ячейки + шапка + подпись) 80 % / MRR 0.565 против 77 % / 0.524, значимости нет (2:1); key-value и заголовок в промпте — без выигрыша. Баг «первый чанк перепакован без подписи» найден замером и исправлен (профиль v2).
- [x] Решение пользователя: t2 по умолчанию (`appsettings.json`), 3 записи DECISIONS.md, CLAUDE.md обновлён. Журнал — `discussion/stage-6-hands-on-findings.md`.
- [ ] Известные ограничения (пользователь: оставить как есть): неверная «шапка» у текстовых таблиц с поздней числовой строкой (`g-dimon-10b5-1`); шапка в отдельном `<table>` выше (143 таблицы JPM 10-K, вариант E); однострочное продолжение с длинной повторённой шапкой выпадает из top-10; 3 из 5 новых вопросов не находятся ни на одной ступени. Мелочи ревью: метка группы резервируется только при `RepeatHeader`; пол max/4 может переполниться при подписи + шапке у предела.

### Отложено (пользователь: «на потом»)
- [ ] Эксперимент: две реплики API за nginx (round-robin) → MCP-изоляция и `headersHelper` без sticky sessions. Триггер: когда понадобится доказать масштабирование stateless MCP.

### Отпало вместе с деплоем (если когда-нибудь вернёмся к Azure)
Managed identity вживую; Azure Database for PostgreSQL Flexible Server + версия pgvector; Blob Storage для сырых файлов; Key Vault / App Settings; логи в App Service / Application Insights; HTTPS-only; stateless MCP на нескольких инстансах App Service; тип/время жизни MCP-токена в проде (варианты B/C из DECISIONS Stage 5).

## Stage 7 — Polish ✅ закрыт 2026-10-12

- [x] README: GIF работы через MCP-клиент (Claude Code, Azure-копия API на :5219, база `lumen_demo`), скриншот Swagger (реальный ответ `/api/ask`).
- [x] Один `docker compose up` поднимает всё (сделано на Stage 6A).
- [x] Mermaid: ingestion с табличной веткой, новая MCP-диаграмма (sequence), Docker-схема; все 7 блоков рендерятся.
- [x] DECISIONS.md: оглавление (25 записей, у каждой число), дубль убран, ссылки на «Stage 6 deploy» исправлены, длинные Why сжаты без потери чисел.
- [x] README под 6B: Results по текущим умолчаниям (35 основных вопросов = исходные 30 + 5 табличных, retrieval по 29 размеченным; плюс holdout 6), лесенка t0–t4, табличные ключи в Configuration, roadmap ✅.
- [x] Скрипты медиа: `tools/media/demo-server.sh`, `mcp-demo.tape`, `swagger-screenshot.mjs`. Демо-база `lumen_demo` оставлена для пересъёмки; удалить: `docker exec dotnet-mcp-rag-server-postgres-1 psql -U postgres -c 'drop database lumen_demo'`; папка клиента `~/lumen-demo`.
- [x] Финальное ревью (свежий агент, opus): 0 critical, 3 important — исправлены: счёт вопросов «41 / 35 scored» → «35 = 30 + 5, retrieval по 29»; инструкция пересъёмки Swagger (`LUMEN_URL`, демо-сервер, `npx playwright install chromium`); строка генерации `.mcp.json` из шаблона в шапке tape. Ещё 3 minor переведены в important и исправлены: «+58 % 10-Q chunks» в индексе DECISIONS, «a prototype aligned…» для 97.8 %, «Task 9…13» в записи 2026-09-10. Леджер — `stage-7/sdd-ledger.md`.
- [ ] Пользователь: посмотреть GIF/скриншот на GitHub после push (`checklists/stage-7-user-checklist.md`) и закоммитить.
- [ ] **Открытый вопрос (2026-10-13): `Jwt:Secret` в `appsettings.json`.** Пользователь сам добавил dev-секрет в `appsettings.json` (не закоммичено) и попросил перенести туда всё из `appsettings.Development.json`. Claude не перенёс и предложил решить, что чинится: (1) compose-API в crash-loop → `.env` с `JWT_SECRET` (рекомендация, строку из `appsettings.json` откатить); (2) compose без `.env` → дефолт dev-секрета в `docker-compose.yml`; (3) `dotnet run` не в Development → профиль запуска. Почему не в `appsettings.json`: секрет попадает в образ и публичный репо — контейнер без своего секрета молча подписывает JWT известным ключом, и любой выпускает токен с чужим `tenant_id` (ломает fail-fast из DECISIONS 6A); compose это и не чинит — `Jwt__Secret: ${JWT_SECRET:-}` подставляет пустую строку поверх `appsettings.json`; `Host=localhost` и автосид демо-юзеров по умолчанию тоже не для всех окружений. Ждёт ответа пользователя.
- [ ] Окружение: compose-API сейчас в crash-loop — в `.env` нет `JWT_SECRET` (Stage 6A caveat); это тот же вопрос, что выше. Из-за него в сессии 2026-10-12 не подключился `lumen` MCP (ECONNRESET).
- [ ] Отложенные minor ревью Stage 7: на MCP-схеме нет шага authorization и MediatR; стрелка «starts after healthcheck» на Docker-схеме читается как «api запускает postgres»; `demo-server.sh` жёстко задаёт пароль/порт Postgres и молча уходит на Fake без user-secrets; команда удаления `lumen_demo` завязана на имя контейнера (`docker compose exec postgres …` переносимее); в GIF видны баннеры аккаунта («login expires», «Claude Pro», «auto mode»); в tape две паузы подряд.

---

## Отложенное с прошлых этапов (вернуться, когда сработает триггер)

### Данные и чанкинг (Stage 2 / 4)

- [x] **Разрезанная большая таблица не повторяет строку заголовков** — закрыто в 6B (`TableHeaders`, 2026-10-12). Остаток (`g-dimon-10b5-1`, шапка в отдельном `<table>`) — в «Известных ограничениях» 6B выше.
- [x] **Подпись таблицы остаётся в соседнем прозаическом чанке** — закрыто в 6B (`TableCaptions`: подпись копируется на каждый чанк таблицы).
- [x] Пустые ячейки таблиц выбрасываются → колонки съезжают; `$ | 45,781` и `42 | %` в отдельных ячейках — закрыто в 6B (`TableGrid` в HTML-ридере + `TableCells`: пустые ячейки сохраняют позицию, `$`/`%` склеиваются по сетке; fallback в компактные строки, если колонки не выравниваются).
- [ ] Сравнения 10-K vs 10-Q (нужны два чанка из двух filing'ов): 0–50 % в eval. Кандидаты: агент с несколькими поисками (M1 покажет), multi-query, reranking.
- [ ] PDF-эвристика таблиц не откалибрована; ручная проверка на реальном PDF так и не сделана (checklists/stage-2-checklist-after-stage-3.md).
- [ ] `CharStart`/`CharEnd` приблизительные — не годятся для цитат с позициями.
- [ ] Осиротевший raw-файл, если `CompleteAsync` упадёт после записи файла.
- [ ] Контекстный заголовок: замерить `ContextTopK=10` **без** заголовков; если не хуже — убрать (DECISIONS 2026-10-07). Также попробовать заголовок только для FTS или только с разделом.
- [ ] Reindex из сохранённого raw-файла: превратил бы профиль ингеста из проверки в триггер автоматического перечанкинга (DECISIONS 2026-10-06).

### Поиск и генерация (Stage 3 / 4)

- [ ] BM25 (`pg_textsearch` / ParadeDB) вместо `ts_rank_cd` — отдельный эксперимент, если в eval появятся вопросы на точные коды/термины.
- [ ] Reranking top-10 → top-5, если стоимость/латентность станут ограничением.
- [ ] HNSW: когда корпус вырастет на порядок (десятки тысяч чанков на тенанта) и p50 поиска > ~1 с; тогда же тенант-фильтр при ANN (iterative scans / partial index / партиционирование).
- [ ] RLS в Postgres как второй барьер — если сырых запросов станет больше одного-двух.
- [ ] Лимит чанков на документ — если у тенантов появятся десятки однотипных filing'ов.
- [ ] Кэш эмбеддингов — если прогоны упрутся в TPM-квоту.
- [ ] Модель для ответов: при Pay-As-You-Go и квоте на `gpt-6-sol` — сменить `Chat:Deployment`, `compare`, переключать только при McNemar p < 0.05.
- [ ] Промпт (E6) — если появятся ложные ответы или сменится модель.

### Eval

- [ ] Eval-сет мал для значимости: было 30 (Stage 4: 7:2, p = 0.18), в 6B стало 35 (+5 табличных; 2:1, p = 1.0 — нужно ~6:0). Расширять дальше, особенно сравнения 10-K vs 10-Q.
- [ ] Holdout: прогнан на финале Stage 4 и на финале 6B (оба 4/6; в 6B дважды — до и после исправления бага перепаковки, исправление общее). Следующий прогон — только на новой финальной конфигурации.

### Auth и MCP (Stage 1 / 5)

- [ ] Названия документов для агента: «Morgan 10-K» агент принял за Morgan Stanley и отказался без поиска (M1) — переименовать в «JPMorgan Chase 10-K/10-Q» при следующей перезагрузке корпуса (сломает сравнимость со старыми прогонами — делать с новым baseline).
- [ ] `documentId` в схеме `search_filings` — `string|null` (union-тип, предупреждение Inspector `--strict`; не принимают строгие схемы OpenAI/Gemini) → необязательный `string`.
- [ ] Мягкие отказы агента (33 % «не раскрыто, но вот соседние цифры»): если важно — описание тула/инструкции сервера с явной просьбой отвечать фразой-отказом.

- [ ] `/register` и `/tenants` (отложено со Stage 1, сейчас только сидинг).
- [ ] Claude Desktop / claude.ai: OpenIddict + CIMD на публичном HTTPS — только как отдельная stretch-цель, если понадобится демо через Desktop (#644/#685 открыты).
- [ ] MCP-ресурсы `lumen://documents/{id}` — если появится сценарий, где пользователь сам выбирает документы через `@`.
- [ ] Отдельная audience для MCP-токенов — вместе с решением о типе токенов при реальном деплое (Stage 6 деплой пропущен).

### Мелкие ручные проверки, так и не отмеченные

- [ ] `DELETE /api/documents/{id}` руками: 204, документ и чанки 404, файл исчез; чужой тенант 404; Member 403.
- [ ] Имена папок/файлов в `App_Data/Documents`; не-ASCII → `unnamed`.
