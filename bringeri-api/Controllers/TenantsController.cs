using bringeri_api.DTOs.Tenants;
using bringeri_api.Services.Tenants;
using Microsoft.AspNetCore.Mvc;

namespace bringeri_api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TenantsController : ControllerBase
{
    private readonly ITenantService _tenantService;

    public TenantsController(ITenantService tenantService)
    {
        _tenantService = tenantService;
    }

    [HttpGet("branding")]
    public async Task<ActionResult<TenantBrandingDto>> GetBranding([FromQuery] string? tenantName = null)
    {
        var branding = await _tenantService.GetBrandingAsync(tenantName);
        if (branding == null)
        {
            return NotFound(new { message = "Tenant branding not found." });
        }

        return Ok(branding);
    }
}
