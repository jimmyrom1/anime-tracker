namespace AnimeTracker.Domain;

public record CountByKey(string Key, int Count, double? MeanScore);

public record MonthActivity(int Year, int Month, int Amount);

public record ListStats(
    MediaType Type,
    int Entries,
    IReadOnlyDictionary<ListStatus, int> ByStatus,
    /// <summary>Episodios vistos o capítulos leídos en total (incluye los que se han vuelto a ver).</summary>
    int ProgressTotal,
    /// <summary>Solo anime: episodios × duración, en días (como el "Days" de MyAnimeList).</summary>
    double DaysWatched,
    double? MeanScore,
    /// <summary>Cuántas obras tienen cada nota, del 1 al 10.</summary>
    IReadOnlyList<int> ScoreDistribution,
    IReadOnlyList<CountByKey> TopGenres,
    IReadOnlyList<CountByKey> Platforms,
    IReadOnlyList<MonthActivity> Activity);

/// <summary>
/// Estadísticas de una lista. Función pura: recibe las entradas (con su ficha) y los eventos de
/// progreso, y no sabe nada de la base de datos.
/// </summary>
public static class StatsCalculator
{
    public const int TopGenresCount = 8;

    /// <summary>Si AniList no da la duración (un anime recién anunciado), se asume la de un episodio normal.</summary>
    public const int DefaultEpisodeMinutes = 24;

    public static ListStats Compute(MediaType type, IReadOnlyCollection<ListEntry> entries, IEnumerable<ProgressEvent> events, int activityMonths, DateOnly today)
    {
        var mine = entries.Where(e => e.Media?.Type == type).ToList();

        // Lo visto incluye las veces que se ha vuelto a ver entera.
        int Watched(ListEntry e) => e.Progress + e.RepeatCount * (e.Media?.Total ?? 0);

        var minutes = type == MediaType.Anime
            ? mine.Sum(e => (long)Watched(e) * (e.Media?.DurationMinutes ?? DefaultEpisodeMinutes))
            : 0;

        var scored = mine.Where(e => e.Score is not null).ToList();
        var distribution = Enumerable.Range(ListEntry.MinScore, ListEntry.MaxScore)
            .Select(score => scored.Count(e => e.Score == score))
            .ToList();

        // "Planeo verla" no dice nada de tus gustos: no cuenta para los géneros.
        var started = mine.Where(e => e.Status != ListStatus.Planning).ToList();
        var genres = started
            .SelectMany(e => (e.Media?.Genres ?? []).Select(g => (Genre: g, e.Score)))
            .GroupBy(x => x.Genre)
            .Select(g => new CountByKey(g.Key, g.Count(), Mean(g.Select(x => x.Score))))
            .OrderByDescending(g => g.Count).ThenByDescending(g => g.MeanScore).ThenBy(g => g.Key)
            .Take(TopGenresCount)
            .ToList();

        var platforms = mine
            .Where(e => e.Platform is not null)
            // "crunchyroll" y "Crunchyroll" son la misma plataforma.
            .GroupBy(e => e.Platform!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CountByKey(g.First().Platform!, g.Count(), Mean(g.Select(e => e.Score))))
            .OrderByDescending(p => p.Count).ThenBy(p => p.Key)
            .ToList();

        // Últimos N meses, incluidos los que no tienen actividad (la gráfica no debe saltárselos).
        var firstMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-(activityMonths - 1));
        var amounts = events
            .Where(e => e.Type == type)
            .GroupBy(e => (e.At.Year, e.At.Month))
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));
        var activity = Enumerable.Range(0, activityMonths)
            .Select(i => firstMonth.AddMonths(i))
            .Select(m => new MonthActivity(m.Year, m.Month, amounts.GetValueOrDefault((m.Year, m.Month))))
            .ToList();

        return new ListStats(
            Type: type,
            Entries: mine.Count,
            ByStatus: Enum.GetValues<ListStatus>().ToDictionary(s => s, s => mine.Count(e => e.Status == s)),
            ProgressTotal: mine.Sum(Watched),
            DaysWatched: Math.Round(minutes / 1440.0, 1),
            MeanScore: Mean(scored.Select(e => e.Score)),
            ScoreDistribution: distribution,
            TopGenres: genres,
            Platforms: platforms,
            Activity: activity);
    }

    private static double? Mean(IEnumerable<int?> scores)
    {
        var values = scores.Where(s => s is not null).Select(s => s!.Value).ToList();
        return values.Count == 0 ? null : Math.Round(values.Average(), 2);
    }
}
