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
    public IActionResult Login(string? returnUrl = "/", string? provider = null)
    {
        var safeReturn = IsValidReturnUrl(returnUrl) ? returnUrl : "/";
        var redirectUri = FrontendUrl + safeReturn;

        if (User.Identity?.IsAuthenticated == true)
            return Redirect(redirectUri);

        var props = new AuthenticationProperties { RedirectUri = redirectUri };

        if (!string.IsNullOrEmpty(provider))
        {
            // Skip Entra's identity provider selection screen and go directly to the provider.
            props.Items["domain_hint"] = provider;
        }
        else
        {
            // Always show a fresh login screen — prevents Entra from auto-selecting
            // a cached account (which may have been deleted or belong to wrong provider).
            props.Items["prompt"] = "select_account";
        }

        return Challenge(props, OpenIdConnectDefaults.AuthenticationScheme);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await antiforgery.ValidateRequestAsync(HttpContext);

        // Native auth session không có Entra browser session nên không cần OIDC signout
        // (sẽ hiện màn hình account picker trống nếu gọi OIDC signout)
        var authResult = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var isNativeAuth = authResult.Properties?.Items.TryGetValue("auth_type", out var authType) == true
                           && authType == "native";

        if (isNativeAuth)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Redirect(FrontendUrl + "/");
        }

        return SignOut(
            new AuthenticationProperties { RedirectUri = FrontendUrl + "/" },
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme);
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        // "idp" claim is set by Entra External ID when user signs in via a federated provider.
        // Value is e.g. "google.com" for Google, absent/null for local email+password accounts.
        var idp = User.FindFirst("idp")?.Value;

        return Ok(new
        {
            Name = User.Identity?.Name,
            Email = User.FindFirst("preferred_username")?.Value
                    ?? User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value,
            Roles = User.Claims
                .Where(c => c.Type == "roles" || c.Type == System.Security.Claims.ClaimTypes.Role)
                .Select(c => c.Value),
            IdentityProvider = idp ?? "local",
        });
    }
}
