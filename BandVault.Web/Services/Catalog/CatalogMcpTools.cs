using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace BandVault.Web.Services.Catalog
{
    // ============================================
    // C22: MCP-СЕРВЕР КАТАЛОГУ ФАН-КЛУБУ (Streamable HTTP, ендпоінт /mcp, заголовок X-Api-Key)
    // 4 тули на читання + 1 на запис
    // ============================================
    [McpServerToolType]
    public class CatalogMcpTools
    {
        private static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        [McpServerTool(Name = "list_merch", ReadOnly = true), Description("Список товарів мерчу гурту з цінами та залишками на складі. Можна фільтрувати за частиною назви.")]
        public static async Task<string> ListMerch(CatalogTools tools,
            [Description("Частина назви товару для фільтрації (необов'язково)")] string? query = null,
            [Description("Максимальна кількість товарів (1-50)")] int limit = 20)
            => JsonSerializer.Serialize(await tools.ListMerchAsync(query, limit), Json);

        [McpServerTool(Name = "get_merch_item", ReadOnly = true), Description("Повна інформація про товар мерчу за його id: опис, категорія, ціна, ціна для фанів, залишок.")]
        public static async Task<string> GetMerchItem(CatalogTools tools,
            [Description("Ідентифікатор товару")] int id)
            => JsonSerializer.Serialize(await tools.GetMerchItemAsync(id), Json);

        [McpServerTool(Name = "list_releases", ReadOnly = true), Description("Дискографія гурту: релізи (альбоми/сингли) з жанром, датою виходу та треклістом.")]
        public static async Task<string> ListReleases(CatalogTools tools)
            => JsonSerializer.Serialize(await tools.ListReleasesAsync(), Json);

        [McpServerTool(Name = "search_catalog", ReadOnly = true), Description("Повнотекстовий пошук (Lucene) по мерчу, релізах і треках. Підтримує пошук за префіксом слова.")]
        public static async Task<string> SearchCatalog(CatalogTools tools,
            [Description("Пошуковий запит")] string query,
            [Description("Обмежити тип: merch, release або track (необов'язково)")] string? type = null)
            => JsonSerializer.Serialize(await tools.SearchCatalogAsync(query, type), Json);

        [McpServerTool(Name = "create_merch_item", Destructive = false), Description("Додає новий товар у каталог мерчу. Категорія задається назвою (наприклад 'Одяг' або 'Аксесуари').")]
        public static async Task<string> CreateMerchItem(CatalogTools tools,
            [Description("Назва товару")] string title,
            [Description("Опис товару")] string description,
            [Description("Ціна в доларах")] decimal price,
            [Description("Кількість на складі")] int stock,
            [Description("Назва категорії")] string category,
            [Description("Знижена ціна для учасників фан-клубу (необов'язково)")] decimal? fanPrice = null)
            => JsonSerializer.Serialize(await tools.CreateMerchItemAsync(title, description, price, stock, category, fanPrice), Json);
    }
}
