using System.Text.Json.Serialization;
using AnimeTracker.Api;
using AnimeTracker.Api.AniList;
using AnimeTracker.Api.Catalog;
using AnimeTracker.Api.Data;
using AnimeTracker.Api.Lists;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddAuthorization();
builder.Services.AddIdentityApiEndpoints<AppUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddEntityFrameworkStores<AppDbContext>();

// Los enums viajan como texto ("Current", "Anime"): más legible y no se rompe si se reordenan.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AppClock>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<ListService>();

// Un único limitador para toda la app: el límite de AniList es por IP del servidor, no por usuario.
builder.Services.AddSingleton(_ => AniListClient.CreateLimiter());
builder.Services.AddHttpClient<AniListClient>(http =>
    {
        http.BaseAddress = new Uri(builder.Configuration["AniList:BaseUrl"] ?? AniListClient.BaseUrl);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("anime-tracker/1.0 (+https://github.com/jimmyrom1/anime-tracker)");
    })
    // Reintentos con espera exponencial ante fallos de red o 5xx, timeout y circuit breaker.
    .AddStandardResilienceHandler();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ErrorHandler>();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapOpenApi();
app.MapScalarApiReference("/docs", o => o.WithTitle("Anime Tracker API"));
app.MapAppEndpoints();

app.Run();

/// <summary>Visible para los tests de integración (WebApplicationFactory).</summary>
public partial class Program;
