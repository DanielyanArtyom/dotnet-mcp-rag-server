# Stage 4b — чек-лист пользователя: лесенка измерений b0…b5

> **Статус 2026-10-07: этап 4 закрыт.** Итог, цифры и решения — `../discussion/stage-4-hands-on-findings.md` и `DECISIONS.md`.

Составлен 2026-10-06 после реализации Stage 4b. Код готов, все шесть переключателей **выключены по умолчанию**. По правилу проекта эксперименты делаешь ты, результат приносишь обратно. Отмечай `[x]` по ходу.

Контекст: `../stage-4b/design.md`, `../stage-4b/plan.md`, `../discussion/stage-4b-design-decisions.md`, `../discussion/stage-4-hands-on-findings.md` (сюда же записывать выводы).

## Как менять настройки

- Конфиг: `dotnet user-secrets set "<Ключ>" "<значение>" --project src/Lumen.Api` (или `appsettings.json`). **После каждой смены перезапусти сервер.** User-secrets подхватываются только в окружении `Development`; в другом окружении ключи молча игнорируются — используй `appsettings.json` или переменные окружения.
- `Search:*` — достаточно перезапуска. Любая смена `Chunking:*` меняет профиль ингеста → **удалить и заново загрузить все четыре filing'а** (`DELETE /api/documents/{id}`, затем `POST /api/documents/upload`), иначе харнесс завершится с кодом 1 (профиль документа не совпадает с `/api/settings`).
- Профиль ингеста выглядит так: `chunk=250/60;hdr=off;merge=0;dec=off`. Харнесс сам сверяет его до первого вопроса, печатает корпус (tenant/title/profile/chunks) и пишет его в `run.json`.
- Прогоны: `cd tools/Lumen.Eval && dotnet run -- run --mode retrieval --label <имя>` и `... --mode full --label <имя>`. Сравнение: `dotnet run -- compare <runDirA> <runDirB>` (имена каталогов в `runs/`). Eval-set: sha256 `8a02fdeeef19` (прежние E0 были на `158f8425bbbf` — с ними не сравнивать).
- После каждого шага — один прогон на изменение: два прогона (retrieval + full), одно `compare`.

## Лесенка

- [x] **0. Новая базовая линия.** Сначала убедись, что в user-secrets не осталось `Search:*` / `Chunking:*` от прошлых экспериментов: `dotnet user-secrets list --project src/Lumen.Api | grep -E "^(Search|Chunking):"` (grep печатает только эти ключи; сам по себе `dotnet user-secrets list` выводит и API-ключи — не запускай его без фильтра и не показывай вывод). Лишнее удали через `dotnet user-secrets remove`. Все настройки по умолчанию. Заново загрузить все четыре filing'а (проставит профили). Затем:
  - `dotnet run -- run --mode retrieval --label b0-retrieval`
  - `dotnet run -- run --mode full --label b0-full`
  - Проверь в шапке, что профиль `chunk=250/60;hdr=off;merge=0;dec=off` у всех документов и sha256 eval-сета `8a02fdeeef19`.
- [x] **1. Гибридный поиск.** `dotnet user-secrets set "Search:Mode" "Hybrid" --project src/Lumen.Api`, перезапуск; re-upload **не нужен**. Прогоны `b1-retrieval`, `b1-full`. Сравнить: `compare <b0-full> <b1-full>` и `compare <b0-retrieval> <b1-retrieval>`. В `results.jsonl` смотри `sources[].score` (RRF) vs `distance`.
- [x] **2. Контекстный заголовок.** `dotnet user-secrets set "Chunking:ContextHeaders" "true" ...`, перезапуск, **re-upload всех filing'ов** (профиль станет `hdr=on`; на Apple 10-K ожидается ~405 чанков вместо 296). Прогоны `b2-retrieval`, `b2-full`; `compare` с `b1` (и retrieval, и full). Помни: этот шаг меряет заголовок **вместе** с более мелкой нарезкой (проза режется на каждом заголовке раздела).
- [x] **3. Склейка коротких чанков.** `dotnet user-secrets set "Chunking:MergeBelowTokens" "20" ...`, перезапуск, **re-upload**. Прогоны `b3-*`; `compare` с `b2`.
- [x] **4. Целые дробные числа.** `dotnet user-secrets set "Chunking:ProtectDecimals" "true" ...`, перезапуск, **re-upload**. Прогоны `b4-*`; `compare` с `b3`.
- [x] **5. Не больше 3 чанков из документа.** `dotnet user-secrets set "Search:MaxChunksPerDocument" "3" ...`, перезапуск; re-upload **не нужен**. Прогоны `b5-*`; `compare` с `b4`. Ограничение мягкое: у тенанта с одним документом всё равно вернётся topK чанков.
- [ ] **6. Откат неудачных шагов.** Если шаг ухудшил результат — выключи его обратно (`dotnet user-secrets remove "<Ключ>" ...`, для `Chunking:*` ещё и re-upload), запиши причину в `../discussion/stage-4-hands-on-findings.md`. Следующий `compare` делай **с последней оставленной ступенью**, а не с неудачной.
- [ ] **7. После лесенки.** E1-стиль разбор оставшихся провалов: теперь причины читаются из `results.jsonl` (`sources`: rank, chunkId, distance, score, превью 200 символов). Решить, какие записи идут в `DECISIONS.md` (только подтверждённые цифрами; гибрид — что дал в точности, заголовки/склейка/дроби — что дал каждый).
