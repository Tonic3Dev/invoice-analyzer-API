using bringeri_api.Data;
using bringeri_api.DTOs.Tenants;
using bringeri_api.Services.TenantProvider;
using Microsoft.EntityFrameworkCore;

namespace bringeri_api.Services.Tenants;

public class TenantService : ITenantService
{
    private readonly AppDbContext _db;
    private readonly ITenantProvider _tenantProvider;

    public TenantService(AppDbContext db, ITenantProvider tenantProvider)
    {
        _db = db;
        _tenantProvider = tenantProvider;
    }

    public async Task<TenantBrandingDto?> GetBrandingAsync(string? tenantSlug = null)
    {
        var resolvedSlug = tenantSlug;

        if (string.IsNullOrWhiteSpace(resolvedSlug))
        {
            resolvedSlug = _tenantProvider.TenantSlug;
        }

        if (string.IsNullOrWhiteSpace(resolvedSlug))
        {
            return null;
        }

        var tenant = await _db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.IsActive && t.Slug.ToLower() == resolvedSlug.ToLower());

        if (tenant == null)
        {
            return null;
        }

        return new TenantBrandingDto
        {
            TenantSlug = tenant.Slug,
            TenantName = tenant.Name,
            PageTitle = tenant.PageTitle,
            PrimaryColor = tenant.PrimaryColor,
            SecondaryColor = tenant.SecondaryColor,
            DefaultLanguage = tenant.DefaultLanguage,
            LogoBase64 = tenant.LogoBase64,
        };
    }
}
