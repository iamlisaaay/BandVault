using BandVault.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace BandVault.Web.Data
{
    public static class DbInitializer
    {
        public static async Task SeedDataAsync(ApplicationDbContext context)
        {
            // 1. Додаємо налаштування сайту (SiteSettings)
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

                    // Фічі для INSIDER (Level 2)
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
        }
    }
}