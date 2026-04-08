using bringeri_api.DTOs.Admin;
using bringeri_api.DTOs.Auth;
using bringeri_api.Services.Auth;
using Microsoft.AspNetCore.Mvc;

namespace bringeri_api.Controllers;

[ApiController]
[Route("api/admin-users")]
public class AdminUsersController : ControllerBase
{
    private readonly IAuthService _authService;

    public AdminUsersController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("admin")]
    public async Task<ActionResult<AuthUserDto>> CreateAdmin([FromBody] CreateAdminUserRequest request)
    {
        try
        {
            var createdUser = await _authService.CreateAdminUserAsync(request);
            return Ok(createdUser);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("user")]
    public async Task<ActionResult<AuthUserDto>> CreateUser([FromBody] CreateUserRequest request)
    {
        try
        {
            var createdUser = await _authService.CreateUserAsync(request);
            return Ok(createdUser);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
