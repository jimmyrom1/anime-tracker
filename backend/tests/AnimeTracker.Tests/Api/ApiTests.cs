using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AnimeTracker.Domain;

namespace AnimeTracker.Tests.Api;

[Collection(nameof(ApiCollection))]
public class ApiTests(TestApp app) : IAsyncLifetime
{
    private const int Frieren = 154587;
    private const int OnePiece = 21;
    private const int ChainsawMan = 105778;

    public Task InitializeAsync() => app.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<JsonObject> Json(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonObject>(TestApp.Json))!;

    private static async Task<long> AddAsync(HttpClient client, int mediaId, string status = "Planning")
    {
        var res = await client.PostAsJsonAsync("/api/list", new { mediaId, status });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await Json(res))["id"]!.GetValue<long>();
    }

    private static object Update(string status, int progress, int? score = null, string? platform = null, string? notes = null) =>
        new { status, progress, score, platform, notes, startedOn = (string?)null, finishedOn = (string?)null };

    [Fact]
    public async Task Without_a_token_the_list_is_private()
    {
        var anonymous = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/list?type=Anime")).StatusCode);
    }

    [Fact]
    public async Task Search_marks_what_is_already_in_your_list()
    {
        var client = await app.LoginAsAsync("ana@test.local");
        await AddAsync(client, Frieren, "Current");

        var results = (await client.GetFromJsonAsync<JsonArray>("/api/catalog/search?type=Anime&q=frieren", TestApp.Json))!;

        var frieren = Assert.Single(results)!;
        Assert.Equal("Sousou no Frieren", frieren["media"]!["title"]!.GetValue<string>());
        Assert.Equal(28, frieren["media"]!["total"]!.GetValue<int>());
        Assert.Equal("Current", frieren["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Searches_are_cached_and_a_title_is_downloaded_once_for_everybody()
    {
        var ana = await app.LoginAsAsync("ana@test.local");
        var luis = await app.LoginAsAsync("luis@test.local");

        await ana.GetAsync("/api/catalog/search?type=Anime&q=frieren");
        await luis.GetAsync("/api/catalog/search?type=Anime&q=Frieren ");
        Assert.Equal(1, app.AniList.Requests);

        await AddAsync(ana, Frieren);
        await AddAsync(luis, Frieren);
        Assert.Equal(2, app.AniList.Requests); // la ficha se descarga una vez y se guarda en PostgreSQL
    }

    [Fact]
    public async Task Airing_shows_are_refreshed_and_AniList_outages_fall_back_to_the_stored_copy()
    {
        var client = await app.LoginAsAsync("ana@test.local");
        await AddAsync(client, OnePiece);

        app.Time.Now = app.Time.Now.AddHours(13);
        client = await app.SignInAsync("ana@test.local"); // el token anterior ha caducado (dura 1 h)
        app.AniList.Down = true;
        // AniList caído: la copia guardada sigue valiendo.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/catalog/{OnePiece}")).StatusCode);

        // Una obra que nunca se ha guardado no se puede servir: 503 con Retry-After.
        var res = await client.PostAsJsonAsync("/api/list", new { mediaId = ChainsawMan, status = "Planning" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
        Assert.NotNull(res.Headers.RetryAfter);
    }

    [Fact]
    public async Task The_same_title_cannot_be_added_twice_even_at_the_same_time()
    {
        var client = await app.LoginAsAsync("ana@test.local");
        var responses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => client.PostAsJsonAsync("/api/list", new { mediaId = Frieren, status = "Planning" })));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
    }

    [Fact]
    public async Task Watching_the_last_episode_completes_the_show_and_you_cannot_go_further()
    {
        var client = await app.LoginAsAsync("ana@test.local");
        var id = await AddAsync(client, Frieren, "Current");
        await client.PutAsJsonAsync($"/api/list/{id}", Update("Current", 27, platform: "Crunchyroll"));

        var last = await Json(await client.PostAsync($"/api/list/{id}/increment", null));
        Assert.Equal("Completed", last["status"]!.GetValue<string>());
        Assert.Equal(28, last["progress"]!.GetValue<int>());
        Assert.Equal("2026-09-27", last["finishedOn"]!.GetValue<string>());
        Assert.Equal("Crunchyroll", last["platform"]!.GetValue<string>());

        var beyond = await client.PostAsync($"/api/list/{id}/increment", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, beyond.StatusCode);
        Assert.NotNull((await Json(beyond))["errors"]!["progress"]);
    }

    [Fact]
    public async Task Concurrent_plus_ones_are_never_lost()
    {
        var client = await app.LoginAsAsync("ana@test.local");
        var id = await AddAsync(client, Frieren, "Current");

        // Diez "+1" a la vez (el móvil y el ordenador, o un doble toque).
        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => client.PostAsync($"/api/list/{id}/increment", null)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var list = (await client.GetFromJsonAsync<JsonArray>("/api/list?type=Anime", TestApp.Json))!;
        Assert.Equal(10, list[0]!["progress"]!.GetValue<int>());
        var stats = (await client.GetFromJsonAsync<JsonObject>("/api/stats?type=Anime", TestApp.Json))!;
        Assert.Equal(10, stats["activity"]!.AsArray().Last()!["amount"]!.GetValue<int>());
    }

    [Fact]
    public async Task Choosing_completed_by_hand_wins_over_the_progress_sent()
    {
        var client = await app.LoginAsAsync("ana@test.local");
        var id = await AddAsync(client, Frieren, "Current");

        var entry = await Json(await client.PutAsJsonAsync($"/api/list/{id}", Update("Completed", 3, score: 10)));

        Assert.Equal("Completed", entry["status"]!.GetValue<string>());
        Assert.Equal(28, entry["progress"]!.GetValue<int>());
        Assert.Equal(10, entry["score"]!.GetValue<int>());
    }

    [Fact]
    public async Task Invalid_scores_are_rejected_with_the_field()
    {
        var client = await app.LoginAsAsync("ana@test.local");
        var id = await AddAsync(client, Frieren);

        var res = await client.PutAsJsonAsync($"/api/list/{id}", Update("Current", 1, score: 11));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        var body = await Json(res);
        Assert.NotNull(body["errors"]!["score"]);
        Assert.Equal("out_of_range", body["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Nobody_can_see_or_touch_another_users_entries()
    {
        var ana = await app.LoginAsAsync("ana@test.local");
        var luis = await app.LoginAsAsync("luis@test.local");
        var id = await AddAsync(ana, Frieren);

        Assert.Equal(HttpStatusCode.NotFound, (await luis.PostAsync($"/api/list/{id}/increment", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await luis.DeleteAsync($"/api/list/{id}")).StatusCode);
        Assert.Empty((await luis.GetFromJsonAsync<JsonArray>("/api/list?type=Anime"))!);
    }

    [Fact]
    public async Task Anime_and_manga_are_separate_lists_with_their_own_stats()
    {
        var client = await app.LoginAsAsync("ana@test.local");
        var anime = await AddAsync(client, Frieren, "Current");
        var manga = await AddAsync(client, ChainsawMan, "Current");
        await client.PutAsJsonAsync($"/api/list/{anime}", Update("Completed", 28, score: 10, platform: "Crunchyroll"));
        await client.PutAsJsonAsync($"/api/list/{manga}", Update("Current", 40, score: 8, platform: "Manga Plus"));

        var animeStats = (await client.GetFromJsonAsync<JsonObject>("/api/stats?type=Anime", TestApp.Json))!;
        var mangaStats = (await client.GetFromJsonAsync<JsonObject>("/api/stats?type=Manga", TestApp.Json))!;

        Assert.Equal(1, animeStats["entries"]!.GetValue<int>());
        Assert.Equal(0.5, animeStats["daysWatched"]!.GetValue<double>()); // 28 × 24 min = 672 min
        Assert.Equal(10, animeStats["meanScore"]!.GetValue<double>());
        Assert.Equal(40, mangaStats["progressTotal"]!.GetValue<int>());
        Assert.Equal("Manga Plus", mangaStats["platforms"]![0]!["key"]!.GetValue<string>());
    }

    [Fact]
    public async Task Today_is_in_the_app_time_zone_not_the_servers()
    {
        app.Time.Now = DateTimeOffset.Parse("2026-09-27T22:30:00Z"); // 00:30 del 28 en Madrid
        var client = await app.LoginAsAsync("ana@test.local");
        var id = await AddAsync(client, Frieren);

        var entry = await Json(await client.PostAsync($"/api/list/{id}/increment", null));

        Assert.Equal("2026-09-28", entry["startedOn"]!.GetValue<string>());
    }

    [Fact]
    public async Task Public_profiles_show_the_list_without_private_notes()
    {
        var client = await app.LoginAsAsync("ana@test.local");
        var id = await AddAsync(client, Frieren, "Current");
        await client.PutAsJsonAsync($"/api/list/{id}", Update("Current", 5, notes: "Nota privada"));
        var anonymous = app.CreateClient();

        // Sin nombre o sin hacerlo público: no existe.
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/profiles/ana?type=Anime")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/me/profile", new { profileName = "Ana_Otaku", isProfilePublic = true })).StatusCode);

        var profile = (await anonymous.GetFromJsonAsync<JsonObject>("/api/profiles/ANA_otaku?type=Anime", TestApp.Json))!;
        Assert.Equal("ana_otaku", profile["profileName"]!.GetValue<string>());
        Assert.Null(profile["entries"]![0]!["notes"]);
        Assert.Equal(5, profile["stats"]!["progressTotal"]!.GetValue<int>());
    }

    [Fact]
    public async Task Profile_names_are_unique_and_validated()
    {
        var ana = await app.LoginAsAsync("ana@test.local");
        var luis = await app.LoginAsAsync("luis@test.local");
        await ana.PutAsJsonAsync("/api/me/profile", new { profileName = "otaku", isProfilePublic = false });

        Assert.Equal(HttpStatusCode.Conflict, (await luis.PutAsJsonAsync("/api/me/profile", new { profileName = "OTAKU", isProfilePublic = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await luis.PutAsJsonAsync("/api/me/profile", new { profileName = "a b", isProfilePublic = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await luis.PutAsJsonAsync("/api/me/profile", new { profileName = (string?)null, isProfilePublic = true })).StatusCode);
    }

    [Fact]
    public async Task Adding_your_history_is_not_activity_but_watching_is()
    {
        var client = await app.LoginAsAsync("ana@test.local");
        var id = await AddAsync(client, Frieren, "Completed"); // una serie que ya viste
        await AddAsync(client, OnePiece, "Current");
        await client.PostAsync($"/api/list/{id}/rewatch", null);
        await client.PostAsync($"/api/list/{id}/increment", null);

        var stats = (await client.GetFromJsonAsync<JsonObject>("/api/stats?type=Anime", TestApp.Json))!;

        Assert.Equal(1, stats["activity"]!.AsArray().Last()!["amount"]!.GetValue<int>());
    }

    [Fact]
    public async Task Rewatching_counts_in_the_stats()
    {
        var client = await app.LoginAsAsync("ana@test.local");
        var id = await AddAsync(client, Frieren, "Completed");
        await client.PostAsync($"/api/list/{id}/rewatch", null);
        await client.PostAsync($"/api/list/{id}/increment", null);

        var stats = (await client.GetFromJsonAsync<JsonObject>("/api/stats?type=Anime", TestApp.Json))!;
        Assert.Equal(29, stats["progressTotal"]!.GetValue<int>());
        Assert.Equal(1, stats["byStatus"]!["Current"]!.GetValue<int>());
    }
}
