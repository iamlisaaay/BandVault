using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using BandVault.Web.Data;
using Xunit;

namespace BandVault.Tests
{
    // QA3: запис через REST API та MCP доступний лише з X-Api-Key або адміну
    public class ApiSecurityTests : IClassFixture<WebApplicationFactory<BandVault.Web.Program>>
    {
        private const string ApiKey = "test-api-key";
        private readonly WebApplicationFactory<BandVault.Web.Program> _factory;

        public ApiSecurityTests(WebApplicationFactory<BandVault.Web.Program> factory)
        {
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Api:Key", ApiKey);
                builder.UseSetting("ConnectionStrings:RabbitMQ", "");
                builder.UseSetting("Storage:Provider", "Local");
                builder.ConfigureTestServices(services =>
                {
                    var dbDescriptors = services.Where(d =>
                        d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>) ||
                        (d.ServiceType.IsGenericType && d.ServiceType.GetGenericTypeDefinition().Name.Contains("IDbContextOptionsConfiguration")))
                        .ToList();
                    foreach (var d in dbDescriptors) services.Remove(d);
                    services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase("ApiSecurityDb"));

                    var cacheDescriptors = services.Where(d => d.ServiceType == typeof(IDistributedCache)).ToList();
                    foreach (var d in cacheDescriptors) services.Remove(d);
                    services.AddDistributedMemoryCache();
                });
            });
        }

        private static object NewMerch => new { title = "Test", description = "Test", price = 10, stockQuantity = 1, categoryId = 1 };

        [Fact]
        public async Task PostMerch_WithoutApiKey_Returns401_NotRedirect()
        {
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var response = await client.PostAsJsonAsync("/api/merch", NewMerch);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task PostMerch_WithWrongApiKey_Returns401()
        {
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            client.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key");
            var response = await client.PostAsJsonAsync("/api/merch", NewMerch);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task PostMerch_WithValidApiKey_Returns201()
        {
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
            var response = await client.PostAsJsonAsync("/api/merch", NewMerch);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        [Fact]
        public async Task GetMerch_IsPublic()
        {
            var client = _factory.CreateClient();
            var response = await client.GetAsync("/api/merch");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Mcp_WithoutApiKey_Returns401()
        {
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var response = await client.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list" });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
