using MenuToOrder.Api.Guest;
using MenuToOrder.Api.Intake;
using MenuToOrder.Api.Owner;
using MenuToOrder.Data;
using MenuToOrder.Extraction;
using MenuToOrder.Verification;
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

var apiKey = config["Gemini:ApiKey"];
var live = !string.IsNullOrEmpty(apiKey);
if (live)
{
    builder.Services.AddSingleton(new GeminiOptions(apiKey!));
    builder.Services.AddHttpClient<IMenuReader, GeminiMenuReader>(http => http.Timeout = TimeSpan.FromMinutes(3));
}
else
{
    var recordings = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, config["Extraction:RecordingsPath"]!));
    builder.Services.AddSingleton<IMenuReader>(new RecordedMenuReader(recordings, TimeProvider.System));
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<MenuDb>().Database.MigrateAsync();
}

app.Logger.LogInformation(live
    ? "Reading menus live with Gemini."
    : "No Gemini key configured: replaying recorded responses, so only the sample photos can be read.");

// Owner links carry the credential in the path, so they must not leak through the Referer header.
app.Use((context, next) =>
{
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    return next();
});
app.UseAntiforgery();

app.MapIntake();
app.MapOwner();
app.MapGuest();

app.Run();
