using bringeri_api.DTOs.Tenants;

namespace bringeri_api.Services.Tenants;

public interface ITenantService
{
    Task<TenantBrandingDto?> GetBrandingAsync(string? tenantSlug = null);
}
