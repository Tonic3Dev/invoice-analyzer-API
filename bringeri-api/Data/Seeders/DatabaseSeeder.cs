using bringeri_api.Entities;
using Microsoft.EntityFrameworkCore;

namespace bringeri_api.Data.Seeders;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(AppDbContext context)
    {
        var tenants = new List<Tenant>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Tonic3",
                Slug = "tonic3",
                PageTitle = "Invoice Analyzer",
                PrimaryColor = "#cb4b27",
                SecondaryColor = "#180901",
                DefaultLanguage = "en",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Bringeri",
                Slug = "bringeri",
                PageTitle = "Invoice Analyzer",
                PrimaryColor = "#ad160d",
                SecondaryColor = "#f5f5f5",
                DefaultLanguage = "es",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            },
        };

        foreach (var tenant in tenants)
        {
            var existing = await context.Tenants.FirstOrDefaultAsync(t => t.Slug == tenant.Slug);
            if (existing == null)
            {
                context.Tenants.Add(tenant);
                continue;
            }

            existing.Name = tenant.Name;
            existing.PageTitle = tenant.PageTitle;
            existing.PrimaryColor = tenant.PrimaryColor;
            existing.SecondaryColor = tenant.SecondaryColor;
            existing.DefaultLanguage = tenant.DefaultLanguage;
            existing.IsActive = true;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync();
    }
}
