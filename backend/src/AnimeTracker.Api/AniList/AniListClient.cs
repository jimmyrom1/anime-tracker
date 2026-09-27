using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using AnimeTracker.Domain;

namespace AnimeTracker.Api.AniList;

/// <summary>
/// Cliente de la API GraphQL pública de AniList (no necesita key). Su límite es de 90 peticiones
/// por minuto, reducido temporalmente a 30; se usan 25 con una ventana deslizante compartida por
/// toda la aplicación y, si se llena, las peticiones esperan en cola en vez de fallar.
/// </summary>
public class AniListClient(HttpClient http, RateLimiter limiter, ILogger<AniListClient> log)
{
    public const string BaseUrl = "https://graphql.anilist.co";

    private const string MediaFields = """
        id type format status episodes chapters volumes duration seasonYear startDate { year }
        genres averageScore isAdult coverImage { large } title { romaji english userPreferred }
        """;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static RateLimiter CreateLimiter() => new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
    {
        PermitLimit = 25,
        Window = TimeSpan.FromMinutes(1),
        SegmentsPerWindow = 6,
        QueueLimit = 50,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
    });

    public async Task<IReadOnlyList<Media>> SearchAsync(string query, MediaType type, CancellationToken ct)
    {
        var data = await QueryAsync<PageData>(
            $$"""
            query ($search: String, $type: MediaType) {
              Page(perPage: 20) { media(search: $search, type: $type, isAdult: false, sort: SEARCH_MATCH) { {{MediaFields}} } }
            }
            """,
            new { search = query, type = type == MediaType.Anime ? "ANIME" : "MANGA" },
            ct);
        return data?.Page.Media.Select(ToDomain).ToList() ?? [];
    }

    /// <summary>null si AniList no la tiene (404).</summary>
    public async Task<Media?> GetAsync(int id, CancellationToken ct)
    {
        var data = await QueryAsync<MediaData>(
            $$"""query ($id: Int) { Media(id: $id) { {{MediaFields}} } }""",
            new { id },
            ct);
        return data?.Media is { } m ? ToDomain(m) : null;
    }

    private async Task<T?> QueryAsync<T>(string query, object variables, CancellationToken ct) where T : class
    {
        using var lease = await limiter.AcquireAsync(1, ct);
        if (!lease.IsAcquired) throw new AniListUnavailableException("Demasiadas búsquedas a la vez; prueba en un momento.");

        using var response = await http.PostAsJsonAsync("", new { query, variables }, Json, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            log.LogWarning("AniList ha respondido 429 (Retry-After: {RetryAfter})", response.Headers.RetryAfter?.Delta);
            throw new AniListUnavailableException("AniList está limitando las peticiones; prueba en un minuto.");
        }
        if (!response.IsSuccessStatusCode) throw new AniListUnavailableException($"AniList ha respondido {(int)response.StatusCode}.");

        var body = await response.Content.ReadFromJsonAsync<GraphQlResponse<T>>(Json, ct);
        return body?.Data;
    }

    internal static Media ToDomain(MediaDto m) => new()
    {
        Id = m.Id,
        Type = m.Type == "MANGA" ? MediaType.Manga : MediaType.Anime,
        Title = m.Title.UserPreferred ?? m.Title.Romaji ?? m.Title.English ?? $"#{m.Id}",
        TitleEnglish = m.Title.English,
        CoverUrl = m.CoverImage?.Large,
        Episodes = m.Episodes,
        Chapters = m.Chapters,
        Volumes = m.Volumes,
        DurationMinutes = m.Duration,
        Format = m.Format,
        ReleaseStatus = m.Status,
        Year = m.SeasonYear ?? m.StartDate?.Year,
        Genres = m.Genres ?? [],
        AverageScore = m.AverageScore,
    };

    internal record GraphQlResponse<T>(T? Data);
    internal record PageData(PageDto Page);
    internal record PageDto(List<MediaDto> Media);
    internal record MediaData(MediaDto? Media);
    internal record TitleDto(string? Romaji, string? English, string? UserPreferred);
    internal record CoverDto(string? Large);
    internal record DateDto(int? Year);

    internal record MediaDto(
        int Id,
        string Type,
        string? Format,
        string? Status,
        int? Episodes,
        int? Chapters,
        int? Volumes,
        int? Duration,
        int? SeasonYear,
        DateDto? StartDate,
        List<string>? Genres,
        int? AverageScore,
        [property: JsonPropertyName("isAdult")] bool IsAdult,
        CoverDto? CoverImage,
        TitleDto Title);
}

public class AniListUnavailableException(string message) : Exception(message);
