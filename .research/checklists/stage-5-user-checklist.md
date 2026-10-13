# Stage 5 — чек-лист пользователя: MCP-сервер, клиенты, замеры M1/M2

Составлен 2026-10-09 после реализации Stage 5 (413 тестов зелёные). Код готов; ручные проверки и замеры — твои, результаты приноси обратно. Отмечай `[x]` по ходу.

Контекст: `../stage-5/design.md`, `../stage-5/plan.md`, `../discussion/stage-5-design-decisions.md`.

## Что построено (кратко)

- Эндпоинт `/mcp` (Streamable HTTP), **stateless** — это требование изоляции тенантов: в Stateful тул видит пустой тенант (доказано тестом с позитивным контролем).
- Тулы (Markdown-результат): `search_filings(query, topK=10 [1–20], documentId?)`, `list_filings()`, `get_chunk_context(chunkId, before=1, after=1 [0–3])` — последний только при `Mcp:ContextTool=true`.
- Проверка `Origin` на `/mcp` (`Mcp:AllowedOrigins`; в Development разрешён `http://localhost:6274`).
- Харнесс `--mode agent`: на каждый вопрос запускается `claude -p` с `--tools ""`, `--strict-mcp-config`, `--allowedTools "mcp__lumen__*"`, хуки отключены (`--settings '{"disableAllHooks":true}'`); в правилах агента «Always answer in English.» и предложение-отказ из Stage 4. Ошибка CLI (ненулевой код / `is_error`) прерывает прогон с кодом 1 (частичные результаты сохраняются); таймаут — вопрос неверный с причиной `timeout`. Конфиг: `Agent:ClaudePath/Model/TimeoutSeconds/McpUrl` в `tools/Lumen.Eval/appsettings.json`.

## 1. Подготовка

- [x] Установлены `jq` и `curl` (нужны `tools/mcp/lumen-token.sh`; проверить `jq --version`, `curl --version`).
- [x] `docker compose up -d`.
- [x] Сервер запущен с Azure-конфигом (`dotnet run --project src/Lumen.Api`, user-secrets: `Chat:*`, `Embeddings:*`), порт 5119.
- [x] Корпус загружен под текущим профилем: `GET /api/settings` → `ingestProfile` совпадает с профилем документов в `GET /api/documents` (по умолчанию `hdr=on`, `dec=on`). Если нет — re-upload.

## 2. Inspector UI

- [x] `npx @modelcontextprotocol/inspector`; Transport `Streamable HTTP`, URL `http://localhost:5119/mcp`, заголовок `Authorization: Bearer <токен>` (токен: `POST /api/auth/token`).
- [x] `tools/list` показывает 2 тула (`search_filings`, `list_filings`).
- [x] Вызвать `list_filings` и `search_filings`; что видно во вкладке результата (Markdown целиком? chunkId на месте?).
- [x] **Записать, какой `Origin` шлёт прокси Inspector** (лог NLog `Rejected MCP request from origin …` либо 200 в ответе); если отклоняет — поправить `Mcp:AllowedOrigins` и зафиксировать значение.

#### my ntoes

- никаких Rejected MCP request from origin логов не было

## 3. Inspector CLI

- [x] Без токена: `npx @modelcontextprotocol/inspector --cli http://localhost:5119/mcp --transport http --method tools/list` → код выхода 3 (`echo $?`).
- [x] С токеном: `... --header "Authorization: Bearer $TOKEN" --method tools/list`.
- [x] `--method tools/call --tool-name search_filings --tool-arg query="..."` под Acme и под Globex — в каждом ответе только свои `documentId`.

## 4. Живая проверка `tools/mcp/lumen-token.sh`

- [x] Успех: `LUMEN_EMAIL=admin@acme.test LUMEN_PASSWORD='Passw0rd!' tools/mcp/lumen-token.sh` печатает `{"Authorization":"Bearer …"}`.
- [x] Неверный пароль: `LUMEN_PASSWORD=wrong …` → сообщение `lumen-token: login to … failed`, ненулевой код выхода.
- [x] Нужен `jq` (без него — голое `command not found`, это известный minor).

## 5. Claude Code

- [x] `export LUMEN_EMAIL=admin@acme.test LUMEN_PASSWORD='Passw0rd!'`; положить `.mcp.json` из `tools/mcp/mcp.example.json` (абсолютный путь к `lumen-token.sh`); `claude mcp list` / `/mcp` показывает `lumen` подключённым.
- [x] 3–5 вопросов руками: числовой, из прозы, «ловушка» чужого тенанта (спросить про компанию другого тенанта — ожидается отсутствие данных, не утечка).
- [x] **Проверить, что модель видит Markdown целиком** — попросить процитировать `chunkId` из результата.
- [x] Через час (или с вручную испорченным токеном) — переподключается ли `headersHelper` сам, без ручного вмешательства.

#### Проверка Claude (2026-10-11)

Разделы 1, 3, 4, 5 повторены Claude — всё прошло; детали и цифры: `../discussion/stage-5-hands-on-findings.md`.

## 6. M1: агент с `search_filings`/`list_filings` против final-full

- [x] `Mcp:ContextTool=false` (по умолчанию), перезапуск сервера.
- [x] Посмотреть один транскрипт агентского прогона: пользовательский `~/.claude/CLAUDE.md` (и output style) всё ещё доходит до `claude -p` (отключены только хуки) — убедиться, что он не искажает ответы.
- [x] (Опционально, до того как на это полагаться) эксперимент: `claude --safe-mode -p "..." --mcp-config <файл> --strict-mcp-config` — переживает ли MCP-сервер `--safe-mode`? Если да, это способ отрезать пользовательский `CLAUDE.md`; записать результат.
- [x] `cd tools/Lumen.Eval && dotnet run -- run --mode agent --label m1-agent`.
- [x] В первом агентском прогоне убедиться, что `permission_denials` в `result`-событии пуст (теперь это проверяется: непустой список прерывает прогон с кодом 1 и именами отклонённых тулов — значит, неверный `--allowedTools`, а не плохая точность).
- [x] Открыть один транскрипт и поискать следы пользовательского `~/.claude/CLAUDE.md` (язык ответа, стиль, лишние правила).
- [x] `dotnet run -- compare runs/20261007-094501_final-full runs/<m1-каталог>` — записать accuracy, false answers, flips, p, стоимость, вызовы тулов на вопрос.

## 7. M2: + `get_chunk_context`

- [x] `dotnet user-secrets set "Mcp:ContextTool" "true" --project src/Lumen.Api` (или appsettings), перезапуск; `tools/list` теперь показывает 3 тула.
- [x] `dotnet run -- run --mode agent --label m2-agent-context`; `compare` с M1.
- [x] Посмотреть, вызывал ли агент `get_chunk_context` и на каких вопросах.

M1/M2 прогнал Claude (2026-10-11): итоги и разбор — `../discussion/stage-5-hands-on-findings.md`, раздел «M1 / M2».

## 8. Вернуть цифры

- [ ] Принести числа M1/M2, наблюдения по `Origin`, `headersHelper` и транскрипту — по ним пишутся записи `DECISIONS.md` (включая «stateless = требование изоляции»).
