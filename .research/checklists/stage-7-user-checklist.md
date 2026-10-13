# Stage 7 — чек-лист пользователя (2026-10-12)

Медиа снял Claude; от тебя только приёмка.

- [ ] **GIF** (`docs/media/mcp-demo.gif`, 38 с, 762 КБ): открыть README на GitHub после push — грузится ли, читается ли текст, не слишком ли быстро. Если переснять: `tools/media/demo-server.sh` (в фоне) → `DEMO=~/lumen-demo vhs tools/media/mcp-demo.tape`. Скорость — `Set PlaybackSpeed` в tape, паузы — `Sleep`.
- [ ] **Скриншот Swagger** (`docs/media/swagger.png`): все эндпоинты, `POST /api/ask` с ответом 22,429. Пересъёмка: демо-сервер запущен → `node tools/media/swagger-screenshot.mjs`.
- [ ] **README сверху вниз глазами рекрутера**: GIF → Results (35 вопросов, 80 %) → What's interesting → диаграммы. Всё ли понятно за 1–2 минуты.
- [ ] **DECISIONS.md → Index**: кликнуть 2–3 ссылки на GitHub (якоря проверены скриптом, но рендер GitHub — финальная проверка).
- [ ] **`Jwt:Secret` в `appsettings.json`** — решить открытый вопрос из roadmap (рекомендация: откатить строку, `JWT_SECRET` положить в `.env`). До решения `appsettings.json` не коммитить.
- [ ] **Уборка (по желанию)**: `drop database lumen_demo`, `rm -r ~/lumen-demo`; в `.env` проставить `JWT_SECRET`, чтобы compose-API перестал падать.
- [ ] **Коммит** — вручную: `README.md`, `DECISIONS.md`, `CLAUDE.md`, `.gitignore`, `docs/media/`, `tools/media/` (без `node_modules`).
