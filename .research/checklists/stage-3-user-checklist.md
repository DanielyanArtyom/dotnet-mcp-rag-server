# Stage 3 — чек-лист пользователя (что проверить руками)

Составлен 2026-09-23 после реализации Stage 3. По правилу проекта (см. `CLAUDE.md`, «Working with the user») эксперименты и наблюдения делает пользователь, результат приносит обратно. Отмечай `[x]` по ходу.

Контекст: `../stage-3/design.md` / `../stage-3/plan.md` (что и почему построено), `../discussion/stage-3-design-decisions.md` (журнал решений), `stage-2-checklist-after-stage-3.md` (три пункта Stage 2, отложенные именно до этого момента, теперь лежит в этой же папке).

Финальная сборка перед началом чек-листа: `dotnet build` — 0 warnings, 0 errors; `dotnet test` — 137/137 (94 unit, 43 integration); `grep -rn "IgnoreQueryFilters" src/` — ровно один хит (`UserLookupService.cs`, не изменился).

## 1. Fake-провайдер: пересоздать базу, залить filing, искать

- [x] Пересоздать локальную базу (миграция `AddChunkEmbeddings` не может добавить `NOT NULL`-колонку на непустую таблицу): `docker compose down -v && docker compose up -d`. Первый запуск потянет `pgvector/pgvector:pg17` вместо старого `postgres:17-alpine`.
- [x] `dotnet run --project src/Lumen.Api`, миграции применяются сами (Development). `appsettings.json` по умолчанию `Embeddings:Provider = Fake` — Azure не нужен для этого пункта.
- [x] Логин `admin@acme.test` / `Passw0rd!` → Authorize в Swagger.
- [x] `POST /api/documents/upload` с тем же Apple 10-K, что и в Stage 2 (`samples/edgar/aapl-20250927.html`) → 201, `chunkCount` должен совпасть со Stage 2 — **296**. Если не совпадёт, это регрессия в чанкере, не в эмбеддингах — стоит остановиться и разобраться, а не списывать на новый провайдер.
- [ ] `POST /api/search` с **полным текстом одного чанка** — скопировать поле `text` целиком из `GET /api/documents/{id}/chunks` (не фразу из filing'а!). Fake хэширует строку целиком (SHA-256), поэтому совпадает только байт-в-байт идентичная строка: этот чанк обязан оказаться первым с `distance` ≈ 0, у всего остального `distance` ≈ 1.0 (случайные векторы в 1536 измерениях почти ортогональны), порядок — шум.
  - *Исправлено 2026-09-27:* изначально здесь было «предложение из середины абзаца» — это была ошибка в чек-листе, не в коде: подстрока чанка — другая строка → другой, по сути случайный вектор. Сюда же относится находка ниже (поиск по «Company’s best estimates…» вернул не тот чанк) — ожидаемое поведение fake. Ещё мелочь: в filing'е типографский апостроф `’`, а не `'` — для fake это тоже разные байты.
- [x] Тот же запрос с `documentId`, указывающим на другой (пока не загруженный) документ → пустой список, а не 400/404.

## 2. Включить Azure

- [x] Создать Azure OpenAI resource и deployment модели `text-embedding-3-small` (портал/CLI — по инструкции из `.research/stage-3/embeddings-dotnet-and-azure-openai.md`).
- [x] Выставить четыре значения (`appsettings.json` или переменные окружения — не коммитить ключ):
  - `Embeddings:Provider=AzureOpenAI`
  - `Embeddings:Endpoint=https://<resource>.openai.azure.com/openai/v1/`
  - `Embeddings:Deployment=<имя deployment>`
  - `Embeddings:ApiKey=<ключ>` — или оставить пустым и завести managed identity / `az login` для `DefaultAzureCredential`.
- [x] Пересоздать базу (`docker compose down -v && docker compose up -d`) и перезалить оба filing'а (Acme/Apple и Globex/JPMorgan — под соответствующими тенантами). Это не опционально: `Fake` и Azure дают векторы разной природы, и `EmbeddingModel`-фильтр в поиске существует именно для того, чтобы они не смешались в одной выдаче — но чище пересоздать таблицу, чем держать там мёртвые fake-строки.
- [x] Повторить те же поисковые запросы, что в пункте 1, и несколько семантических (перефразированный вопрос, не точная цитата). Ожидаемая разница: с fake — только точное совпадение в топе, с Azure — результаты должны быть смыслово релевантны, не только буквально совпадающими.
- [x] Первый живой вызов деплоя стоит сделать этим же тестом — в исследовании не подтверждено построчно, что `/openai/v1/embeddings` покрыт GA-подмножеством v1 API; если вызов упадёт с ошибкой конфигурации/версии, это первое, что нужно проверить.

## 3. Замеры (то, что идёт в DECISIONS.md)

- [x] Время upload с эмбеддингом (fake и/или Azure) на том же Apple 10-K — сравнить со Stage 2's 0.75 с без эмбеддинга. Записать оба числа и разницу.
- [x] Латентность `POST /api/search` — несколько запросов, разброс.
- [x] `SELECT count(*) FROM "DocumentChunks";` после каждой перезаливки — должно совпадать с суммой `chunkCount` из upload-ответов.

## 4. Три отложенных пункта Stage 2 (теперь актуальны)

Полный текст — `stage-2-checklist-after-stage-3.md` (в этой же папке). Кратко:

- [x] **Параметры чанкинга.** `Chunking:MaxTokensPerChunk`/`OverlapTokens` 250/60 → 400/100 → 150/40, тот же файл, но теперь сравнивать не только количество чанков — сравнивать качество поиска: та же фраза/тот же смысловой вопрос, разные настройки, какая версия чаще ставит нужный чанк в топ-k.
- [x] **Реальные PDF.** Загрузить настоящий PDF с таблицей (не синтетику) — что стало `Table`, есть ли ложные срабатывания на прозе; известная слабость эвристики (разрыв > 1.5 высоты слова, 3+ ячейки = строка таблицы) описана там же.
- [x] **Delete / имена файлов / ячейки таблиц / 403 vs 404.** Четыре пункта из старого раздела 10 — включая новое замечание: после `DELETE /api/documents/{id}` должны исчезать и его эмбеддинги (строка `DocumentChunks` целиком, тем же каскадом, что и раньше — но раньше это была проза/таблицы без векторов, теперь стоит явно посмотреть в БД, что `vector`-колонка тоже ушла, а не осталась висеть отдельно).

## 5. Что записать в DECISIONS.md

Только то, что подтверждено цифрой — не наблюдение «показалось лучше». Кандидаты по итогам этого чек-листа:
- [x] Upload latency до/после эмбеддинга (пункт 3) — если провайдер оказался заметным вкладом в latency, это решение стоит зафиксировать (например, почему upload остаётся синхронным, а не переезжает в очередь).
- [x] Любое расхождение между `Fake` и Azure в качестве ранжирования на одном и том же вопросе (пункт 2) — конкретный пример «fake поставил X первым, Azure поставил Y первым, Y правильнее, потому что …».
- [x] Итог по параметрам чанкинга (пункт 4) — если один из вариантов 250/60 / 400/100 / 150/40 заметно выигрывает по попаданию в топ-k, это и есть решение, которое стоило Stage 2 оставить неподкреплённым до появления реального поиска.


### Researched 
#### Chunking:MaxTokensPerChunk`/`OverlapTokens == 250/60
  - первый upload занял несколько секунда пока закончиться, файл был jpmorgan, а последующие были достаточно быстрые
  - при точном поиске получал первим же тот чанк который нужен, при смысловой тоже находиль его певрым
   -- например The Company uses hedging to reduce, but not fully eliminate, the financial risks from changes in exchange rates and interest rates -> нашел это
    "The Company may use derivative instruments to partially offset its business exposure to foreign exchange and interest rate risk. However, the Company may choose not to hedge certain exposures for a variety of reasons, including accounting considerations or the prohibitive economic cost of hedging particular exposures.\nThere can be no assurance the hedges will offset more than a portion of the financial impact resulting from movements in foreign exchange or interest rates.\nForeign Exchange Rate Risk\nTo protect gross margins from fluctuations in foreign exchange rates, the Company may use forwards, options or other instruments, and may designate these instruments as cash flow hedges. The Company generally hedges portions of its forecasted foreign currency exposure associated with revenue and inventory purchases, typically for up to 12 months.\nTo protect the Company’s foreign currency–denominated term debt or marketable securities from fluctuations in foreign exchange rates, the Company may use forwards, cross-currency swaps or other instruments. The Company designates these instruments as either cash flow or fair value hedges. As of June 27, 2026, the maximum length of time over which the Company is hedging its exposure to the variability in future cash flows for term debt–related foreign currency transactions is 16 years.\n""
  - при fake нужно было четко написать весь чанк что бы оно находило, в противном случай результат не совподал с ожиданием.

  #### Chunking:MaxTokensPerChunk`/`OverlapTokens == 400/100

  - Apple 10-k , получил "chunkCount": 186 загрузка была так же несколько секуд 3-4 макс
  - Apple 10-q "chunkCount": 93 загрузка была  быстрой около 1 секунды
  - JpMorgan 10-k , получил  "chunkCount": 1560 загрузка была долгой около 7 секунд
  - JpMorgan 10-q , получил  "chunkCount": 756 загрузка была долгой около 3-4 секунд

##### поиск в jpm 10-q
- Fully taxable-equivalent adjustments -> получил ожидаемвый результат в виде таблице мд
""2026 | 2025\n(in millions, except ratios) | Reported | Fully taxable-equivalent adjustments(a) | Managed basis | Reported | Fully taxable-equivalent adjustments(a) | Managed basis\nOther income | $ | 9,220 | $ | 1,151 | $ | 10,371 | $ | 3,077 | $ | 1,265 | $ | 4,342\nTotal noninterest revenue | 56,306 | 1,151 | 57,457 | 43,740 | 1,265 | 45,005\nNet interest income | 50,877 | 224 | 51,101 | 46,482 | 207 | 46,689\nTotal net revenue | 107,183 | 1,375 | 108,558 | 90,222 | 1,472 | 91,694\nTotal noninterest expense | 54,166 | NA | 54,166 | 47,376 | NA | 47,376\nPre-provision profit | 53,017 | 1,375 | 54,392 | 42,846 | 1,472 | 44,318\nProvision for credit losses | 5,022 | NA | 5,022 | 6,154 | NA | 6,154\nIncome before income tax expense | 47,995 | 1,375 | 49,370 | 36,692 | 1,472 | 38,164\nIncome tax expense | 10,346 | 1,375 | 11,721 | 7,062 | 1,472 | 8,534\nNet income | $ | 37,649 | NA | $ | 37,649 | $ | 29,630 | NA | $ | 29,630","
поиск был быстрим
- посел удаление получил 200 с пустим массиовм то что и ожидалось!

### итог
отпимальний чанкнинг это 250 на 60.
иногда azure долго грузиться но это из за струткури файлов так как в одном кейсе было 3 секунды в другой 7 при этом разрывм чанков достаточно огромен!. пока оставляем azure