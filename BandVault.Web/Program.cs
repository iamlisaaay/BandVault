using BandVault.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;

namespace BandVault.Web
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

            // ============================================
            // АВТОРИЗАЦІЯ ЧЕРЕЗ COOKIE ТА GOOGLE OAUTH2 (C16)
            // ============================================
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.LoginPath = "/Auth/Login";
                options.AccessDeniedPath = "/Auth/AccessDenied";
            })
            .AddGoogle(GoogleDefaults.AuthenticationScheme, googleOptions =>
            {
                var clientId = builder.Configuration["Authentication:Google:ClientId"];
                var clientSecret = builder.Configuration["Authentication:Google:ClientSecret"];

                googleOptions.ClientId = string.IsNullOrWhiteSpace(clientId) ? "DUMMY_CLIENT_ID" : clientId;
                googleOptions.ClientSecret = string.IsNullOrWhiteSpace(clientSecret) ? "DUMMY_CLIENT_SECRET" : clientSecret;
                googleOptions.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                googleOptions.ClaimActions.MapJsonKey("urn:google:picture", "picture");
            });

            // ============================================
            // B9: КЕШУВАННЯ (REDIS DISTRIBUTED CACHE)
            // ============================================
            var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
            if (!string.IsNullOrWhiteSpace(redisConnectionString))
            {
                builder.Services.AddStackExchangeRedisCache(options =>
                {
                    options.Configuration = redisConnectionString;
                    options.InstanceName = "BandVault_";
                });
            }
            else
            {
                builder.Services.AddDistributedMemoryCache();
            }

            builder.Services.AddControllersWithViews()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
                });

            var app = builder.Build();

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapStaticAssets();
            app.MapControllers();
            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}")
                .WithStaticAssets();

            using (var scope = app.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                try
                {
                    var context = services.GetRequiredService<ApplicationDbContext>();
                    var config = services.GetRequiredService<IConfiguration>();

                    DbInitializer.SeedDataAsync(context, config).Wait();
                }
                catch (Exception ex)
                {
                    var logger = services.GetRequiredService<ILogger<Program>>();
                    logger.LogError(ex, "Сталася помилка під час заповнення бази даних.");
                }
            }

            app.Run();
        }
    }
}