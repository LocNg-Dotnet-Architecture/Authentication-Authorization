using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SsoEntraId.Bff.Controllers;

[ApiController]
[Route("auth")]
public class AuthController(IConfiguration configuration, IAntiforgery antiforgery) : ControllerBase
{
    private string FrontendUrl => configuration["FrontendUrl"] ?? "http://localhost:5173";

    // Validate returnUrl: must be a relative path, no open redirect
    private static bool IsValidReturnUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        // Must start with / but not // (protocol-relative redirect)
        return url.StartsWith('/') && !url.StartsWith("//");
    }

    [HttpGet("login")]
    public IActionResult Login(string? returnUrl = "/")
    {
        var safeReturn = IsValidReturnUrl(returnUrl) ? returnUrl : "/";
        var redirectUri = FrontendUrl + safeReturn;

        if (User.Identity?.IsAuthenticated == true)
            return Redirect(redirectUri);

        return Challenge(
            new AuthenticationProperties { RedirectUri = redirectUri },
            OpenIdConnectDefaults.AuthenticationScheme);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await antiforgery.ValidateRequestAsync(HttpContext);

        return SignOut(
            new AuthenticationProperties { RedirectUri = FrontendUrl + "/" },
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme);
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        return Ok(new
        {
            Name = User.Identity?.Name,
            Email = User.FindFirst("preferred_username")?.Value
                    ?? User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value,
            Roles = User.Claims
                .Where(c => c.Type == "roles" || c.Type == System.Security.Claims.ClaimTypes.Role)
                .Select(c => c.Value)
        });
    }
}
