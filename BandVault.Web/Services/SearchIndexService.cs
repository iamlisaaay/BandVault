using BandVault.Web.Data;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.EntityFrameworkCore;

namespace BandVault.Web.Services
{
    public record SearchHit(string Type, int Id, string Title, string? Snippet, string Url, float Score);

    // ============================================
    // B4: ПОВНОТЕКСТОВИЙ ПОШУК (Lucene.Net)
    // Індекс у пам'яті: мерч, релізи, треки. Перебудовується ліниво після змін (MarkDirty)
    // ============================================
    public class SearchIndexService : IDisposable
    {
        private const LuceneVersion Version = LuceneVersion.LUCENE_48;
        private static readonly string[] SearchFields = { "title", "description", "extra" };

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SearchIndexService> _logger;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly StandardAnalyzer _analyzer = new(Version);

        private Lucene.Net.Store.Directory? _directory;
        private DirectoryReader? _reader;
        private volatile bool _dirty = true;

        public SearchIndexService(IServiceScopeFactory scopeFactory, ILogger<SearchIndexService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public void MarkDirty() => _dirty = true;

        public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, string? type = null, int limit = 20)
        {
            if (string.IsNullOrWhiteSpace(query)) return Array.Empty<SearchHit>();
            await EnsureIndexAsync();

            var searcher = new IndexSearcher(_reader!);
            var parser = new MultiFieldQueryParser(Version, SearchFields, _analyzer,
                new Dictionary<string, float> { ["title"] = 3f, ["extra"] = 1.5f, ["description"] = 1f })
            {
                DefaultOperator = Operator.AND
            };

            // Кожне слово шукаємо і точно, і як префікс ("vis" -> "visual"), щоб пошук працював під час набору
            var terms = query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => QueryParserBase.Escape(t.ToLowerInvariant()))
                .Select(t => $"({t} OR {t}*)");
            var luceneQuery = parser.Parse(string.Join(" ", terms));

            Query finalQuery = luceneQuery;
            if (!string.IsNullOrWhiteSpace(type))
            {
                finalQuery = new BooleanQuery
                {
                    { luceneQuery, Occur.MUST },
                    { new TermQuery(new Term("type", type.ToLowerInvariant())), Occur.MUST }
                };
            }

            var topDocs = searcher.Search(finalQuery, Math.Clamp(limit, 1, 100));
            return topDocs.ScoreDocs.Select(sd =>
            {
                var doc = searcher.Doc(sd.Doc);
                var docType = doc.Get("type");
                var id = int.Parse(doc.Get("id"));
                var description = doc.Get("description");
                return new SearchHit(docType, id, doc.Get("title"),
                    description != null && description.Length > 140 ? description[..140] + "…" : description,
                    doc.Get("url"), sd.Score);
            }).ToList();
        }

        private async Task EnsureIndexAsync()
        {
            if (!_dirty && _reader != null) return;

            await _lock.WaitAsync();
            try
            {
                if (!_dirty && _reader != null) return;
                _dirty = false;

                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var directory = new RAMDirectory();
                using (var writer = new IndexWriter(directory, new IndexWriterConfig(Version, _analyzer)))
                {
                    foreach (var m in await context.MerchItems.Include(m => m.Category).AsNoTracking().ToListAsync())
                    {
                        writer.AddDocument(BuildDoc("merch", m.Id, m.Title, m.Description, m.Category?.Name, $"/Merch/Details/{m.Id}"));
                    }
                    foreach (var r in await context.Releases.Include(r => r.Genre).AsNoTracking().ToListAsync())
                    {
                        writer.AddDocument(BuildDoc("release", r.Id, r.Title, r.Description, r.Genre?.Name, $"/Releases/Details/{r.Id}"));
                    }
                    foreach (var t in await context.Tracks.Include(t => t.Release).AsNoTracking().ToListAsync())
                    {
                        writer.AddDocument(BuildDoc("track", t.Id, t.Title, null, t.Release?.Title, $"/Releases/Details/{t.ReleaseId}"));
                    }
                    writer.Commit();
                }

                var oldReader = _reader;
                var oldDirectory = _directory;
                _reader = DirectoryReader.Open(directory);
                _directory = directory;
                oldReader?.Dispose();
                oldDirectory?.Dispose();

                _logger.LogInformation("Lucene-індекс перебудовано: {Count} документів", _reader.NumDocs);
            }
            finally
            {
                _lock.Release();
            }
        }

        private static Document BuildDoc(string type, int id, string title, string? description, string? extra, string url)
        {
            var doc = new Document
            {
                new StringField("type", type, Field.Store.YES),
                new StringField("id", id.ToString(), Field.Store.YES),
                new TextField("title", title ?? "", Field.Store.YES),
                new StoredField("url", url)
            };
            if (!string.IsNullOrWhiteSpace(description)) doc.Add(new TextField("description", description, Field.Store.YES));
            if (!string.IsNullOrWhiteSpace(extra)) doc.Add(new TextField("extra", extra, Field.Store.YES));
            return doc;
        }

        public void Dispose()
        {
            _reader?.Dispose();
            _directory?.Dispose();
            _analyzer.Dispose();
        }
    }
}
