using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SsoEntraId.Bff.NativeAuth;

public record StartResetRequest(string Email);
public record VerifyResetOtpRequest(string ContinuationToken, string Otp);
public record CompleteResetRequest(string ContinuationToken, string NewPassword, string ConfirmPassword);

[ApiController]
[Route("auth/native")]
[AllowAnonymous]
public class NativeResetPasswordController(EntraNativeAuthService nativeAuth) : ControllerBase
{
    // POST /auth/native/resetpassword/start
    [HttpPost("resetpassword/start")]
    public async Task<IActionResult> Start([FromBody] StartResetRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Email))
            return BadRequest(new { error = "email_required" });

        var init = await nativeAuth.StartResetPasswordAsync(req.Email, ct);
        if (init.Error is not null) return MapError(init.Error, init.ErrorDescription);

        var challenge = await nativeAuth.RequestResetOtpChallengeAsync(init.ContinuationToken, ct);
        if (challenge.Error is not null) return MapError(challenge.Error, challenge.ErrorDescription);

        return Ok(new
        {
            continuationToken = challenge.ContinuationToken,
            step = "otp-required",
            codeLength = challenge.CodeLength ?? 8,
            maskedEmail = challenge.MaskedEmail
        });
    }

    // POST /auth/native/resetpassword/verify-otp
    [HttpPost("resetpassword/verify-otp")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyResetOtpRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ContinuationToken) || string.IsNullOrWhiteSpace(req.Otp))
            return BadRequest(new { error = "invalid_request" });

        var result = await nativeAuth.SubmitResetOtpAsync(req.ContinuationToken, req.Otp, ct);

        if (result.Error is "password_reset_required" or "new_password_required"
            && !string.IsNullOrEmpty(result.ContinuationToken))
            return Ok(new { continuationToken = result.ContinuationToken, step = "password-required" });

        if (result.Error is not null) return MapError(result.Error, result.ErrorDescription);

        return Ok(new { continuationToken = result.ContinuationToken, step = "password-required" });
    }

    // POST /auth/native/resetpassword/complete
    [HttpPost("resetpassword/complete")]
    public async Task<IActionResult> Complete([FromBody] CompleteResetRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ContinuationToken) || string.IsNullOrWhiteSpace(req.NewPassword))
            return BadRequest(new { error = "invalid_request" });

        if (req.NewPassword != req.ConfirmPassword)
            return BadRequest(new { error = "password_mismatch", description = "Mật khẩu xác nhận không khớp." });

        var result = await nativeAuth.SubmitNewPasswordAsync(req.ContinuationToken, req.NewPassword, ct);
        if (result.Error is not null) return MapError(result.Error, result.ErrorDescription);

        // Poll completion (Entra may need a moment to finalize)
        var poll = await nativeAuth.PollResetCompletionAsync(result.ContinuationToken, ct);
        if (poll.Error is not null && poll.Error != "succeeded")
            return MapError(poll.Error, poll.ErrorDescription);

        return Ok(new { step = "complete" });
    }

    private IActionResult MapError(string error, string? desc) => error switch
    {
        "user_not_found"      => BadRequest(new { error, description = "Email không tồn tại trong hệ thống." }),
        "invalid_oob_value"   => BadRequest(new { error, description = "Mã xác minh không đúng." }),
        "expired_token"       => BadRequest(new { error, description = "Mã đã hết hạn, vui lòng bắt đầu lại." }),
        "password_too_weak"   => BadRequest(new { error, description = desc ?? "Mật khẩu không đủ mạnh." }),
        _                     => BadRequest(new { error, description = desc })
    };
}
