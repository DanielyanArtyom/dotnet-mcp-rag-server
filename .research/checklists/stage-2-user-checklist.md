# Stage 2 — чек-лист пользователя (что проверить руками)

Составлен 2026-09-19 после реализации Stage 2. По правилу проекта (см. `CLAUDE.md`, «Working with the user») эксперименты и наблюдения делает пользователь, результат приносит обратно. Отмечай `[x]` по ходу.

Контекст: `progress.md` (итог этапа), `../discussion/stage-2-*.md` (журнал решений и находок), `sdd-ledger.md` (все ревью и rulings).

## 1. Перед коммитом
- [x] `git status` и `git diff --cached --stat`: в staging только файлы этапа (~77 после переноса Domain по папкам). Среди них миграция `AddDocumentChunksAndRawFileMetadata`, `tests/Lumen.IntegrationTests/Fixtures/apple-10k-fy2025.htm` (1,4 МБ), правки `CLAUDE.md`/`DECISIONS.md`. Не должно быть `.research/`, `.superpowers/`, `App_Data/`.
- [x] Перечитать запись в `DECISIONS.md` («Таблицы… атомарные чанки…»). Решение пользователя: пока оставить как есть.
- [x] Онлайн запустить `dotnet list package --vulnerable --include-transitive` (в моей среде не было сети).
- [x] `DOCKER_API_VERSION=1.43 dotnet test` — ожидается 59 unit + 24 integration.
- [x] Закоммитить самому (ассистент коммиты не делает).

## 2. Ручной прогон через Swagger
- [x] `docker compose up -d`, затем `dotnet run --project src/Lumen.Api`. Миграция применяется сама (Development), ошибок про колонки `RawFilePath`/`FilingDate` нет.
- [x] Логин `admin@acme.test` / `Passw0rd!` → **Authorize** в Swagger.
- [x] `POST /api/documents/upload`: файл Apple 10-K (`samples/edgar/aapl-20250927.html`), любой `title`, `filingDate=2025-10-31` → 201, `chunkCount` около 296.
- [x] Файл лежит в `src/Lumen.Api/App_Data/Documents/{tenant-name}_{TenantId}/{title}_{DocumentId}.html` (например `Acme-Corp_<guid>/Apple-10-K_<guid>.html`), `git status` его не показывает.
- [x] `GET /api/documents/{id}/chunks` → чанки по порядку `chunkIndex`.
- [x] Изоляция руками: залогиниться как `admin@globex.test`, запросить chunks документа Acme → 404 (не пустой 200).
- [x] Ошибки: `.docx` → 400; битый файл (текст, переименованный в `.pdf`) → 400; без `title` → 400; без `filingDate` → 400; `title` длиннее 500 → 400; файл больше 20 МБ → отказ (лимит тестами не проверяется — TestServer его не поддерживает, только руками).

## 3. Качество чанков (главное)
Запрос в Postgres (контейнер из `docker compose`, база `lumen`):

```sql
SELECT "ChunkType", count(*), min("TokenCount"),
       percentile_cont(0.5) WITHIN GROUP (ORDER BY "TokenCount") AS median, max("TokenCount")
FROM "DocumentChunks" GROUP BY "ChunkType";
```

- [x] Микро-чанки: минимум был 4 токена. Найти чанки короче ~20 токенов и понять, что это (заголовки, «Item 1A.», хвосты).
- [x] Таблицы глазами: `Americas | $ | 45781` (отдельные ячейки `$`), пропавшие пустые ячейки, съехавшие колонки. Сравнить с оригиналом в браузере.
- [x] Заголовки: у EDGAR они жирным `span`, а не `h1`–`h6`, поэтому `Heading`-элементов почти нет. Мешает ли это секциям («Item 7», «Item 1A»)?
- [x] Потеря текста: в Content 210K символов против ~254K видимых в исходнике. Взять абзацы из середины документа и проверить, что они есть.
- [x] Артефакты вёрстки: повторяющиеся колонтитулы страниц, «Table of Contents», номера страниц внутри абзацев.
- [x] Скрытый iXBRL-блок (`ix:header`) не попал в чанки.
- [x] Большие таблицы: у разрезанных чанков нет строки заголовков. Найти такой случай и оценить, насколько это плохо.

## 4. JPMorgan (самый «табличный» стресс-тест)
- [x] Загрузить 10-K (`jpm-20251231.html`) и 10-Q (`jpm-20260630.html`) под `admin@globex.test`.
- [x] Сравнить с Apple: число чанков, доля `Table`, максимум и p95 токенов, время запроса, сколько таблиц пришлось резать по строкам.
- [x] Вложенные таблицы (у Apple не нашёл) и банковские таблицы с многоуровневыми заголовками.

## 5. Параметры (то, что идёт в DECISIONS.md)
- [x] Менять `Chunking:MaxTokensPerChunk` / `OverlapTokens` в `appsettings.json` (например 250/60 → 400/100 → 150/40), перезапускать, загружать тот же файл заново, сравнивать те же числа.
- [x] Записывать наблюдения «попробовал X → стало Y → поэтому Z» с цифрами.

## 6. PDF (проверялось только на синтетике)
- [x] Загрузить реальный PDF с таблицей (например распечатать страницу 10-Q в PDF): что стало `Table`, есть ли ложные срабатывания на прозе.
- [x] Известные слабости: таблицы из двух колонок читаются как проза, а проза с большими пробелами может быть принята за таблицу.

## 7. Решения для тебя
- [x] `DocumentDto` и `Content`: решено 2026-09-20 — `Content` не возвращается, пока не передан `?includeContent=true` (список, `GET /{id}`); upload его тоже не возвращает.
- [x] `DELETE /api/documents/{id}` добавлен 2026-09-20 (Admin; строка → каскад чанков → raw-файл). Проверить руками — см. раздел 10.
- [x] Файловое хранилище на диске сломается на Azure App Service (Stage 6). Записать в `DECISIONS.md` как «когда пересмотрю» (переезд на Blob Storage).

## 8. Предупреждения в выводе тестов (разобрано 2026-09-20)
- [x] `LuckyPennySoftware.MediatR/AutoMapper.License`: MediatR 14 и AutoMapper 16 — коммерческие пакеты. Получить бесплатный Community-ключ на luckypennysoftware.com и задать его переменной окружения `LUCKYPENNY_LICENSE_KEY` (в пакетах также найдена `MEDIATR_LICENSE_KEY`) или user-secrets, не коммитить. Точное имя переменной по документации не проверено, найдено по строкам в DLL. Не глушить эти предупреждения в `nlog.config`, иначе пропадёт и продакшен-предупреждение.
- [x] `A request body size limit could not be applied…` — это `[RequestSizeLimit]` на upload под `TestServer` (в Kestrel работает). При желании заглушить категории `EndpointRoutingMiddleware` и `RequestSizeLimitFilter` в `nlog.config` только для тестов.
- [x] `GlobalExceptionHandler … Handled … as 401/400` — ожидаемые предупреждения от намеренных негативных тестов. Опционально понизить 4xx до `Information`.

## 9. Открытое после рефакторинга (2026-09-20)
- [x] Ты просил вынести usings в `GlobalUsings.cs`. Правило в `CLAUDE.md` («только если namespace используется в 2+ файлах») теперь нарушено для перенесённых. Решить: поменять правило в `CLAUDE.md` или вернуть локальные usings.
- [x] Domain разложен по папкам, namespaces теперь `Lumen.Domain.Entities` / `Lumen.Domain.Enums` (usings обновлены во всех проектах).

## 10. Добавлено после ревью пользователя (2026-09-20)
- [ ] Проверить руками Delete: `DELETE /api/documents/{id}` → 204, документ и chunks дают 404, файл из `App_Data` исчез; чужой тенант → 404; Member → 403.
- [ ] Посмотреть новые имена папок/файлов в `App_Data/Documents` (`<tenant-name>_<guid>/<title>_<guid>.<ext>`); не-ASCII названия превращаются в `unnamed`.
- [ ] Ячейки вида `42 | %` и `$ | 45,781` в таблицах JPMorgan НЕ склеиваются (жёсткая склейка символов откатана по просьбе пользователя): парсер должен решать это сам, обобщённо — способ ещё не выбран.
- [ ] 403 vs 404 для чужого тенанта: оставлено 404 (глобальный фильтр делает строку невидимой; 403 раскрыл бы, что такой id существует в другом тенанте). Если не согласен — сказать.

