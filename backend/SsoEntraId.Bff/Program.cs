using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Identity.Web;
using SsoEntraId.Bff.NativeAuth;
using System.Net.Http.Headers;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

// OIDC correlation/nonce cookies tích lũy qua nhiều lần login có thể vượt 32KB default.
builder.WebHost.ConfigureKestrel(o =>
    o.Limits.MaxRequestHeadersTotalSize = 65_536); // 64 KB

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

// Đăng ký SAU AddMicrosoftIdentityWebApp để handler chạy sau IPostConfigureOptions
// của MSIW (theo registration order). Nhờ vậy previousHandler capture đúng handler
// cuối của MSIW, đảm bảo prompt=create và screen_hint=signup được forward lên Entra.
builder.Services.PostConfigure<OpenIdConnectOptions>(
    OpenIdConnectDefaults.AuthenticationScheme,
    options =>
    {
        // Giới hạn thời gian sống của correlation/nonce cookies để tránh tích lũy
        options.CorrelationCookie.MaxAge = TimeSpan.FromMinutes(15);
        options.NonceCookie.MaxAge       = TimeSpan.FromMinutes(15);

        var previousHandler = options.Events.OnRedirectToIdentityProvider;
        options.Events.OnRedirectToIdentityProvider = async ctx =>
        {
            // Với fetch/XHR requests (vd: /auth/me khi chưa đăng nhập), trả về 401
            // thay vì redirect 302 ra Entra — tránh CORS error trên browser.
            var accept = ctx.Request.Headers.Accept.ToString();
            var isApiRequest = accept.Contains("application/json")
                || ctx.Request.Headers.XRequestedWith == "XMLHttpRequest";
            if (isApiRequest)
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.HandleResponse();
                return;
            }

            if (previousHandler is not null)
                await previousHandler(ctx);

            if (ctx.Properties.Items.TryGetValue("prompt", out var prompt)
                && !string.IsNullOrEmpty(prompt))
            {
                ctx.ProtocolMessage.Prompt = prompt;
            }
            if (ctx.Properties.Items.TryGetValue("screen_hint", out var screenHint)
                && !string.IsNullOrEmpty(screenHint))
            {
                ctx.ProtocolMessage.SetParameter("screen_hint", screenHint);
            }
        };
    });

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

// Native Authentication typed HttpClient
// Base URL: {instance}/{tenantDomain}/ — paths dùng signup/v1.0/* và oauth2/v2.0/token
var entraBase = $"{builder.Configuration["AzureAd:Instance"]!.TrimEnd('/')}/{builder.Configuration["Entra:TenantDomain"]}/";
builder.Services.AddHttpClient<EntraNativeAuthService>(c =>
{
    c.BaseAddress = new Uri(entraBase);
    c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
});

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

            string? token = null;
            try
            {
                var scopes = transformContext.HttpContext
                    .RequestServices
                    .GetRequiredService<IConfiguration>()["ApiScopes"]
                    ?? "api://YOUR_API_CLIENT_ID/.default";

                token = await tokenAcquisition.GetAccessTokenForUserAsync(new[] { scopes });
            }
            catch
            {
                // ITokenAcquisition failed (vd: session từ native auth không có MSAL cache).
                // Fallback: đọc access_token được lưu trong cookie auth properties bởi SignInAsync.
                token = await transformContext.HttpContext.GetTokenAsync("access_token");
            }

            if (!string.IsNullOrEmpty(token))
            {
                transformContext.ProxyRequest!.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
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
