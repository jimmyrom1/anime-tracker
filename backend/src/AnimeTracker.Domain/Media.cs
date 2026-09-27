namespace AnimeTracker.Domain;

public enum MediaType
{
    Anime,
    Manga,
}

/// <summary>
/// Ficha de un anime o manga copiada del catálogo de AniList. El id es el de AniList: así la misma
/// obra es la misma fila para todos los usuarios y el catálogo se descarga una sola vez.
/// </summary>
public class Media
{
    public int Id { get; set; }
    public MediaType Type { get; set; }
    public required string Title { get; set; }
    public string? TitleEnglish { get; set; }
    public string? CoverUrl { get; set; }

    /// <summary>Null mientras se emite y AniList no sabe cuántos tendrá.</summary>
    public int? Episodes { get; set; }

    public int? Chapters { get; set; }
    public int? Volumes { get; set; }

    /// <summary>Minutos por episodio: con esto se calculan los "días vistos".</summary>
    public int? DurationMinutes { get; set; }

    /// <summary>TV, MOVIE, OVA, MANGA, ONE_SHOT... tal cual lo da AniList.</summary>
    public string? Format { get; set; }

    /// <summary>FINISHED, RELEASING, NOT_YET_RELEASED... tal cual lo da AniList.</summary>
    public string? ReleaseStatus { get; set; }

    public int? Year { get; set; }
    public List<string> Genres { get; set; } = [];

    /// <summary>Nota media en AniList (0-100).</summary>
    public int? AverageScore { get; set; }

    public DateTimeOffset FetchedAt { get; set; }

    /// <summary>Episodios (anime) o capítulos (manga) en total, si se sabe.</summary>
    public int? Total => Type == MediaType.Anime ? Episodes : Chapters;
}
