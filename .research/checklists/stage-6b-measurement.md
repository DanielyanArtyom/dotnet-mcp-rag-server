# Stage 6B — чек-лист измерений: лесенка t0…t4 + holdout

Составлен 2026-10-11 после реализации Stage 6B (таблицы). Измерения выполняет Claude (пользователь делегировал 2026-10-11); чек-лист написан так, чтобы его мог повторить кто угодно. Отмечай `[x]` по ходу. Дизайн: `../stage-6/design.md` §6, план: `../stage-6/plan.md`. Итоги записывать в `../discussion/stage-6-hands-on-findings.md`.

Лесенка **кумулятивная**: каждая ступень включает все предыдущие. Сервер: Azure (`gpt-4o` отвечает, `text-embedding-3-small`, судья `gpt-5-mini`), остальное — финал Stage 4 (дефолты `appsettings.json`: `Search:Mode=Vector`, `ContextHeaders`/`ProtectDecimals` on, `ContextTopK=10`, `Temperature=0.2`). Переключатели таблиц по умолчанию **выключены**.

## Как менять настройки

- `dotnet user-secrets set "<Ключ>" "<значение>" --project src/Lumen.Api`, **после каждой смены перезапусти сервер**. User-secrets работают только в `Development`.
- Перед t0 проверь, что остатков экспериментов нет: `dotnet user-secrets list --project src/Lumen.Api | grep -E "^(Search|Chunking|Chat:SourceContextHeader)"` (всегда с фильтром — без него вывод содержит ключи API). Лишнее: `dotnet user-secrets remove "<Ключ>" --project src/Lumen.Api`.
- Любая смена `Chunking:*` меняет профиль ингеста → **удалить и заново загрузить все четыре filing'а** с теми же `title` и `filingDate`, что сейчас (`GET /api/documents` до удаления покажет их): `DELETE /api/documents/{id}`, затем `POST /api/documents/upload`. Названия и файлы (`samples/edgar/`):
  - acme: `Apple 10-K` (`aapl-20250927.html`), `Apple 10-Q` (`aapl-20260627.html`);
  - globex: `Morgan 10-K` (`jpm-20251231.html`), `Morgan 10-Q` (`jpm-20260630.html`).
  Иначе харнесс завершится с кодом 1 (профиль документа не совпадает с `/api/settings`).
- Профили: токены `;cells=v2`, `;thdr=v2`, `;tcap=v2`, `;tser=kv2` дописываются только у включённых переключателей (все выключены → строка как в Stage 4: `chunk=250/60;hdr=v1;merge=0;dec=v1`). Харнесс сам сверяет профиль до первого вопроса и печатает корпус (tenant/title/profile/chunks).
- Прогоны: `cd tools/Lumen.Eval && dotnet run -- run --mode retrieval --label <имя>` и `... --mode full --label <имя>`; сравнение: `dotnet run -- compare runs/<A> runs/<B>` (каталоги в `runs/`). На каждой ступени: два прогона (retrieval + full) и два `compare` (retrieval и full) с указанной в таблице ступенью.
- Eval-set на t0…t4 — **расширенный** (Stage 6B §5: 3–5 новых JPM-вопросов, ответ в середине длинной таблицы; добавляются в `tools/Lumen.Eval/eval-set.json` после одобрения пользователя). Запиши sha256 набора из шапки; со старыми прогонами (`b*`) не сравнивать.

## Лесенка

- [x] **t0. Базовая линия: финал Stage 4 на расширенном eval-set.** Все переключатели таблиц выключены, `Chat:SourceContextHeader` выключен. Re-upload нужен, только если профили в корпусе не `chunk=250/60;hdr=v1;merge=0;dec=v1`. Прогоны `t0-retrieval`, `t0-full`. Сравнивать не с чем.
- [x] **t1. + `TableCells`.** `dotnet user-secrets set "Chunking:TableCells" "true" --project src/Lumen.Api`, перезапуск, **re-upload** (профиль `...;cells=v2`). Прогоны `t1-retrieval`, `t1-full`; `compare` с `t0` (оба режима).
- [x] **t2. + `TableHeaders` + `TableCaptions`.** `dotnet user-secrets set "Chunking:TableHeaders" "true" ...` и `dotnet user-secrets set "Chunking:TableCaptions" "true" ...` (TableHeaders требует TableCells — иначе сервер не стартует), перезапуск, **re-upload** (`...;cells=v2;thdr=v2;tcap=v2`). Прогоны `t2-*`; `compare` с `t1`.
- [x] **t3. + `TableFormat=KeyValue`.** `dotnet user-secrets set "Chunking:TableFormat" "KeyValue" ...` (требует TableCells), перезапуск, **re-upload** (`...;tser=kv2`). Прогоны `t3-*`; `compare` с `t2`.
- [x] **t4. Лучший корпус из t0…t3 + `Chat:SourceContextHeader`.** Оставь только переключатели лучшей ступени (остальные `dotnet user-secrets remove`); **re-upload только если лучшая ступень не t3** (текущий корпус — от t3). `dotnet user-secrets set "Chat:SourceContextHeader" "true" ...`, перезапуск (`promptHash` сменится: off = `ae0bb62cae85`). Прогоны `t4-*`; `compare` с прогоном этого корпуса (retrieval-метрики с ним совпадут — переключатель влияет только на `/api/ask`, смотри `full`).
- [x] **holdout.** Финальная конфигурация, один раз: `dotnet run -- run --mode full --label t-holdout --holdout`. Сравнить с holdout Stage 4 (4/6, 0 ложных ответов; промахи — `g-dimon-10b5-1` и сравнение 10-K/10-Q).

## Что читать, кроме accuracy и flips

- **hit@1** — повторяющийся текст в соседних чанках снижал его на Stage 4 b2;
- **ранг вопросов с якорем в продолжении таблицы** (`a-gm-pct-fy25` и новые JPM-вопросы) — в `results.jsonl` (`sources[].rank`);
- **число чанков и доля токенов таблиц по filing'ам** (корпус в шапке/`run.json`);
- **строка Information-лога после каждого re-upload** (по одной на документ, только при включённом форматировании таблиц): `Table formatting: {FellBack} of {WithData} tables with data rows fell back to compact rows; {Repacked} table chunks exceeded the budget and were repacked.` — записать число fallback'ов (дизайн §2.1: сколько таблиц ушло в compact-рендер из-за рваной сетки) и число repack'ов по каждому filing'у. Ожидаемый repack = 0; если нет — отметить в журнале (значит, префикс заголовка/подписи не влез в бюджет);
- **unlabelable-вопросы** — C меняет текст таблиц, якоря могут перестать находиться; число таких вопросов в шапке и предупреждение `compare` о разных наборах.

Если n мал для значимости (на Stage 4 n=30, p>0.05), пер-вопросные ранги идут в журнал, а в решении честно сказано, что значимости нет.

## После лесенки

- [x] Победившие переключатели → дефолты `appsettings.json` (интеграционный хост держит их выключенными независимо).
- [x] Проигравшие — выключить, причину записать в `../discussion/stage-6-hands-on-findings.md`.
- [x] `DECISIONS.md` — запись только с числом; в `CLAUDE.md` статус обновить цифрами.
