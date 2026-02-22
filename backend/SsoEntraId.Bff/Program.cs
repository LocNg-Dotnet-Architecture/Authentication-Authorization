using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Identity.Web;
using System.Net.Http.Headers;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

var apiScopes = new[] { builder.Configuration["ApiScopes"] ?? "api://YOUR_API_CLIENT_ID/.default" };

// Authentication: OIDC + Cookie
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddMicrosoftIdentityWebApp(
        microsoftIdentityOptions =>
        {
            builder.Configuration.Bind("AzureAd", microsoftIdentityOptions);
            microsoftIdentityOptions.ResponseType = "code";

            // Kiểm tra group membership ngay sau khi Entra ID xác thực thành công
            // Chạy TRƯỚC khi cookie session được set → user không được group sẽ không có session
            microsoftIdentityOptions.Events.OnTokenValidated = ctx =>
            {
                var config = ctx.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
                var requiredGroupId = config["RequiredGroupId"];

                if (string.IsNullOrEmpty(requiredGroupId))
                    return Task.CompletedTask;

                var inGroup = ctx.Principal?.Claims
                    .Any(c => c.Type == "groups" && c.Value == requiredGroupId) ?? false;

                if (!inGroup)
                {
                    var frontendUrl = config["FrontendUrl"] ?? "http://localhost:5173";
                    ctx.Response.Redirect(frontendUrl + "/access-denied");
                    ctx.HandleResponse(); // Dừng OIDC flow, không set cookie
                }

                return Task.CompletedTask;
            };
        },
        cookieOptions =>
        {
            cookieOptions.Cookie.Name = "bff.session";
            cookieOptions.Cookie.HttpOnly = true;
            cookieOptions.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            cookieOptions.Cookie.SameSite = SameSiteMode.Lax;
            cookieOptions.ExpireTimeSpan = TimeSpan.FromHours(8);
            cookieOptions.SlidingExpiration = true;
            cookieOptions.Events.OnRedirectToLogin = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            cookieOptions.Events.OnRedirectToAccessDenied = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        })
    .EnableTokenAcquisitionToCallDownstreamApi(apiScopes)
    .AddInMemoryTokenCaches();

// CORS for frontend dev server
builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendDev", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddAntiforgery(options =>
{
    // Cookie nội bộ (HttpOnly, không cần JS đọc) - giữ tên mặc định
    // JS đọc cookie XSRF-TOKEN riêng (chứa request token) được set thủ công bên dưới
    options.HeaderName = "X-XSRF-TOKEN";
});

// YARP Reverse Proxy with Bearer token injection
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(context =>
    {
        context.AddRequestTransform(async transformContext =>
        {
            var tokenAcquisition = transformContext.HttpContext
                .RequestServices
                .GetRequiredService<ITokenAcquisition>();
            try
            {
                var scopes = transformContext.HttpContext
                    .RequestServices
                    .GetRequiredService<IConfiguration>()["ApiScopes"]
                    ?? "api://YOUR_API_CLIENT_ID/.default";

                var token = await tokenAcquisition.GetAccessTokenForUserAsync(
                    new[] { scopes });
                transformContext.ProxyRequest!.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
            }
            catch
            {
                // Token acquisition failed - API will return 401
            }
        });
    });

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors("FrontendDev");
app.UseAuthentication();
app.UseAuthorization();

// Set cookie XSRF-TOKEN (readable by JS) chứa request token sau khi authenticated
// Đây là token JS phải đọc và gửi lại qua header X-XSRF-TOKEN hoặc form field
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
        var tokens = antiforgery.GetAndStoreTokens(context);
        context.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,                           // JS cần đọc được
            Secure = true,
            SameSite = SameSiteMode.Strict,
            IsEssential = true
        });
    }
    await next();
});

app.MapControllers();
app.MapReverseProxy().RequireAuthorization();

app.Run();
