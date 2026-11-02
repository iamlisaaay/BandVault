using BandVault.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BandVault.Web.Data
{
    public static class DbInitializer
    {
        public static async Task SeedDataAsync(ApplicationDbContext context, IConfiguration config)
        {
            if (context.Database.IsRelational())
            {
                await context.Database.MigrateAsync();
            }
            else
            {
                await context.Database.EnsureCreatedAsync();
            }

            // Статуси замовлень для мапи C8
            var existingStatuses = await context.OrderStatuses.Select(s => s.Name).ToListAsync();
            foreach (var statusName in new[] { "New", "Shipped", "Completed" })
            {
                if (!existingStatuses.Contains(statusName))
                {
                    context.OrderStatuses.Add(new OrderStatus { Name = statusName });
                }
            }
            await context.SaveChangesAsync();

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

            if (!await context.SubscriptionTiers.AnyAsync())
            {
                var tiers = new List<SubscriptionTier>
                {
                    new SubscriptionTier { Name = "FAN", Level = 1, Price = 5.00m, IsRecommended = false },
                    new SubscriptionTier { Name = "INSIDER", Level = 2, Price = 15.00m, IsRecommended = true },
                    new SubscriptionTier { Name = "VIP CULT", Level = 3, Price = 50.00m, IsRecommended = false }
                };

                context.SubscriptionTiers.AddRange(tiers);
                await context.SaveChangesAsync();

                context.TierFeatures.AddRange(
                    new TierFeature { TierId = tiers[0].Id, Description = "Доступ до закритих новин" },
                    new TierFeature { TierId = tiers[0].Id, Description = "Участь у розіграшах мерчу" },
                    new TierFeature { TierId = tiers[1].Id, Description = "Доступ до закритих новин" },
                    new TierFeature { TierId = tiers[1].Id, Description = "Ранній доступ до квитків" },
                    new TierFeature { TierId = tiers[1].Id, Description = "Ексклюзивні демо-записи" },
                    new TierFeature { TierId = tiers[2].Id, Description = "Всі переваги попередніх рівнів" },
                    new TierFeature { TierId = tiers[2].Id, Description = "Щомісячний дзвінок з гуртом" },
                    new TierFeature { TierId = tiers[2].Id, Description = "Безкоштовна VIP-футболка щороку" }
                );
                await context.SaveChangesAsync();
            }

            // B6: безкоштовний базовий план для нових користувачів (раніше всі одразу отримували платний FAN)
            if (!await context.SubscriptionTiers.AnyAsync(t => t.Level == 0))
            {
                var guest = new SubscriptionTier { Name = "GUEST", Level = 0, Price = 0m, IsRecommended = false };
                context.SubscriptionTiers.Add(guest);
                await context.SaveChangesAsync();
                context.TierFeatures.AddRange(
                    new TierFeature { TierId = guest.Id, Description = "Відкриті новини та релізи" },
                    new TierFeature { TierId = guest.Id, Description = "Стіна автографів і асистент" }
                );
                await context.SaveChangesAsync();
            }

            string superAdminEmail = config["AdminSettings:SuperAdminEmail"] ?? "admin@bandvault.com";

            if (!await context.Users.AnyAsync(u => u.Email == superAdminEmail))
            {
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