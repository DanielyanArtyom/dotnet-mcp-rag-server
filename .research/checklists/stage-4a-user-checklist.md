# Stage 4a — чек-лист пользователя (что проверить руками)

> **Статус 2026-10-07: этап 4 закрыт.** Итог, цифры и решения — `../discussion/stage-4-hands-on-findings.md` и `DECISIONS.md`.

Составлен 2026-10-03 после реализации Stage 4a, **обновлён 2026-10-05 после baseline E0**. По правилу проекта (см. `CLAUDE.md`, «Working with the user») эксперименты и наблюдения делает пользователь, результат приносит обратно. Отмечай `[x]` по ходу.

Контекст: `../stage-4/design.md` / `../stage-4/plan.md` (что и почему построено), `../discussion/stage-4-design-decisions.md` (журнал решений), `../discussion/stage-4-hands-on-findings.md` (все цифры прогонов и наблюдения), `../discussion/stage-4-incident-mixed-chunking.md` (почему первый E0 пришлось перезапускать).

**Что такое E0–E7** — номера экспериментов этапа 4, план согласован на старте (`../discussion/stage-4-kickoff.md`). Каждый эксперимент меняет **одну** вещь и сравнивается с предыдущим через `compare`:

| | что | меняем |
|---|---|---|
| **E0** | baseline — замер «как есть» | ничего; точка отсчёта ✅ |
| **E1** | разбор провалов E0 | ничего не меняем — смотрим *почему* ошибся каждый вопрос |
| E2 | гибридный поиск (FTS + RRF) | способ поиска |
| E3 | размер чанков 250/60 vs 400/100 vs 150/40 | чанкинг |
| E4 | сколько чанков давать модели (3 / 5 / 10) | `Chat:ContextTopK` |
| E5 | temperature, стабильность (×3 повтора) | `Chat:Temperature` |
| E6 | промпт и отказы | текст промпта |
| E7 | HNSW-индекс (опционально) | индекс |

Порядок E2–E7 не жёсткий: что делать первым, решает раскладка E1 (это и есть дизайн 4b).

---

## Часть 1. Сделано

- [x] 1. Два chat-deployment'а: ответы — `gpt-4o` (Standard, Sweden Central, 50K TPM), судья — `gpt-5-mini` (Global Standard). Новые модели недоступны из-за квоты free trial — см. `DECISIONS.md` и `stage-4-hands-on-findings.md`.
- [x] 2. `Chat:*` прописан в своём `appsettings.Development.json`, `Judge:*` настроен.
- [x] 3. Проба temperature: `gpt-4o` с `temperature: 0.2` → **200**, ответ верный. E5 будет варьировать temperature.
- [x] 4. Eval-сет: из 47 кандидатов отобрано 36 (30 рабочих + 6 holdout) → `tools/Lumen.Eval/eval-set.json`. После правки якорей sha256 = `158f8425bbbf`.
- [x] 5. `GET /api/settings`: модель, параметры и `promptHash` видны (они же в шапке каждого прогона).
- [x] 6. E0 baseline: `runs/20261005-210334_e0-retrieval` и `runs/20261005-210426_e0-full`. hit@5 71 %, MRR 0.59, accuracy 67 %, false answers 0 %, false refusals 38 %. Первый, «смешанный» E0 (`…204443`, `…204525`) — недействителен, оставлен как запись инцидента.

---

## Часть 2. Порядок наведения (сделать до E1, ~15 минут)

- [x] **2.1. Ключи не должны попасть в git.** *Сделано 2026-10-06:* все Azure-настройки API (provider/endpoint/deployment/ключ для `Embeddings` и `Chat`) перенесены в user-secrets (`UserSecretsId` в `Lumen.Api.csproj`), `appsettings.Development.json` возвращён к закоммиченной версии без секретов (остаётся в git — без него проект не запустится после клона: там connection string и dev JWT secret); ключ судьи — в `tools/Lumen.Eval/appsettings.Local.json`. **Ключ ресурса уже есть в истории — коммит `0639d78`** → перевыпустить (Regenerate) до перехода на Pay-As-You-Go. Было: Непустой `ApiKey` лежал в двух отслеживаемых файлах: `src/Lumen.Api/appsettings.Development.json` (Embeddings + Chat) и `tools/Lumen.Eval/appsettings.json` (Judge).
  - Ключ судьи → в `tools/Lumen.Eval/appsettings.Local.json` (уже в `.gitignore`), в `appsettings.json` оставить `"ApiKey": ""`.
  - Ключи API → user-secrets (Claude добавит `UserSecretsId` в `Lumen.Api.csproj` по твоей просьбе) или переменные окружения `Chat__ApiKey` / `Embeddings__ApiKey`.
  - Проверка: `git log -p -S "ApiKey" -- src tools` — в истории не должно быть ни одного настоящего ключа. Если уже закоммичен/запушен — **перевыпустить ключ** в Azure (Keys and Endpoint → Regenerate); удаление следующим коммитом его из истории не убирает.
  - Ключ эмбеддингов перевыпустить в любом случае: он засветился в логе субагента при реализации (Task 1).
- [x] **2.2. Цены для отчёта о стоимости.** В `tools/Lumen.Eval/appsettings.json` → `Prices`: `"gpt-4o": { "InputPerMillion": …, "OutputPerMillion": … }` — цены сверить на странице Azure. Без этого в отчёте `cost n/a`. (Ориентир: E0 full ≈ 36K входных токенов.)
- [x] **2.3. Решить судьбу `tools/Lumen.Eval/runs/`.** Коммитить как доказательство к будущим цифрам в DECISIONS.md — или добавить в `.gitignore`. Сейчас папки нет ни там, ни там.
- [x] **2.4. Ответить Claude на два открытых вопроса:**
  - поправить якоря `g-cet1-delta` (miss✓ — верный ответ 0.4 из квартальной строки `capital ratio – Standardized | 14.2 | 14.3 | 14.6 …`, которую якоря не покрывают)? Это снова поменяет хэш eval-сета → baseline придётся перезапустить (≈ $0.1);
  - добавить в `results.jsonl` список найденных источников (id чанка, rank, первые ~200 символов), чтобы E1 разбирать по файлу прогона, а не повторять запросы в Swagger?

  Обе правки разумно сделать **вместе и до E1**, потом один раз перезапустить baseline.

---

## Часть 3. Пункт 7 — калибровка судьи

Судья (`gpt-5-mini`) оценивает только вопросы с текстовым эталоном. В baseline он реально выносил вердикт дважды (остальные текстовые вопросы закончились отказом модели — их судья не видит, это автоматически ✗):

| id | вердикт судьи | что проверить |
|---|---|---|
| `g-apple-card-what` | ✓ | согласен ли ты, что ответ верный |
| `g-nir-why-q2-26` | ✗ «назвал только Visa-гейн и $1.0B equity gains» | эталон перечисляет 5 причин, но ключевые — две первые. Ты бы засчитал? |

- [x] 7.1. Открой `runs/20261005-210426_e0-full/results.jsonl`, найди эти два id, прочитай `answer` и сравни с эталоном в `eval-set.json`.
- [x] 7.2. Для каждого реши сам: верно / неверно. Запиши.
- [x] 7.3. Если ты не согласен с судьёй хотя бы в одном случае — это сигнал: либо эталон сформулирован слишком подробно (тогда сократить эталон до ключевого факта), либо промпт судьи слишком строг (тогда Claude правит `JudgePromptConstants`). Принеси своё мнение — решим вместе.
- [x] 7.4. Двух вердиктов для настоящей калибровки мало. Вернуться к пункту, когда после 4b модель начнёт отвечать на больше текстовых вопросов (их в рабочем наборе 5: `a-siri-ai`, `a-dividend-change`, `g-apple-card-what`, `g-expense-why`, `g-nir-why-q2-26`).


### мои решениея
- g-apple-card-what -> похоже да он прав в этом! 
- g-nir-why-q2-26 -> тут он вроде тоже прав! 
давай ты еще раз по файлам пройдешься и напишешь еще свое мненеия и я подумаю.

### мнение Claude (2026-10-06)
- **`g-apple-card-what` — согласен, верно.** Ответ содержит всё из эталона (новый эмитент, forward purchase commitment 30.12.2025, закрытие ~24 мес.) плюс верные лишние детали ($104B exposure, $23B drawn) — по правилу судьи лишнее верное не штрафуется.
- **`g-nir-why-q2-26` — я бы засчитал, но решать тебе.** Ответ модели: «predominantly due to a $4.6 billion net gain related to Visa shares and $1.0 billion of gains on certain equity investments». Слово *predominantly* стоит и в самом filing'е — главная причина названа верно. Судья счёл ответ неполным, потому что **мой эталон перечисляет все 5 причин** — и тогда «пропущен ключевой факт» срабатывает на любом сокращении. По-моему, проблема в эталоне, а не в судье: для вопроса «почему» эталон должен содержать ключевой факт, а не весь список. Предложение: сократить эталон до «Predominantly the $4.6 billion net gain related to Visa shares (plus gains on equity investments and higher Markets revenue)». Промпт судьи не трогаем.
- Если согласишься — правлю эталон вместе с якорем `g-cet1-delta` (одна смена хэша, один перезапуск baseline).
---

## Часть 4. Пункт 8 — E1: разбор провалов

**Цель:** для каждого из 10 неверных ответов понять *почему*, и свести в раскладку «сколько провалов от какой причины». Эта раскладка определит, что идёт в 4b первым.

**Как смотреть, что видела модель:** Swagger → залогиниться под тенантом вопроса (`admin@acme.test` для `a-*`, `admin@globex.test` для `g-*`, пароль `Passw0rd!`) → `POST /api/ask` с тем же текстом вопроса (он в `eval-set.json`) → смотреть `sources[].text`, `distance`, `cited`. Для «промахов поиска» дополнительно `POST /api/search` с `topK: 20` — на каком месте нужный чанк и что стоит выше него.

### 4.1. Промахи поиска (miss✗ — нужного чанка нет в top-5, модель отказала)

| id | вопрос (кратко) | где ответ | гипотеза Claude |
|---|---|---|---|
| `a-china-fy25` | net sales Greater China FY25 | 10-K, сегментная таблица | табличный чанк без подписи «net sales by reportable segment» — одни цифры |
| `a-gm-pct-fy25` | total gross margin % FY25 | 10-K, таблица Gross Margin | вторая половина разрезанной таблицы: **нет строки `2025 | 2024 | 2023` и подписи** |
| `a-rnd-fy25` | R&D FY25 | 10-K, таблица Operating Expenses | вероятно, та же история: подпись таблицы в соседнем прозаическом чанке |
| `a-buyback-q3fy26` | buyback Q3 FY26 | 10-Q, абзац Capital Return Program | возможно, выше стоит такой же абзац из 10-K ($89.3B) — проверить, что выше |
| `g-apple-card-allowance` | Apple Card → allowance 2025 | 10-K, Executive Overview и др. | в retrieval-прогоне на 6-м месте — чуть-чуть не влез в top-5; проверить, что занимает 1–5 |
| `g-expense-why` | почему вырос noninterest expense | 10-K, Firmwide overview | 2 200 чанков, «noninterest expense» есть почти в каждом сегменте — тонет среди похожих |

- [x] 4.1.1. По каждому: на каком месте нужный чанк в `/api/search` (topK 20) и что стоит выше.
- [x] 4.1.2. По каждому записать причину одним словом: «таблица без подписи», «таблица без заголовков колонок», «похожий чанк из другого filing'а», «тонет среди однотипных», «другое».


### мои иследования
#### a-china-fy25

очень странно но для apple 10-k я получил такой ответ "he provided documents do not contain the answer to this question." хотя да source-ов у него много было!

кажеться ответ был тут "chunkIndex": 177, - в sources он был 3-им.

Причина скорее всего в том что оно контекст правильно не поняла либо вопроса либо соурса!

#### a-gm-pct-fy25

#### a-rnd-fy25
тут тоже answer выдал что нет в доке но при этом в соурасх ответ был первим под индексом  "chunkIndex": 131,

#### a-buyback-q3fy26
тут вообще в сорусах не было ничего про repurchased $25.8 billion как в 10-q так и 10-k

#### мнение Claude по 4.1 (проверил чанки в базе, только SELECT)
Во всех трёх случаях **модель отказала правильно** — нужного числа в её контексте не было. Это промахи поиска, а не ошибки модели:
- **`a-china-fy25`:** чанк 177, который ты нашёл, — это *проза*: «…except in Greater China, where iPhone revenue represented a moderately higher proportion of net sales». Слова «Greater China» есть, а числа 64,377 нет. Ответ лежит в табличном чанке 122 (сегментная таблица: «Americas | $ | 178,353 … Greater China | 64,377 …»), и он в top-5 не попал. Проза с теми же словами обогнала таблицу с числом.
- **`a-rnd-fy25`:** та же история. Чанк 131 на 1-м месте — проза «The growth in R&D expense during 2025… was primarily driven by headcount…» — это *причины*, без суммы. Сумма $34,550 — в соседнем табличном чанке 130 («Research and development | $ | 34,550 | 10 | % …»), он не найден.
- **`a-buyback-q3fy26`:** вероятно, вперёд вышли чанки 10-K про байбэк ($89.3B, программа на $100B) — они семантически про то же, но за другой период. 10-Q проигрывает 10-K (см. ниже — та же картина в `a-siri-ai` и `a-dividend-change`).

**Обобщение:** для вопроса «сколько» векторный поиск предпочитает прозу, которая *говорит о* метрике, таблице, которая *содержит* число. В табличном чанке почти нет слов — только подписи строк и цифры, а подпись таблицы («Operating expenses for 2025, 2024 and 2023…») уехала в соседний прозаический чанк.

### 4.2. Нашли, но ответили неверно (hit✗)

| id | rank нужного чанка | что случилось | гипотеза Claude |
|---|---|---|---|
| `g-cet1-2025` | 1 | отказ | найденная строка таблицы без заголовков колонок — модель не может понять, какой столбец = 31.12.2025, и честно отказывается |
| `a-siri-ai` | 4 | отказ | чанк с «introduced Siri AI» найден; возможно, модель не сочла «Siri AI» ответом на «Which AI product» — слишком строгий промпт? |
| `a-dividend-change` | 1 | отказ | сравнение: найден только один из двух нужных чанков (10-K или 10-Q) — без второго ответить нельзя, отказ ожидаем |
| `g-nir-why-q2-26` | 1 | судья ✗ | ответ неполный (2 причины из 5) — см. часть 3 |

- [ ] 4.2.1. По каждому: открыть `sources[].text` и решить — модель была права, что отказала (данных в чанке реально недостаточно), или ошиблась (ответ был в чанке)?
- [ ] 4.2.2. Если «ответ был, а модель отказала» хотя бы в 2 случаях — это аргумент смягчить промпт; если нет — промпт оставляем, чиним данные.

### мои иследования
#### a-siri-ai
тут проблема в том что во всех sources нету ничего про AI

"sources": [
    {
      "sourceId": 1,
      "chunkId": "aa794889-a159-4d46-8109-8db372686d85",
      "documentId": "2fd16aae-b65f-49a2-bb15-d6be65038a6f",
      "documentTitle": "Apple 10-k",
      "chunkIndex": 118,
      "chunkType": "Prose",
      "tokenCount": 195,
      "distance": 0.3691190057652738,
      "cited": false,
      "text": "The Company announces new product, service and software offerings at various times during the year. Significant announcements during fiscal year 2025 included the following:\nFirst Quarter 2025:\n•MacBook Pro\n•Mac mini\n•iMac\n•iPad mini\nSecond Quarter 2025:\n•iPhone 16e\n•iPad Air\n•iPad\n•MacBook Air\n•Mac Studio\nThird Quarter 2025:\n•iOS 26, macOS Tahoe 26, iPadOS 26, watchOS 26, visionOS 26 and tvOS 26\nFourth Quarter 2025:\n•iPhone 17, iPhone Air, iPhone 17 Pro and iPhone 17 Pro Max\n•Apple Watch Series 11, Apple Watch SE 3 and Apple Watch Ultra 3\n•AirPods Pro 3\nFiscal Period The Company’s fiscal year is the 52- or 53-week period that ends on the last Saturday of September."
    },
    {
      "sourceId": 2,
      "chunkId": "1e0b4fe1-f3c4-4d90-b402-5b832f9fa151",
      "documentId": "2fd16aae-b65f-49a2-bb15-d6be65038a6f",
      "documentTitle": "Apple 10-k",
      "chunkIndex": 14,
      "chunkType": "Prose",
      "tokenCount": 192,
      "distance": 0.43347927505248185,
      "cited": false,
      "text": "Unless otherwise stated, all information presented herein is based on the Company’s fiscal calendar, and references to particular years, quarters, months or periods refer to the Company’s fiscal years ended in September and the associated quarters, months and periods of those fiscal years.\nEach of the terms the “Company” and “Apple” as used herein refers collectively to Apple Inc. and its wholly owned subsidiaries, unless otherwise stated.\nPART I\nItem 1. Business\nCompany Background\nThe Company designs, manufactures and markets smartphones, personal computers, tablets, wearables and accessories, and sells a variety of related services. The Company’s fiscal year is the 52- or 53-week period that ends on the last Saturday of September.\nProducts\niPhone iPhone® is the Company’s line of smartphones based on its iOS operating system. The iPhone line includes iPhone 17 Pro, iPhone Air™, iPhone 17, iPhone 16 and iPhone 16e.\nMac\n"
    },
    {
      "sourceId": 3,
      "chunkId": "f9d2fd00-cf13-4996-b436-0a591e987039",
      "documentId": "2fd16aae-b65f-49a2-bb15-d6be65038a6f",
      "documentTitle": "Apple 10-k",
      "chunkIndex": 117,
      "chunkType": "Prose",
      "tokenCount": 206,
      "distance": 0.4428507984646243,
      "cited": false,
      "text": "Item 6. [Reserved]\nApple Inc. | 2025 Form 10-K | 20\nItem 7. Management’s Discussion and Analysis of Financial Condition and Results of Operations\nThe following discussion should be read in conjunction with the consolidated financial statements and accompanying notes included in Part II, Item 8 of this Form 10-K. This Item generally discusses 2025 and 2024 items and year-to-year comparisons between 2025 and 2024.\nDiscussions of 2023 items and year-to-year comparisons between 2024 and 2023 are not included, and can be found in “Management’s Discussion and Analysis of Financial Condition and Results of Operations” in Part II,\nItem 7 of the Company’s Annual Report on Form 10-K for the fiscal year ended September 28, 2024.\nProduct, Service and Software Announcements The Company announces new product, service and software offerings at various times during the year. Significant announcements during fiscal year 2025 included the following:\n"
    },
    {
      "sourceId": 4,
      "chunkId": "bba6dc85-48de-40f6-9ad8-4b9e36974f6f",
      "documentId": "2fd16aae-b65f-49a2-bb15-d6be65038a6f",
      "documentTitle": "Apple 10-k",
      "chunkIndex": 288,
      "chunkType": "Prose",
      "tokenCount": 14,
      "distance": 0.4493411431367561,
      "cited": false,
      "text": "Apple Inc. | 2025 Form 10-K | 56"
    },
    {
      "sourceId": 5,
      "chunkId": "e2858f9e-5ee2-4d84-8d13-1cec43d9d2c0",
      "documentId": "2fd16aae-b65f-49a2-bb15-d6be65038a6f",
      "documentTitle": "Apple 10-k",
      "chunkIndex": 295,
      "chunkType": "Prose",
      "tokenCount": 14,
      "distance": 0.45530882363108804,
      "cited": false,
      "text": "Apple Inc. | 2025 Form 10-K | 58"
    }
  ],

  вот сам смотри!


#### a-dividend-change
тут я тоже до конца не понял в чемс проблема

"sources": [
    {
      "sourceId": 1,
      "chunkId": "8a70b2a3-52b9-4ce6-b40a-0218a4b2ed4f",
      "documentId": "2fd16aae-b65f-49a2-bb15-d6be65038a6f",
      "documentTitle": "Apple 10-k",
      "chunkIndex": 137,
      "chunkType": "Prose",
      "tokenCount": 228,
      "distance": 0.3562619845273439,
      "cited": false,
      "text": "As of September 27, 2025, the balance of the deemed repatriation tax payable imposed by the U.S. Tax Cuts and Jobs Act of 2017 (“TCJA”) was $8.8 billion, which was payable within 12 months.\nApple Inc. | 2025 Form 10-K | 25\nCapital Return Program\nIn addition to its contractual cash requirements, the Company has an authorized share repurchase program. The program does not obligate the Company to acquire a minimum amount of shares.\nAs of September 27, 2025, the Company’s quarterly cash dividend was $0.26 per share. The Company intends to increase its dividend on an annual basis, subject to declaration by the Board.\nIn May 2025, the Company announced a new share repurchase program of up to $100 billion and raised its quarterly dividend from $0.25 to $0. 26 per share beginning in May 2025. During 2025, the Company repurchased $89.3 billion of its common stock and paid dividends and dividend equivalents of $15.4 billion.\n"
    },
    {
      "sourceId": 2,
      "chunkId": "4c1e7ead-63a9-431e-9e21-48c4aa9b077b",
      "documentId": "2fd16aae-b65f-49a2-bb15-d6be65038a6f",
      "documentTitle": "Apple 10-k",
      "chunkIndex": 224,
      "chunkType": "Prose",
      "tokenCount": 188,
      "distance": 0.36720780800899977,
      "cited": false,
      "text": "As of September 27, 2025 and September 28, 2024, the fair value of the Company’s Notes, based on Level 2 inputs, was $80.4 billion and $88.4 billion, respectively.\nNote 10 – Shareholders’ Equity\nShare Repurchase Program\nDuring 2025, the Company repurchased 402 million shares of its common stock for $89.3 billion. The Company’s share repurchase programs do not obligate the Company to acquire a minimum amount of shares.\nUnder the programs, shares may be repurchased in privately negotiated or open market transactions, including under plans complying with Rule 10b5-1 under the Exchange Act.\nApple Inc. | 2025 Form 10-K | 44\nShares of Common Stock The following table shows the changes in shares of common stock for 2025, 2024 and 2023 (in thousands):"
    },
    {
      "sourceId": 3,
      "chunkId": "86eb4766-78df-48d3-8238-694af2bd27f5",
      "documentId": "2fd16aae-b65f-49a2-bb15-d6be65038a6f",
      "documentTitle": "Apple 10-k",
      "chunkIndex": 114,
      "chunkType": "Prose",
      "tokenCount": 227,
      "distance": 0.38127275049256104,
      "cited": false,
      "text": "(1)On May 2, 2024, the Company announced a program to repurchase up to $110 billion of the Company’s common stock. During the fourth quarter of 2025, the Company utilized the final $19.\n8 billion under the May 2024 program. On May 1, 2025, the Company announced an additional program to repurchase up to $100 billion of the Company’s common stock.\nAs of September 27, 2025, $221 million of the May 2025 program had been utilized. The programs do not obligate the Company to acquire a minimum amount of shares.\nUnder the programs, shares may be repurchased in privately negotiated or open market transactions, including under plans complying with Rule 10b5-1 under the Exchange Act.\nApple Inc. | 2025 Form 10-K | 19\nCompany Stock Performance The following graph shows a comparison of five-year cumulative total shareholder return, calculated on a dividend-reinvested basis, for the Company, the S&P 500 Index and the Dow Jones U.S. Technology Total Stock Market Index.\n"
    },
    {
      "sourceId": 4,
      "chunkId": "3a442cff-202a-41b7-8658-c1b97c7a2f58",
      "documentId": "2fd16aae-b65f-49a2-bb15-d6be65038a6f",
      "documentTitle": "Apple 10-k",
      "chunkIndex": 119,
      "chunkType": "Prose",
      "tokenCount": 200,
      "distance": 0.4004994961168372,
      "cited": false,
      "text": "The Company’s fiscal year is the 52- or 53-week period that ends on the last Saturday of September. An additional week is included in the first fiscal quarter every five or six years to realign the Company’s fiscal quarters with calendar quarters, which occurred in the first quarter of 2023.\nThe Company’s fiscal years 2025 and 2024 spanned 52 weeks each, whereas fiscal year 2023 spanned 53 weeks.\nMacroeconomic Conditions\nMacroeconomic conditions, including inflation, interest rates and currency fluctuations, have directly and indirectly impacted, and could in the future materially impact, the Company’s results of operations and financial condition.\nApple Inc. | 2025 Form 10-K | 21\nTariffs and Other Measures Beginning in the second quarter of 2025, new U.S. Tariffs were announced, including additional tariffs on imports from China, India, Japan, South Korea, Taiwan, Vietnam and the EU, among others.\n"
    },
    {
      "sourceId": 5,
      "chunkId": "a02b2675-8290-4447-b262-85172e755239",
      "documentId": "2fd16aae-b65f-49a2-bb15-d6be65038a6f",
      "documentTitle": "Apple 10-k",
      "chunkIndex": 126,
      "chunkType": "Prose",
      "tokenCount": 49,
      "distance": 0.4315782268123527,
      "cited": false,
      "text": "Apple Inc. | 2025 Form 10-K | 23\nGross Margin\nProducts and Services gross margin and gross margin percentage for 2025, 2024 and 2023 were as follows (dollars in millions):"
    }
  ],


#### мнение Claude по 4.2 (по твоим `sources`)
- **`a-siri-ai`:** ты прав — в твоём запросе **все 5 источников из 10-K**, а «introduced Siri AI» живёт в 10-Q. Модель отказала правильно. Две находки:
  1. **Мусорные чанки-колонтитулы.** Источники 4 и 5 — это `"Apple Inc. | 2025 Form 10-K | 56"` и `"… | 58"`, по 14 токенов: нижние колонтитулы страниц, ставшие отдельными чанками. Они занимают 2 из 5 мест. Короткий текст со словом «Apple» оказывается «похож» почти на любой вопрос про Apple. В базе таких чанков (≤ 20 токенов): Apple 10-K — 7, Apple 10-Q — 3, JPM 10-Q — 57, **JPM 10-K — 422 из 2 200 (19 %)**.
  2. **Расхождение с харнессом:** в прогоне E0 у этого вопроса нужный чанк был на 4-м месте, а у тебя его нет в top-5. Дистанции у мест 3–5 почти равны (0.443 / 0.449 / 0.455) — хвост top-5 — это почти ничья, и мелкий шум в эмбеддинге вопроса (Azure не гарантирует бит-в-бит одинаковый вектор) или чуть другая формулировка вопроса меняют порядок. Если копировал вопрос не дословно из `eval-set.json` — это тоже объясняет.
- **`a-dividend-change`:** модель отказала правильно. В контексте есть только половина ответа — «quarterly cash dividend was $0.26 per share» из 10-K (источник 1); второй половины ($0.27 из 10-Q) нет — **опять все 5 источников из 10-K**. Попутно в источнике 1 видно «$0. 26» и в источнике 3 — «$19.\n8 billion» — тот самый дефект разорванных дробных чисел.
- **Общий вывод по 4.2:** «модель зря отказала» не подтвердилось ни разу — во всех трёх отказах нужного факта в контексте не было. **Промпт не смягчаем.**

#### Раскладка E1 по acme (черновик Claude — проверь)
| причина | вопросы | сколько |
|---|---|---|
| таблица с числом проиграла прозе на ту же тему (подпись таблицы оторвана, в табличном чанке нет слов) | `a-china-fy25`, `a-rnd-fy25`, `a-gm-pct-fy25` | 3 |
| 10-Q проигрывает 10-K (оба filing'а про одно и то же, 10-K «громче») | `a-buyback-q3fy26`, `a-siri-ai`, `a-dividend-change` | 3 |
| мусорные чанки-колонтитулы занимают места в top-5 | усугубляет `a-siri-ai` | (1) |
| модель отказала при наличии ответа | — | 0 |

По Globex (`g-cet1-2025`, `g-apple-card-allowance`, `g-expense-why`, `g-nir-why-q2-26`, `g-cet1-delta`) ты сказал, что тенденция та же; `g-nir-why-q2-26` и `g-cet1-delta` — это вопросы разметки/эталона (часть 3 и 4.3), а не системы.

**Что это значит для 4b (предложение, решаем вместе):**
1. **Починка таблиц на ингесте** — подпись таблицы («Operating expenses for 2025, 2024 and 2023…») и строка годов внутрь каждого табличного чанка → у чанка появятся слова, по которым его найдёт вектор. Бьёт по первой строке раскладки.
2. **Выкинуть колонтитулы** (`<Company> | <Form> | <page>`) на ингесте — дешёво, освобождает места в top-5.
3. **Склеить разорванные дробные числа** — дешёво, заодно с п. 1.
4. **10-K vs 10-Q** — не лечится ни таблицами, ни FTS напрямую. Варианты: поиск отдельно по каждому документу тенанта и слияние; или учитывать период из вопроса. Это самый «дизайнерский» пункт — обсудить отдельно.
5. **Гибрид (FTS)** — по-прежнему кандидат, но E1 по acme его не требует напрямую: exact-term уже 100 %. Логичнее сначала починить данные (1–3), перемерить, и только потом решать про гибрид.

### 4.3. Верный ответ без найденного якоря (miss✓)

- [x] `g-cet1-delta` — ответ 0.4 верный; это ошибка разметки, а не поиска. **Исправлено 2026-10-06:** добавлена группа якорей `[capital ratio - Standardized, 14.2, 14.6]` (квартальная строка JPM 10-Q, чанк 22, содержит оба значения). Eval-set sha256 `158f8425bbbf` → `8a02fdeeef19` → baseline перезапустить.

### 4.4. Итог E1

- [ ] 4.4.1. Свести раскладку: сколько провалов от «таблица без подписи/заголовков», от «тонет среди похожих», от «сравнение двух filing'ов», от «строгий промпт», от «разметка».
- [ ] 4.4.2. Посмотреть категории: table-value hit@5 73 % vs exact-term 100 % vs prose 57 % — совпадает ли с раскладкой.
- [ ] 4.4.3. Принести раскладку Claude → дизайн 4b. Кандидаты (выбираем по раскладке, не заранее):
  - **починка таблиц на ингесте** — повтор строки заголовков у разрезанной таблицы, подпись таблицы внутри табличного чанка, склейка разорванных дробных чисел («$10. 7 billion»);
  - **гибридный поиск** (Postgres FTS + RRF) — ловит точные строки вроде `Greater China`, `64,377`;
  - **хранение параметров чанкинга на документе** + проверка в харнессе (урок инцидента);
  - сравнения 10-K vs 10-Q — отдельный приём (например, поиск по каждому документу отдельно).

---

## Часть 5. Позже (не сейчас)

- [ ] E5 — temperature 0 / 0.2 / 0.6 с `--repeat 3`, смотреть accuracy и `stability`. Делать на финальной конфигурации поиска (после 4b), иначе шум поиска забьёт эффект.
- [ ] Holdout (`--holdout`, 6 вопросов) — **один раз**, на финальной конфигурации, как честная проверка, что мы не подогнали систему под 30 рабочих вопросов.
- [ ] `DECISIONS.md` — запись о поиске/чанкинге появится только после 4b, с цифрами из `compare` (McNemar p < 0.05; при n=30 нужно примерно 6–0 или 8–1 чистых «переворотов»).
- [ ] Когда/если появится квота на `gpt-6-sol` (Pay-As-You-Go): сменить `Chat:Deployment`, прогнать тот же eval-сет, `compare` с baseline.
