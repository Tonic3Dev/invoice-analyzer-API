namespace bringeri_api.Services.TenantProvider;

public interface ITenantProvider
{
    Guid? TenantId { get; }

    string? TenantSlug { get; }

    bool HasTenant { get; }
}
