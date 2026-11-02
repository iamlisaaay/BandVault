# BandVault — нові пункти (реалізація, запуск, перевірка)

| Код | Що зроблено | Де в коді |
|---|---|---|
| **C10** | Моніторинг залишків лімітованого мерчу 5 способами: WebSocket, SignalR, Long Poll, Frequent Poll, Adaptive Poll (з ETag/304) + сторінка з метриками | `Services/StockNotifier.cs`, `Controllers/Api/StockApiController.cs`, `/ws/stock` у `Program.cs`, `Views/Merch/Live.cshtml` (меню «Наживо») |
| **C11** | SignalR у C10 (`StockHub`), а також у дошці та редакторі | `Hubs/StockHub.cs`, `Hubs/BoardHub.cs`, `Hubs/EditorHub.cs` |
| **C13** | Редактор поста синхронізується між вкладками/пристроями наживо | `Hubs/EditorHub.cs`, `wwwroot/js/post-editor.js` |
| **C14** | Підсвітка блоку, який редагує інший користувач (колір + «ІМ'Я редагує»), спалах зміненого блоку, FLIP-анімація переміщень | `wwwroot/js/post-editor.js` |
| **C18** | Спільна «Стіна автографів»: малювання на Canvas, курсори інших учасників, онлайн-список, гумка, збереження PNG, очищення адміном | `Hubs/BoardHub.cs`, `Views/Board/Index.cshtml` (меню «Стіна») |
| **B8** | Черга RabbitMQ: після замовлення фоновий обробник генерує PDF-інвойс (QuestPDF) у Blob і шле Telegram-сповіщення. Без брокера — in-memory черга | `Services/Messaging/*`, `Views/Orders/Success.cshtml` |
| **C19** | Усі медіа (обкладинки, мерч, аудіо, аватари, інвойси) — у Azure Blob Storage (локально Azurite), `Cache-Control: immutable`, `Storage:PublicBaseUrl` для CDN, CORS для Web Audio | `Services/AzureBlobService.cs`, `Services/LocalFileService.cs`, `docker-compose.yml` |
| **B4** | Повнотекстовий пошук Lucene.Net по мерчу, релізах і треках (префіксний, з вагами полів) | `Services/SearchIndexService.cs`, `GET /api/search?q=&type=` |
| **C22** | MCP-сервер (Streamable HTTP) з 4 тулами на читання і 1 на запис | `Services/Catalog/CatalogMcpTools.cs`, `/mcp` |
| **C21** | ШІ-асистент: чат, відповідь стрімиться через SSE, показуються думки та виклики інструментів (ті самі, що в MCP) | `Controllers/AssistantController.cs`, `Views/Assistant/Index.cshtml` (меню «Асистент») |
| **B7** | Serverless API на Azure Functions: модерація повідомлень фанатів, генерація і перевірка підписаних промокодів | `BandVault.Functions/` |
| **QA2** | 10 E2E-сценаріїв на Playwright проти справжнього сервера та PostgreSQL | `BandVault.E2E/` |
| **C3** | GitHub Actions: збірка → тести (unit + E2E) → Docker-образ у GHCR → розгортання на Render → smoke-тест `/health`; Functions — в Azure | `.github/workflows/ci-cd.yml` |
| **C2** | Blueprint для Render (веб-сервіс у Docker + PostgreSQL), підтримка `DATABASE_URL`, `ForwardedHeaders`, `/health` | `render.yaml`, `Data/DatabaseUrl.cs` |

Додатково: візуалізатор плеєра має панель налаштувань (⚙ або клік по візуалізатору). Виправлення помилок старих пунктів — у [REVIEW-ERRORS.md](REVIEW-ERRORS.md).

Також: запис через REST API і MCP тепер вимагає адміна або заголовок `X-Api-Key` (раніше API було відкрите). У Development ключ — `dev-bandvault-api-key` (`appsettings.Development.json`).

---

## Локальний запуск

```bash
docker compose up -d db redis rabbitmq azurite
```

Потім запустіть `BandVault.Web` з Visual Studio. У Development медіа за замовчуванням зберігаються локально (`Storage:Provider = Local`). Щоб працювати з Azurite: `Storage__Provider=Blob` та `ConnectionStrings__AzureBlobStorage=UseDevelopmentStorage=true`.

Повністю в Docker:

```bash
docker compose up -d --build
```

Секрети (не комітити!):

```bash
dotnet user-secrets set "Anthropic:ApiKey" "<ключ>" --project BandVault.Web
dotnet user-secrets set "TelegramBot:Token" "<новий токен>" --project BandVault.Web
dotnet user-secrets set "Api:Key" "<довгий випадковий ключ>" --project BandVault.Web
```

---

## C10 — порівняльний аналіз способів зв'язку

Методика: сторінка «Наживо», режим «Авто-продажі» (зміна залишку кожні 3 с), по 30 с на кожен режим, локальний сервер. «Байт» — корисне навантаження (JSON без HTTP-заголовків), «затримка» — від моменту зміни на сервері до відображення в браузері.

| Спосіб | HTTP-запитів / з'єднань | Оновлень | Порожніх відповідей | Байт корисних даних | Сер. затримка |
|---|---|---|---|---|---|
| WebSocket | 1 | 9 | 0 | 1 002 | ~0 мс |
| SignalR (WebSocket-транспорт) | 1 | 10 | 0 | 1 112 | ~1 мс |
| Long Poll | 11 | 9 | 0 | 1 011 | ~2 мс |
| Frequent Poll (1 с, з ETag) | 25 | 9 | 15 (304) | 1 011 | ~478 мс |
| Adaptive Poll (1→10 с) | 20 | 11 | 9 (304) | 1 213 | ~1 337 мс |

Висновки:
- **WebSocket / SignalR** — одне постійне з'єднання, оновлення миттєві, накладні витрати мінімальні (кадр WS має 2–14 байт заголовка проти ~300–800 байт HTTP-заголовків). Мінус: сервер тримає з'єднання для кожного клієнта (пам'ять, балансувальник має підтримувати WS). SignalR додає трохи байтів протоколу, зате дає автоперепідключення, групи та fallback на SSE/Long Polling.
- **Long Poll** — затримка майже як у WebSocket, але кожне оновлення коштує новий HTTP-запит із повними заголовками. Сервер тримає «висячі» запити (до 25 с), а без змін клієнт отримує 204 і перепідключається. Працює через будь-які проксі.
- **Frequent Poll** — найпростіший, але найдорожчий: запити йдуть постійно, навіть коли нічого не змінюється (тут 60% відповідей порожні). Середня затримка — половина інтервалу. ETag/304 економить тіло відповіді, але не запит.
- **Adaptive Poll** — у «тиші» інтервал зростає до 10 с (у ~10 разів менше запитів, ніж у Frequent Poll), після зміни повертається до 1 с. Ціна економії — більша затримка. За частих змін (як у тесті) виграш невеликий, за рідких — значний.
- **Для лімітованого мерчу** (важливі секунди, багато глядачів) обрано SignalR: миттєво, з автоперепідключенням і fallback. Polls варто лишати як запасний варіант для клієнтів без WebSocket.

---

## C22 — підключення MCP-сервера до агента

Ендпоінт: `http://localhost:5088/mcp` (Streamable HTTP), заголовок `X-Api-Key`.

Тули: `list_merch`, `get_merch_item`, `list_releases`, `search_catalog` (читання) і `create_merch_item` (запис).

Claude Code:

```bash
claude mcp add --transport http bandvault http://localhost:5088/mcp --header "X-Api-Key: dev-bandvault-api-key"
```

GitHub Copilot у VS Code (`.vscode/mcp.json`):

```json
{
  "servers": {
    "bandvault": {
      "type": "http",
      "url": "http://localhost:5088/mcp",
      "headers": { "X-Api-Key": "dev-bandvault-api-key" }
    }
  }
}
```

Ті самі інструменти використовує ШІ-асистент C21 (`CatalogTools`), тому сервер можна демонструвати і з власного агента.

## C21 — ШІ-асистент

Модель `claude-opus-5-5` (адаптивне мислення, короткі підсумки думок). Увімкнено серверний fallback: якщо класифікатори безпеки відхилять запит, сервер сам повторить його на резервній моделі. Події SSE: `thinking`, `tool_call`, `tool_result`, `text`, `done`, `error`. Додавати товари через асистента може лише адміністратор. Потрібен ключ `Anthropic:ApiKey` (user-secrets) або змінна `ANTHROPIC_API_KEY`.

## B7 — Azure Functions

```bash
cd BandVault.Functions
func start
```

```bash
curl -X POST http://localhost:7071/api/moderation/check -H "Content-Type: application/json" -d "{\"author\":\"fan\",\"text\":\"Найкращий концерт!\"}"
```

```bash
curl -X POST http://localhost:7071/api/promo/generate -H "Content-Type: application/json" -d "{\"discountPercent\":15,\"validDays\":30,\"campaign\":\"tour\"}"
```

```bash
curl "http://localhost:7071/api/promo/validate?code=<код>"
```

Для запуску потрібні Azure Functions Core Tools. У хмарі `promo/generate` захищено function key, секрет підпису — налаштування `PROMO_SECRET`.

---

## QA2 — E2E-тести та покриття

```bash
docker compose up -d db
```

```bash
dotnet test BandVault.E2E
```

Тести самі піднімають сервер на окремій базі `bandvault_e2e` і відкривають Microsoft Edge (`E2E_BROWSER=chromium` для CI, `E2E_HEADED=1` — показати вікно).

| Сценарій | Пункти |
|---|---|
| Лендінг: герой, контакти, OpenGraph | C6, C7 |
| Аноніма перенаправляє на логін | C16 |
| Фанат не відкриває адмін-форми, не бачить «Мапу» | C16, B6 |
| Адмін створює товар із Select2-автокомплітом категорії | C1, F2 |
| Живий пошук у каталозі відкриває сторінку товару | F2, C5, B1 |
| Кошик → оформлення → PDF-інвойс з фонової черги | C8 (точка на мапі), B8, C19 |
| Зміна залишку через API приходить по WebSocket | C10, C5 |
| Редактор: drag-n-drop і відновлення чернетки після перезавантаження | C12, F3 |
| Два «пристрої»: синхронізація тексту і підсвітка блоку | C13, C14 |
| Панель візуалізатора: пресет, стиль, колір, збереження після перезавантаження | F6, C15 |

**Оцінка покриття:** E2E проходять 10 із 13 ключових користувацьких потоків (77%): каталог, автокомпліт, доступи, замовлення, редактор, синхронізація, залишки наживо. Не покриті: вхід через Google (зовнішній провайдер, у тестах замінено DEV LOGIN), стіна автографів (Canvas), ШІ-асистент (потребує платного API), мапа адміна. Покриття коду збирається через `--collect:"XPlat Code Coverage"` (coverlet) і публікується в артефактах CI.

---

## C3 / C2 — CI/CD і хмара

1. Render → **New → Blueprint** → репозиторій `iamlisaaay/BandVault` → застосувати `render.yaml` (створить PostgreSQL і веб-сервіс).
2. У Render задати секрети (Google, Telegram, Azure Blob, `ANTHROPIC_API_KEY`); у налаштуваннях сервісу скопіювати **Deploy Hook URL**.
3. GitHub → Settings → Secrets → Actions: `RENDER_DEPLOY_HOOK_URL`, `RENDER_APP_URL` (адреса `https://….onrender.com`). Для Functions: `AZURE_FUNCTIONAPP_NAME`, `AZURE_FUNCTIONAPP_PUBLISH_PROFILE`.
4. Після push у `main` (або поточну `master`) workflow збирає проєкт, проганяє тести, публікує образ і розгортає. Розгортання відбувається лише якщо тести зелені.

> Умова C3 говорить про гілку `main`, а в репозиторії зараз `master`. Workflow реагує на обидві. Для захисту варто перейменувати гілку на `main`.
