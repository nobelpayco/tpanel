using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using PayDoPay.Api.Auth;
using PayDoPay.Api.Spa;
using PayDoPay.Application;
using PayDoPay.Application.Common.Interfaces;
using PayDoPay.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ---- Servisler ----
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Frontend snake_case bekliyor; controller'lar anonim/DTO ile kendi şekillerini verir.
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
    });

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();
builder.Services.AddSingleton<ViteManifestService>();

// Infrastructure'ın ASP.NET'e bağımlı olmaması için content root köprüsü
builder.Services.AddSingleton<PayDoPay.Infrastructure.Services.IWebHostEnvironmentMarker>(
    sp => new WebHostEnvironmentMarker(sp.GetRequiredService<IWebHostEnvironment>().ContentRootPath));

// ---- Kimlik doğrulama / yetkilendirme ----
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services
    .AddAuthentication(OpaqueTokenAuthenticationHandler.SchemeName)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, OpaqueTokenAuthenticationHandler>(
        OpaqueTokenAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(options => options.AddPayDoPayPolicies());

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

var app = builder.Build();

// ---- Pipeline ----
app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

// Merchant API v1 HMAC (sadece deposit/withdraw/transaction yollarında etkin)
app.UseMiddleware<PayDoPay.Api.Middleware.MerchantApiAuthMiddleware>();

// Frontend statik dosyaları (public/ ve public/build/) — frontend kaynağı birebir korunur.
var manifest = app.Services.GetRequiredService<ViteManifestService>();
if (Directory.Exists(manifest.PublicPath))
{
    var fileProvider = new PhysicalFileProvider(manifest.PublicPath);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
}

app.MapControllers();

// SPA fallback — API dışı tüm GET istekleri index HTML'e döner (Vue router devralır).
app.MapFallback(([FromServices] ViteManifestService vite) =>
    Results.Content(vite.RenderIndexHtml(), "text/html; charset=utf-8"));

app.Run();

/// <summary>IWebHostEnvironment.ContentRootPath'i Infrastructure'a taşıyan köprü.</summary>
file sealed class WebHostEnvironmentMarker(string contentRootPath)
    : PayDoPay.Infrastructure.Services.IWebHostEnvironmentMarker
{
    public string ContentRootPath { get; } = contentRootPath;
}
