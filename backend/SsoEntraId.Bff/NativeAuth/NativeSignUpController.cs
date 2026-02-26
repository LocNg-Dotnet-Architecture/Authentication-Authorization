using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SsoEntraId.Bff.NativeAuth;

public record StartSignUpRequest(string Email);
public record VerifyOtpRequest(string ContinuationToken, string Otp);
public record CompleteSignUpRequest(string ContinuationToken, string Password);

[ApiController]
[Route("auth/native")]
[AllowAnonymous]
public class NativeSignUpController(EntraNativeAuthService nativeAuth) : ControllerBase
{
    // POST /auth/native/signup/start
    // Khởi tạo sign-up: gửi OTP về email người dùng
    [HttpPost("signup/start")]
    public async Task<IActionResult> Start([FromBody] StartSignUpRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Email))
            return BadRequest(new { error = "email_required" });

        var init = await nativeAuth.StartSignUpAsync(req.Email, ct);
        if (init.Error is not null) return MapError(init.Error, init.ErrorDescription);

        // Ngay sau khi initiate, request OTP challenge để Entra gửi OTP về email
        var challenge = await nativeAuth.RequestOtpChallengeAsync(init.ContinuationToken, ct);
        if (challenge.Error is not null) return MapError(challenge.Error, challenge.ErrorDescription);

        return Ok(new
        {
            continuationToken = challenge.ContinuationToken,
            step = "otp-required",
            codeLength = challenge.CodeLength ?? 8,
            maskedEmail = challenge.MaskedEmail
        });
    }

    // POST /auth/native/signup/verify-otp
    // Xác minh OTP từ email
    [HttpPost("signup/verify-otp")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ContinuationToken) || string.IsNullOrWhiteSpace(req.Otp))
            return BadRequest(new { error = "invalid_request" });

        var result = await nativeAuth.SubmitOtpAsync(req.ContinuationToken, req.Otp, ct);

        // "credential_required" = OTP was VALID, Entra now asks for password next.
        // This is an intermediate state returned as HTTP 400 by Entra — treat it as success.
        if (result.Error is "credential_required" && !string.IsNullOrEmpty(result.ContinuationToken))
            return Ok(new { continuationToken = result.ContinuationToken, step = "password-required" });

        if (result.Error is not null) return MapError(result.Error, result.ErrorDescription);

        return Ok(new { continuationToken = result.ContinuationToken, step = "password-required" });
    }

    // POST /auth/native/signup/complete
    // Đặt password, trao đổi lấy tokens, tạo session cookie → user tự động đăng nhập
    [HttpPost("signup/complete")]
    public async Task<IActionResult> Complete([FromBody] CompleteSignUpRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ContinuationToken) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { error = "invalid_request" });

        var pwResult = await nativeAuth.SubmitPasswordAsync(req.ContinuationToken, req.Password, ct);
        if (pwResult.Error is not null) return MapError(pwResult.Error, pwResult.ErrorDescription);

        NativeAuthTokenResult tokens;
        try
        {
            tokens = await nativeAuth.ExchangeTokensAsync(pwResult.ContinuationToken, ct);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "token_exchange_failed", description = ex.Message });
        }

        var claims = DecodeJwtPayload(tokens.IdToken);
        var principal = BuildPrincipal(claims);
        var props = BuildAuthProps(tokens);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme, principal, props);

        return Ok(new { step = "complete" });
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private IActionResult MapError(string error, string? desc) => error switch
    {
        "user_already_exists" => Conflict(new   { error, description = desc }),
        "invalid_oob_value"   => BadRequest(new { error, description = "Mã xác minh không đúng." }),
        "expired_token"       => BadRequest(new { error, description = "Mã đã hết hạn, vui lòng bắt đầu lại." }),
        "password_too_weak"   => BadRequest(new { error, description = desc ?? "Mật khẩu không đủ mạnh." }),
        _                     => BadRequest(new { error, description = desc })
    };

    private static Dictionary<string, object?> DecodeJwtPayload(string jwt)
    {
        var part = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        var pad = part.Length % 4;
        if (pad > 0) part += new string('=', 4 - pad);
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(part));
        return JsonSerializer.Deserialize<Dictionary<string, object?>>(json) ?? [];
    }

    private static ClaimsPrincipal BuildPrincipal(Dictionary<string, object?> payload)
    {
        var claims = payload
            .Where(kv => kv.Value is not null)
            .Select(kv => kv.Key switch
            {
                "sub"                => new Claim(ClaimTypes.NameIdentifier, kv.Value!.ToString()!),
                "name"               => new Claim(ClaimTypes.Name,           kv.Value!.ToString()!),
                "email"              => new Claim(ClaimTypes.Email,          kv.Value!.ToString()!),
                "preferred_username" => new Claim("preferred_username",      kv.Value!.ToString()!),
                _                    => new Claim(kv.Key,                    kv.Value!.ToString()!)
            });

        return new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme,
            ClaimTypes.Name,
            ClaimTypes.Role));
    }

    private static AuthenticationProperties BuildAuthProps(NativeAuthTokenResult tokens)
    {
        var expiry = DateTimeOffset.UtcNow.AddSeconds(tokens.ExpiresIn);
        var props = new AuthenticationProperties { IsPersistent = true, ExpiresUtc = expiry };
        // Đánh dấu session này là native auth (không có Entra browser session)
        // → AuthController.Logout sẽ bỏ qua OIDC signout để tránh màn hình account picker
        props.Items["auth_type"] = "native";
        props.StoreTokens(
        [
            new AuthenticationToken { Name = "access_token",  Value = tokens.AccessToken  },
            new AuthenticationToken { Name = "id_token",      Value = tokens.IdToken       },
            new AuthenticationToken { Name = "refresh_token", Value = tokens.RefreshToken  },
            new AuthenticationToken
            {
                Name  = "expires_at",
                Value = expiry.ToString("o", CultureInfo.InvariantCulture)
            }
        ]);
        return props;
    }
}
