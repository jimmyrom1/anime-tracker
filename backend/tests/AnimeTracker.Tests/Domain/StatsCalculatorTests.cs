using AnimeTracker.Domain;

namespace AnimeTracker.Tests.Domain;

public class StatsCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 9, 27);
    private static int _nextId = 1;

    private static ListEntry Entry(MediaType type, int? total, int progress, int? score = null, string? platform = null,
        ListStatus? status = null, int? minutes = 24, params string[] genres)
    {
        var entry = new ListEntry
        {
            UserId = "u1",
            Media = new Media
            {
                Id = _nextId++, Type = type, Title = "x", Episodes = total, Chapters = total,
                DurationMinutes = minutes, Genres = [.. genres],
            },
        };
        if (progress > 0) entry.SetProgress(progress, total, Today);
        if (status is not null) entry.SetStatus(status.Value, total, Today);
        entry.SetScore(score);
        entry.SetPlatform(platform);
        return entry;
    }

    [Fact]
    public void Days_watched_is_episodes_times_duration()
    {
        var entries = new[]
        {
            Entry(MediaType.Anime, 24, 24, minutes: 24),   // 576 min
            Entry(MediaType.Anime, 12, 6, minutes: 24),    // 144 min
            Entry(MediaType.Anime, 1, 1, minutes: 720),    // una película de 12 h: 720 min
            Entry(MediaType.Manga, 100, 50),               // el manga no suma días
        };

        var stats = StatsCalculator.Compute(MediaType.Anime, entries, [], 12, Today);

        Assert.Equal(3, stats.Entries);
        Assert.Equal(31, stats.ProgressTotal);
        Assert.Equal(Math.Round(1440 / 1440.0, 1), stats.DaysWatched);
    }

    [Fact]
    public void Rewatches_count_towards_what_you_have_watched()
    {
        var entry = Entry(MediaType.Anime, 12, 12);
        entry.StartRewatch();
        entry.SetProgress(3, 12, Today);

        var stats = StatsCalculator.Compute(MediaType.Anime, [entry], [], 12, Today);

        Assert.Equal(15, stats.ProgressTotal);
    }

    [Fact]
    public void Mean_and_distribution_only_use_scored_entries()
    {
        var entries = new[]
        {
            Entry(MediaType.Anime, 12, 12, score: 9),
            Entry(MediaType.Anime, 12, 12, score: 10),
            Entry(MediaType.Anime, 12, 12, score: 9),
            Entry(MediaType.Anime, 12, 3),
        };

        var stats = StatsCalculator.Compute(MediaType.Anime, entries, [], 12, Today);

        Assert.Equal(9.33, stats.MeanScore);
        Assert.Equal([0, 0, 0, 0, 0, 0, 0, 0, 2, 1], stats.ScoreDistribution);
    }

    [Fact]
    public void Genres_ignore_planned_entries_and_platforms_ignore_case()
    {
        var entries = new[]
        {
            Entry(MediaType.Anime, 12, 12, score: 8, platform: "Crunchyroll", genres: ["Action", "Fantasy"]),
            Entry(MediaType.Anime, 12, 6, platform: "crunchyroll", genres: ["Action"]),
            Entry(MediaType.Anime, 12, 0, platform: "Netflix", status: ListStatus.Planning, genres: ["Romance"]),
        };

        var stats = StatsCalculator.Compute(MediaType.Anime, entries, [], 12, Today);

        Assert.Equal(["Action", "Fantasy"], stats.TopGenres.Select(g => g.Key));
        Assert.Equal(new CountByKey("Action", 2, 8), stats.TopGenres[0]);
        Assert.Equal(["Crunchyroll", "Netflix"], stats.Platforms.Select(p => p.Key));
        Assert.Equal(2, stats.Platforms[0].Count);
    }

    [Fact]
    public void Activity_covers_every_month_even_empty_ones()
    {
        ProgressEvent Event(string at, int amount) => new() { UserId = "u1", Type = MediaType.Anime, Amount = amount, At = DateTimeOffset.Parse(at) };
        var events = new[]
        {
            Event("2026-09-02T20:00:00Z", 3), Event("2026-09-20T20:00:00Z", 2),
            Event("2026-07-10T20:00:00Z", 12),
            Event("2025-01-01T20:00:00Z", 99), // fuera de la ventana
        };

        var stats = StatsCalculator.Compute(MediaType.Anime, [], events, 3, Today);

        Assert.Equal(
            [new MonthActivity(2026, 7, 12), new MonthActivity(2026, 8, 0), new MonthActivity(2026, 9, 5)],
            stats.Activity);
    }
}
