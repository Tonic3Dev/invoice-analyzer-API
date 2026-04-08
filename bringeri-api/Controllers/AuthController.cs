using System.Text;
using bringeri_api.DTOs.Auth;
using bringeri_api.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace bringeri_api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        var result = await _authService.LoginAsync(request);
        if (result == null)
        {
            var destinationTenant = await _authService.FindTenantSlugForCredentialsAsync(request);
            if (!string.IsNullOrWhiteSpace(destinationTenant))
            {
                return Unauthorized(new
                {
                    code = "TENANT_MISMATCH",
                    message = "User belongs to a different tenant.",
                    tenantSlug = destinationTenant,
                    tenantEncoded = EncodeTenantSlug(destinationTenant),
                });
            }

            return Unauthorized(new { code = "INVALID_CREDENTIALS", message = "Invalid credentials or tenant." });
        }

        return Ok(result);
    }

    [HttpGet("tenant-by-email")]
    public async Task<ActionResult<TenantByEmailResponse>> GetTenantByEmail([FromQuery] string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new { code = "INVALID_CREDENTIALS", message = "Invalid credentials or tenant." });
        }

        var tenant = await _authService.FindTenantByEmailAsync(email);
        if (tenant == null)
        {
            return NotFound(new { code = "INVALID_CREDENTIALS", message = "Invalid credentials or tenant." });
        }

        return Ok(new TenantByEmailResponse
        {
            TenantSlug = tenant.TenantSlug,
            TenantName = tenant.TenantName,
            TenantEncoded = EncodeTenantSlug(tenant.TenantSlug),
        });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<AuthUserDto>> Me()
    {
        var user = await _authService.GetCurrentUserAsync(User);
        if (user == null)
        {
            return Unauthorized(new { message = "Invalid token." });
        }

        return Ok(user);
    }

    private static string EncodeTenantSlug(string tenantSlug)
    {
        var bytes = Encoding.UTF8.GetBytes(tenantSlug);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
