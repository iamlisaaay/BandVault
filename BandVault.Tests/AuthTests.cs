using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace BandVault.Tests
{
    public class AuthTests : IClassFixture<WebApplicationFactory<BandVault.Web.Program>>
    {
        private readonly WebApplicationFactory<BandVault.Web.Program> _factory;

        public AuthTests(WebApplicationFactory<BandVault.Web.Program> factory)
        {
            _factory = factory;
        }

        [Theory]
        [InlineData("/Posts/Create")]       // Захищено [Authorize]
        [InlineData("/SiteSettings/Index")] // Захищено [Authorize]
        public async Task Get_EndpointsReturnRedirectToLogin_ForUnauthenticatedUser(string url)
        {
            // Arrange (Підготовка)
            // Вимикаємо автоматичний перехід за редиректами, щоб зловити код 302
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

            // Act (Дія - імітуємо захід гостя на захищену сторінку)
            var response = await client.GetAsync(url);

            // Assert (Перевірка результату)
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

            var redirectUrl = response.Headers.Location?.ToString();
            Assert.NotNull(redirectUrl);
            Assert.Contains("/Auth/Login", redirectUrl);
        }

        [Fact]
        public async Task Get_HomePageReturnsSuccessAndCorrectContentType()
        {
            // Arrange
            var client = _factory.CreateClient();

            // Act
            var response = await client.GetAsync("/");

            // Assert
            response.EnsureSuccessStatusCode(); // Має бути код 200-299
            Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        }
    }
}