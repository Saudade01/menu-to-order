using System.Threading.RateLimiting;
using MenuToOrder.Api.Guest;
using MenuToOrder.Api.Intake;
using MenuToOrder.Api.Owner;
using MenuToOrder.Data;
using MenuToOrder.Extraction;
using MenuToOrder.Verification;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.AddRazorComponents();
builder.Services.AddDbContext<MenuDb>(o => o.UseNpgsql(config.GetConnectionString("Menu")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(config.GetSection("Verification").Get<VerificationOptions>() ?? new VerificationOptions());
builder.Services.AddScoped<MenuIntake>();
builder.Services.AddScoped<VerificationGate>();
builder.Services.AddScoped<Review>();

// Every upload spends Gemini quota and every order lands on the owner's list, so one address gets only so many.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = (context, ct) =>
    {
        context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
        return new ValueTask(context.HttpContext.Response.WriteAsync("Çok sık denendi. Lütfen biraz sonra tekrar deneyin.", ct));
    };
    o.AddPolicy("uploads", http => PerAddress(http, 10, TimeSpan.FromHours(1)));
    o.AddPolicy("orders", http => PerAddress(http, 30, TimeSpan.FromMinutes(1)));
});

// Antiforgery tokens are signed with these keys; kept in memory, every restart would void every open form.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, config["DataProtection:KeysPath"] ?? "keys")));

var apiKey = config["Gemini:ApiKey"];
var live = !string.IsNullOrEmpty(apiKey);
var models = (config["Gemini:Models"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
if (live)
{
    // Free-tier models run out of capacity and daily quota separately, so one reader per model, tried in this order.
    builder.Services.AddHttpClient("gemini", http => http.Timeout = TimeSpan.FromMinutes(3));
    foreach (var model in models)
        builder.Services.AddScoped<IMenuReader>(sp => new GeminiMenuReader(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("gemini"), new GeminiOptions(apiKey!, model), TimeProvider.System));
}
else if (!builder.Environment.IsDevelopment())
{
    // Replaying the samples in production would answer every real menu with "could not read".
    // Exiting rather than throwing: as PID 1 in a container a crashed process can hang instead of stopping.
    Console.Error.WriteLine("Gemini:ApiKey is not set. Outside development menus are read live, so set it (Gemini__ApiKey).");
    return 1;
}
else
{
    var recordings = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, config["Extraction:RecordingsPath"]!));
    builder.Services.AddSingleton<IMenuReader>(new RecordedMenuReader(recordings, TimeProvider.System));
}

var app = builder.Build();

// shortcut: migrating at startup is safe for a single instance; make it a deploy step before running several.
await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<MenuDb>().Database.MigrateAsync();

app.Logger.LogInformation(live
    ? $"Reading menus live with {string.Join(", then ", models)}."
    : "No Gemini key configured: replaying recorded responses, so only the sample photos can be read.");

// Owner links carry the credential in the path, so they must not leak through the Referer header.
app.Use((context, next) =>
{
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    return next();
});
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAntiforgery();

app.MapIntake();
app.MapOwner();
app.MapGuest();

app.Run();
return 0;

static RateLimitPartition<string> PerAddress(HttpContext http, int limit, TimeSpan window) =>
    RateLimitPartition.GetFixedWindowLimiter(http.Connection.RemoteIpAddress?.ToString() ?? "",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = window });

// Lets the HTTP tests start this app with WebApplicationFactory.
public partial class Program;
