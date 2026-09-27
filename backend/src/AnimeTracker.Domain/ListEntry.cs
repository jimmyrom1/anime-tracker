namespace AnimeTracker.Domain;

/// <summary>Estado de una obra en tu lista (mismos estados que MyAnimeList y AniList).</summary>
public enum ListStatus
{
    /// <summary>Viendo (anime) o leyendo (manga).</summary>
    Current,
    Completed,
    Paused,
    Dropped,
    Planning,
}

/// <summary>Error de una regla de negocio, asociado al campo que lo provoca.</summary>
public class DomainException(string field, string code, string message) : Exception(message)
{
    public string Field { get; } = field;
    public string Code { get; } = code;
}

/// <summary>
/// Una obra en la lista de un usuario. Toda la lógica de progreso vive aquí y no en los endpoints:
/// así la misma regla se aplica venga el cambio de donde venga, y se testea sin base de datos.
/// </summary>
public class ListEntry
{
    public const int MinScore = 1;
    public const int MaxScore = 10;
    public const int MaxPlatformLength = 40;
    public const int MaxNotesLength = 2000;

    public long Id { get; set; }
    public required string UserId { get; set; }
    public int MediaId { get; set; }
    public Media? Media { get; set; }

    public ListStatus Status { get; private set; } = ListStatus.Planning;

    /// <summary>Último episodio visto o capítulo leído.</summary>
    public int Progress { get; private set; }

    /// <summary>1-10, o null si aún no la has puntuado.</summary>
    public int? Score { get; private set; }

    /// <summary>Dónde la ves o la lees: Crunchyroll, Netflix, Manga Plus, tomo físico...</summary>
    public string? Platform { get; private set; }

    public string? Notes { get; private set; }
    public DateOnly? StartedOn { get; private set; }
    public DateOnly? FinishedOn { get; private set; }

    /// <summary>Cuántas veces la has vuelto a ver entera.</summary>
    public int RepeatCount { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Control de concurrencia optimista (xmin en PostgreSQL).</summary>
    public uint Version { get; set; }

    /// <summary>
    /// Cambia el progreso aplicando las reglas de la lista. Devuelve cuántos episodios o capítulos
    /// se han avanzado (0 si se ha retrocedido), para las estadísticas de actividad.
    /// </summary>
    public int SetProgress(int progress, int? total, DateOnly today)
    {
        if (progress < 0)
            throw new DomainException("progress", "negative", "El progreso no puede ser negativo.");
        if (total is > 0 && progress > total)
            throw new DomainException("progress", "out_of_range", $"Solo tiene {total}.");

        var advanced = Math.Max(0, progress - Progress);
        Progress = progress;

        if (total is > 0 && progress == total)
        {
            // Llegar al último episodio es terminarla.
            Complete(total, today);
        }
        else if (progress > 0 && Status != ListStatus.Current)
        {
            // Empezar algo pendiente, retomar algo en pausa o abandonado, o corregir un "completado".
            Status = ListStatus.Current;
            StartedOn ??= today;
        }
        return advanced;
    }

    /// <summary>"He visto otro episodio": el botón +1.</summary>
    public int Increment(int? total, DateOnly today) => SetProgress(Progress + 1, total, today);

    /// <summary>
    /// Volver a verla (o leerla) desde el principio. Es una acción aparte: bajar el progreso de una
    /// completada puede ser solo corregir un error, y eso no debe contar como otra vez.
    /// </summary>
    public void StartRewatch()
    {
        if (Status != ListStatus.Completed)
            throw new DomainException("status", "not_completed", "Solo se puede volver a ver algo que ya has terminado.");
        RepeatCount++;
        Progress = 0;
        Status = ListStatus.Current;
    }

    public void SetStatus(ListStatus status, int? total, DateOnly today)
    {
        switch (status)
        {
            case ListStatus.Completed:
                Complete(total, today);
                break;
            case ListStatus.Current:
                Status = ListStatus.Current;
                StartedOn ??= today;
                break;
            default:
                Status = status;
                break;
        }
    }

    public void SetScore(int? score)
    {
        if (score is < MinScore or > MaxScore)
            throw new DomainException("score", "out_of_range", $"La puntuación va de {MinScore} a {MaxScore}.");
        Score = score;
    }

    public void SetPlatform(string? platform)
    {
        var value = string.IsNullOrWhiteSpace(platform) ? null : platform.Trim();
        if (value?.Length > MaxPlatformLength)
            throw new DomainException("platform", "too_long", $"Como mucho {MaxPlatformLength} caracteres.");
        Platform = value;
    }

    public void SetNotes(string? notes)
    {
        var value = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (value?.Length > MaxNotesLength)
            throw new DomainException("notes", "too_long", $"Como mucho {MaxNotesLength} caracteres.");
        Notes = value;
    }

    public void SetDates(DateOnly? startedOn, DateOnly? finishedOn, DateOnly today)
    {
        if (startedOn > today || finishedOn > today)
            throw new DomainException("dates", "in_the_future", "Las fechas no pueden ser futuras.");
        if (startedOn is not null && finishedOn is not null && finishedOn < startedOn)
            throw new DomainException("finishedOn", "before_start", "No puedes terminarla antes de empezarla.");
        StartedOn = startedOn;
        FinishedOn = finishedOn;
    }

    private void Complete(int? total, DateOnly today)
    {
        Status = ListStatus.Completed;
        // Si se sabe el total, completarla es haberla visto entera.
        if (total is > 0) Progress = total.Value;
        StartedOn ??= today;
        FinishedOn ??= today;
    }
}

/// <summary>
/// Un avance de progreso (+N episodios o capítulos). Es lo que alimenta la actividad por meses:
/// el progreso actual por sí solo no dice cuándo se vio cada episodio.
/// </summary>
public class ProgressEvent
{
    public long Id { get; set; }
    public required string UserId { get; set; }
    public long EntryId { get; set; }
    public ListEntry? Entry { get; set; }
    public MediaType Type { get; set; }
    public int Amount { get; set; }
    public DateTimeOffset At { get; set; }
}
