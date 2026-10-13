using BandVault.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration; 
namespace BandVault.Web.Data
{
    public static class DbInitializer
    {
        public static async Task SeedDataAsync(ApplicationDbContext context, IConfiguration config)
        {
            
            if (!await context.SiteSettings.AnyAsync())
            {
                context.SiteSettings.AddRange(
                    new SiteSetting { Key = "HeroVideoUrl", Value = "https://www.youtube.com/watch?v=F0Bfcfv_4k4" },
                    new SiteSetting { Key = "BandName", Value = "MADMANS ESPRIT" },
                    new SiteSetting { Key = "PromoText", Value = "Dandelion" },
                    new SiteSetting { Key = "Social_YouTube", Value = "https://youtube.com/@madmansesprit" },
                    new SiteSetting { Key = "Social_Instagram", Value = "https://instagram.com/madmansesprit_official" },
                    new SiteSetting { Key = "Social_Twitter", Value = "https://twitter.com/MadmansEsprit" },
                    new SiteSetting { Key = "Social_Discord", Value = "https://discord.gg/madmansesprit" },
                    new SiteSetting { Key = "Social_Telegram", Value = "https://t.me/madmansesprit" },
                    new SiteSetting { Key = "ContactEmail", Value = "booking@madmansesprit.com" }
                );
                await context.SaveChangesAsync();
            }

            // 2. Додаємо Тіри (Рівні підписки)
            if (!await context.SubscriptionTiers.AnyAsync())
            {
                var tiers = new List<SubscriptionTier>
                {
                    new SubscriptionTier
                    {
                        Name = "FAN",
                        Level = 1,
                        Price = 5.00m,
                        IsRecommended = false
                    },
                    new SubscriptionTier
                    {
                        Name = "INSIDER",
                        Level = 2,
                        Price = 15.00m,
                        IsRecommended = true
                    },
                    new SubscriptionTier
                    {
                        Name = "VIP CULT",
                        Level = 3,
                        Price = 50.00m,
                        IsRecommended = false
                    }
                };

                context.SubscriptionTiers.AddRange(tiers);
                await context.SaveChangesAsync(); // Зберігаємо, щоб отримати їхні ID

                // 3. Додаємо Фічі для цих Тірів
                context.TierFeatures.AddRange(
                    // Фічі для FAN (Level 1)
                    new TierFeature { TierId = tiers[0].Id, Description = "Доступ до закритих новин" },
                    new TierFeature { TierId = tiers[0].Id, Description = "Участь у розіграшах мерчу" },

 
                    new TierFeature { TierId = tiers[1].Id, Description = "Доступ до закритих новин" },
                    new TierFeature { TierId = tiers[1].Id, Description = "Ранній доступ до квитків" },
                    new TierFeature { TierId = tiers[1].Id, Description = "Ексклюзивні демо-записи" },

                    // Фічі для VIP CULT (Level 3)
                    new TierFeature { TierId = tiers[2].Id, Description = "Всі переваги попередніх рівнів" },
                    new TierFeature { TierId = tiers[2].Id, Description = "Щомісячний дзвінок з гуртом" },
                    new TierFeature { TierId = tiers[2].Id, Description = "Безкоштовна VIP-футболка щороку" }
                );
                await context.SaveChangesAsync();
            }
            // 1. Читаємо пошту адміна з appsettings.json
            string superAdminEmail = config["AdminSettings:SuperAdminEmail"] ?? "admin@bandvault.com";

                // 2. Якщо такого користувача ще немає - створюємо
                if (!await context.Users.AnyAsync(u => u.Email == superAdminEmail))
                {
                    // Шукаємо найвищий рівень підписки (наприклад, Tier 4)
                    var highestTier = await context.SubscriptionTiers
                        .OrderByDescending(t => t.Level)
                        .FirstOrDefaultAsync();

                    int tierId = highestTier != null ? highestTier.Id : 0;

                    var superAdmin = new User
                    {
                        Email = superAdminEmail,
                        DisplayName = superAdminEmail.Split('@')[0],
                        IsAdmin = true,
                        CreatedAt = DateTime.UtcNow,
                        SubscriptionTierId = tierId
                    };

                    context.Users.Add(superAdmin);

                    await context.SaveChangesAsync();
            }
        }
    }
}