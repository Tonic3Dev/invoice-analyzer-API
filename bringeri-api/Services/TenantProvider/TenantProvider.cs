using bringeri_api.Data;
using Microsoft.EntityFrameworkCore;

namespace bringeri_api.Services.TenantProvider;

public class TenantProvider : ITenantProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    private Guid? _tenantId;
    private string? _tenantSlug;
    private bool _isResolved;

    public TenantProvider(IHttpContextAccessor httpContextAccessor, IDbContextFactory<AppDbContext> contextFactory)
    {
        _httpContextAccessor = httpContextAccessor;
        _contextFactory = contextFactory;
    }

    public Guid? TenantId
    {
        get
        {
            ResolveTenant();
            return _tenantId;
        }
    }

    public string? TenantSlug
    {
        get
        {
            ResolveTenant();
            return _tenantSlug;
        }
    }

    public bool HasTenant => TenantId.HasValue;

    private void ResolveTenant()
    {
        if (_isResolved)
        {
            return;
        }

        _isResolved = true;

        var context = _httpContextAccessor.HttpContext;
        var tenantHeader = context?.Request.Headers["X-Tenant"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(tenantHeader))
        {
            return;
        }

        using var db = _contextFactory.CreateDbContext();
        var tenant = db.Tenants
            .AsNoTracking()
            .FirstOrDefault(t => t.IsActive && t.Slug.ToLower() == tenantHeader.ToLower());

        if (tenant == null)
        {
            return;
        }

        _tenantId = tenant.Id;
        _tenantSlug = tenant.Slug;
    }
}
