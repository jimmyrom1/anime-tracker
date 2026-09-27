using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AnimeTracker.Api.AniList;
using AnimeTracker.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AnimeTracker.Tests.Api;

/// <summary>Reloj que el test puede mover.</summary>
public class TestTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-09-27T18:00:00Z");
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// AniList falso: responde a las mismas consultas GraphQL con un catálogo pequeño y cuenta las
/// peticiones, para comprobar la caché. <see cref="Down"/> simula que AniList no responde.
/// </summary>
public class FakeAniList : HttpMessageHandler
{
    public int Requests;
    public bool Down;

    public static readonly Dictionary<int, JsonObject> Catalog = new()
    {
        [154587] = Media(154587, "ANIME", "Sousou no Frieren", "Frieren: Beyond Journey's End", episodes: 28, status: "FINISHED", genres: ["Adventure", "Drama", "Fantasy"]),
        [21] = Media(21, "ANIME", "ONE PIECE", "One Piece", episodes: null, status: "RELEASING", genres: ["Action", "Adventure"]),
        [101922] = Media(101922, "ANIME", "Kimetsu no Yaiba", "Demon Slayer", episodes: 26, status: "FINISHED", genres: ["Action", "Supernatural"]),
        [105778] = Media(105778, "MANGA", "Chainsaw Man", "Chainsaw Man", chapters: 232, status: "FINISHED", genres: ["Action", "Horror"]),
    };

    private static JsonObject Media(int id, string type, string romaji, string english, int? episodes = null, int? chapters = null, string status = "FINISHED", string[]? genres = null) => new()
    {
        ["id"] = id, ["type"] = type, ["format"] = type == "ANIME" ? "TV" : "MANGA", ["status"] = status,
        ["episodes"] = episodes, ["chapters"] = chapters, ["volumes"] = null, ["duration"] = type == "ANIME" ? 24 : null,
        ["seasonYear"] = 2023, ["startDate"] = new JsonObject { ["year"] = 2023 },
        ["genres"] = new JsonArray((genres ?? []).Select(g => (JsonNode)g).ToArray()), ["averageScore"] = 90, ["isAdult"] = false,
        ["coverImage"] = new JsonObject { ["large"] = $"https://img.anili.st/media/{id}.jpg" },
        ["title"] = new JsonObject { ["romaji"] = romaji, ["english"] = english, ["userPreferred"] = romaji },
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Interlocked.Increment(ref Requests);
        if (Down) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
        var variables = body["variables"]!;
        JsonNode data;
        if (variables["id"] is { } idNode)
        {
            var id = idNode.GetValue<int>();
            if (!Catalog.TryGetValue(id, out var media)) return new HttpResponseMessage(HttpStatusCode.NotFound);
            data = new JsonObject { ["Media"] = media.DeepClone() };
        }
        else
        {
            var search = variables["search"]!.GetValue<string>().ToLowerInvariant();
            var type = variables["type"]!.GetValue<string>();
            var found = Catalog.Values
                .Where(m => m["type"]!.GetValue<string>() == type && m["title"]!.ToJsonString().Contains(search, StringComparison.OrdinalIgnoreCase))
                .Select(m => m.DeepClone()).ToArray();
            data = new JsonObject { ["Page"] = new JsonObject { ["media"] = new JsonArray(found) } };
        }
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new JsonObject { ["data"] = data }.ToJsonString(), Encoding.UTF8, "application/json"),
        };
    }
}

/// <summary>
/// La API completa en memoria, contra PostgreSQL real. En la CI hay un servicio de PostgreSQL;
/// en local vale cualquier base de datos, incluso un esquema aparte (Search Path=...).
/// </summary>
public class TestApp : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly string ConnectionString = Environment.GetEnvironmentVariable("TEST_DATABASE_URL")
        ?? "Host=localhost;Database=anime_test;Username=anime;Password=anime";

    public FakeAniList AniList { get; } = new();
    public TestTimeProvider Time { get; } = new();

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("AniList:BaseUrl", "http://anilist.test");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
            services.AddHttpClient<AniListClient>().ConfigurePrimaryHttpMessageHandler(() => AniList);
        });
    }

    public async Task InitializeAsync()
    {
        // Crea el esquema con las migraciones reales (lo hace el arranque de la app).
        _ = Server;
        await ResetAsync();
    }

    public new Task DisposeAsync() => base.DisposeAsync().AsTask();

    /// <summary>Vacía todas las tablas salvo el historial de migraciones.</summary>
    public async Task ResetAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Los nombres salen del modelo de EF, no de ninguna entrada externa: no hay riesgo de inyección.
        var tables = db.Model.GetEntityTypes().Select(t => t.GetTableName()).Where(t => t is not null).Distinct()
            .Select(t => "\"" + t + "\"");
        var sql = "TRUNCATE " + string.Join(", ", tables) + " RESTART IDENTITY CASCADE";
        await db.Database.ExecuteSqlRawAsync(sql);
        // La caché de búsquedas vive en memoria durante toda la app: se vacía entre tests.
        ((MemoryCache)Services.GetRequiredService<IMemoryCache>()).Clear();
        AniList.Requests = 0;
        AniList.Down = false;
        Time.Now = DateTimeOffset.Parse("2026-09-27T18:00:00Z");
    }

    private const string Password = "Contraseña123";

    /// <summary>Registra un usuario, inicia sesión y devuelve un cliente con su token.</summary>
    public async Task<HttpClient> LoginAsAsync(string email)
    {
        (await CreateClient().PostAsJsonAsync("/api/auth/register", new { email, password = Password })).EnsureSuccessStatusCode();
        return await SignInAsync(email);
    }

    /// <summary>Solo inicia sesión. El token dura 1 hora según el reloj de la app (también el de los tests).</summary>
    public async Task<HttpClient> SignInAsync(string email)
    {
        var client = CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonObject>())!["accessToken"]!.GetValue<string>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

[CollectionDefinition(nameof(ApiCollection))]
public class ApiCollection : ICollectionFixture<TestApp>;
