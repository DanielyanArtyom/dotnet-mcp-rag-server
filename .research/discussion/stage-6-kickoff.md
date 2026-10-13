# Stage 6 — стартовое обсуждение (2026-10-11)

Перед этапом Claude перечитал `.research/README.md`, `roadmap.md`, обсуждения и находки Stage 5, `DECISIONS.md`, упоминания деплоя в research'е и текущий `Program.cs`.

## Вводная

Исходный Stage 6 — деплой в Azure App Service (managed identity, Flexible Server + pgvector, Blob Storage, Key Vault, App Insights, health checks). Пользователь заранее сказал, что деплой, скорее всего, пропустит.

Наблюдения по коду, которые не зависят от облака:
- Dockerfile для API нет; `docker-compose.yml` поднимает только Postgres — а Stage 7 обещает «один `docker compose up`».
- `UseDatabaseMigrationAsync` и сидинг работают только в Development → в контейнере (Production) база не создастся, залогиниться нельзя.
- `/health` нет.
- JWT secret лежит в `appsettings.Development.json`; `AllowedHosts = *`.

## Решения

| # | Вопрос | Решение | Причина | Пересмотреть, если |
|---|--------|---------|---------|--------------------|
| 1 | Деплой в Azure | **Пропускаем.** Stage 6 = «Run anywhere»: Dockerfile, compose API + Postgres (Fake по умолчанию), миграции и сидинг вне Development за флагами, `/health`, конфиг через env; опционально — текст «How I'd deploy to Azure» | Даёт то же «запускается не только у меня» без облачных расходов и квоты free trial; Azure-история рассказывается словами (код уже готов к managed identity) | Понадобится живое демо по публичному URL |
| 2 | Две реплики API за nginx (проверка stateless MCP без sticky sessions) | **Отложено** («на потом») | — | Stage 7 или нужно доказать масштабирование |
| 3 | Таблицы | **Делаем в Stage 6:** разрезанная таблица повторяет строку заголовков (+ подпись таблицы рядом с данными), за переключателем, с замером eval-харнессом | Главный известный провал качества (`a-gm-pct-fy25`, holdout `g-dimon-10b5-1`); даёт ещё одно решение с числом | Замер не покажет выигрыша — переключатель остаётся выключенным |
| 4 | Ключ Azure в коммите `0639d78` | **Не перевыпускать** — ключ тестовый (free trial), так решил пользователь | — | Не поднимать этот пункт снова |

## Дальше

Research (Dockerfile/compose для .NET 10 + pgvector, health checks, миграции при старте; как повторять шапку таблицы при разрезе и что делать с подписью) → design → plan → одобрение → реализация.

## После research (2026-10-11)

- Research (`../stage-6/table-splitting-headers-and-captions.md`) поправил диагноз: `a-gm-pct-fy25`, `a-china-fy25`, `a-rnd-fy25` в финальном корпусе Stage 4 — целые таблицы и уже отвечены верно; реальные провалы — продолжения таблиц JPM без шапки/метки группы (`g-cet1-q2-26`), подпись в прозе (`g-dimon-10b5-1`, holdout), сдвиг колонок. Заголовок контекста не попадает в промпт `/api/ask`.
- Пользователь: «делай всё как сказал, всё в этом этапе» → R + D + A′ + B, C отдельным замером, заголовок в промпте отдельным шагом, 3–5 новых вопросов JPM по продолжениям таблиц (одобряет пользователь), E — нет. Замеры eval прогоняет Claude.
- Два отступления от research, принятые пользователем: D делается в чанкере (не в reader'е — `Content` и профиль остаются про чанкинг); новые токены профиля пишутся только при включённом переключателе (текущий корпус остаётся валидным для базы t0).
- Спека — `../stage-6/design.md`.

## Stage 6A (2026-10-12)

- Research — `../stage-6/run-anywhere-containers.md`. Решения пользователя: демо-сидинг сразу четырёх настоящих filing'ов (а не только тенантов); filing'и переложены в `samples/edgar/` (в git), `.research/stage-2/samples/` удалена; документы «Welcome» убрать; Swagger в compose включён.
- Claude выбрал `aspnet:10.0-noble-chiseled-extra` вместо plain chiseled (research советовал plain + `InvariantGlobalization` после проверки): +15 МБ против непроверенного invariant-режима в SemanticKernel/AngleSharp/PdfPig.
- Спека — `../stage-6/design-6a.md`.
