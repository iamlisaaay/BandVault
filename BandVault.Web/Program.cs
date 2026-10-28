using BandVault.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google; // <--- ДОДАНО ДЛЯ GOOGLE OAUTH2

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
                .AddCookie(options =>
                {
                    options.LoginPath = "/Auth/Login";
                    options.AccessDeniedPath = "/Auth/AccessDenied";
                })
                .AddGoogle(googleOptions =>
                {
                    // Ключі братимуться з appsettings.json. Якщо їх там немає, стоять заглушки, щоб проєкт не падав
                    googleOptions.ClientId = builder.Configuration["Authentication:Google:ClientId"] ?? "ЗАГЛУШКА_CLIENT_ID";
                    googleOptions.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"] ?? "ЗАГЛУШКА_CLIENT_SECRET";
                });

            // Add services to the container.
            builder.Services.AddControllersWithViews()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
                });

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseRouting();

            // ============================================
            // ОБОВ'ЯЗКОВО UseAuthentication МАЄ БУТИ ДО UseAuthorization
            // ============================================
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapStaticAssets();
            app.MapControllers();
            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}")
                .WithStaticAssets();

            // ============================================
            // ЗАПУСК СІДЕРА ДЛЯ ЗАПОВНЕННЯ БАЗИ ДАНИХ
            // ============================================
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
            // ============================================

            app.Run();
        }
    }
}