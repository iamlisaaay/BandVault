using BandVault.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace BandVault.Web
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
         

    
            builder.Services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));



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
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseRouting();

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
                    var context = services.GetRequiredService<BandVault.Web.Data.ApplicationDbContext>();
                    // Цей метод автоматично заповнить SiteSettings та Tiers, якщо вони порожні
                    BandVault.Web.Data.DbInitializer.SeedDataAsync(context).Wait();
                }
                catch (Exception ex)
                {
                    var logger = services.GetRequiredService<ILogger<Program>>();
                    logger.LogError(ex, "Сталася помилка під час заповнення бази даних.");
                }
            }
            // ============================================

            app.Run(); // Цей рядок уже має бути у тебе в самому кінці
        }
    }
}
