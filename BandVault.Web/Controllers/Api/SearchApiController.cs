using BandVault.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace BandVault.Web.Controllers.Api
{
    // B4: API повнотекстового пошуку по каталогу (Lucene.Net)
    // GET /api/search?q=dandelion&type=merch|release|track&limit=10
    [Route("api/search")]
    [ApiController]
    public class SearchApiController : ControllerBase
    {
        private readonly SearchIndexService _search;

        public SearchApiController(SearchIndexService search)
        {
            _search = search;
        }

        [HttpGet]
        public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] string? type = null, [FromQuery] int limit = 20)
        {
            if (string.IsNullOrWhiteSpace(q)) return BadRequest(new { error = "Параметр q обов'язковий" });
            if (type != null && type is not ("merch" or "release" or "track"))
                return BadRequest(new { error = "type має бути merch, release або track" });

            var hits = await _search.SearchAsync(q, type, limit);
            return Ok(new { query = q, total = hits.Count, data = hits });
        }
    }
}
