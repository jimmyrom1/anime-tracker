using AnimeTracker.Api.Catalog;
using AnimeTracker.Api.Data;
using AnimeTracker.Domain;
using Microsoft.EntityFrameworkCore;

namespace AnimeTracker.Api.Lists;

public record EntryDto(
    long Id,
    MediaDto Media,
    ListStatus Status,
    int Progress,
    int? Score,
    string? Platform,
    string? Notes,
    DateOnly? StartedOn,
    DateOnly? FinishedOn,
    int RepeatCount,
    DateTimeOffset UpdatedAt)
{
    public static EntryDto From(ListEntry e) => new(
        e.Id, MediaDto.From(e.Media!), e.Status, e.Progress, e.Score, e.Platform, e.Notes,
        e.StartedOn, e.FinishedOn, e.RepeatCount, e.UpdatedAt);
}

public record MediaDto(
    int Id, MediaType Type, string Title, string? TitleEnglish, string? CoverUrl, int? Total,
    int? Episodes, int? Chapters, int? DurationMinutes, string? Format, string? ReleaseStatus, int? Year,
    IReadOnlyList<string> Genres, int? AverageScore)
{
    public static MediaDto From(Media m) => new(
        m.Id, m.Type, m.Title, m.TitleEnglish, m.CoverUrl, m.Total, m.Episodes, m.Chapters, m.DurationMinutes,
        m.Format, m.ReleaseStatus, m.Year, m.Genres, m.AverageScore);
}

/// <summary>Lo que se puede editar de una entrada; el formulario manda siempre todos los campos.</summary>
public record EntryUpdate(
    ListStatus Status,
    int Progress,
    int? Score,
    string? Platform,
    string? Notes,
    DateOnly? StartedOn,
    DateOnly? FinishedOn);

public class NotFoundException(string message) : Exception(message);

public class ConflictException(string message) : Exception(message);

public class ListService(AppDbContext db, CatalogService catalog, TimeProvider time, AppClock clock)
{
    public async Task<IReadOnlyList<EntryDto>> GetListAsync(string userId, MediaType type, ListStatus? status, CancellationToken ct)
    {
        var query = db.ListEntries.AsNoTracking().Include(e => e.Media)
            .Where(e => e.UserId == userId && e.Media!.Type == type);
        if (status is not null) query = query.Where(e => e.Status == status);
        var entries = await query.OrderByDescending(e => e.UpdatedAt).ToListAsync(ct);
        return entries.Select(EntryDto.From).ToList();
    }

    public async Task<EntryDto> AddAsync(string userId, int mediaId, ListStatus status, CancellationToken ct)
    {
        var media = await catalog.GetOrFetchAsync(mediaId, ct) ?? throw new NotFoundException("Esa obra no existe en AniList.");
        if (await db.ListEntries.AnyAsync(e => e.UserId == userId && e.MediaId == mediaId, ct))
            throw new ConflictException("Ya la tienes en tu lista.");

        var now = time.GetUtcNow();
        var entry = new ListEntry { UserId = userId, MediaId = media.Id, Media = media, CreatedAt = now, UpdatedAt = now };
        entry.SetStatus(status, media.Total, clock.Today);
        // Sin evento de progreso: añadir algo que ya viste (p. ej. como completado) es catalogar tu
        // historial, no actividad de hoy. Si no, meter 50 animes vistos llenaría la gráfica de este mes.
        db.ListEntries.Add(entry);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // Dos "añadir" a la vez: el índice único deja pasar solo uno.
            throw new ConflictException("Ya la tienes en tu lista.");
        }
        return EntryDto.From(entry);
    }

    public async Task<EntryDto> UpdateAsync(string userId, long id, EntryUpdate update, CancellationToken ct)
    {
        var entry = await LoadAsync(userId, id, ct);
        var total = entry.Media!.Total;
        var today = clock.Today;

        // Si solo cambia el progreso, se aplican las transiciones automáticas (último episodio =
        // completada). Si el usuario cambia el estado a mano, su elección manda.
        var statusChanged = update.Status != entry.Status;
        var advanced = update.Progress != entry.Progress ? entry.SetProgress(update.Progress, total, today) : 0;
        if (statusChanged) entry.SetStatus(update.Status, total, today);
        entry.SetScore(update.Score);
        entry.SetPlatform(update.Platform);
        entry.SetNotes(update.Notes);
        entry.SetDates(update.StartedOn ?? entry.StartedOn, update.FinishedOn ?? entry.FinishedOn, today);
        entry.UpdatedAt = time.GetUtcNow();
        AddProgressEvent(entry, advanced);

        await SaveAsync(ct);
        return EntryDto.From(entry);
    }

    /// <summary>
    /// "+1 episodio". La fila se bloquea con SELECT ... FOR UPDATE: si llegan varios +1 a la vez
    /// (el móvil y el ordenador, un doble toque), PostgreSQL los pone en fila y cada uno lee el
    /// progreso que dejó el anterior. Ninguno se pierde y ninguno falla.
    ///
    /// Primero se probó con concurrencia optimista y reintentos, pero con 10 +1 simultáneos algunos
    /// agotaban los reintentos (en la CI, con más paralelismo real que en local).
    /// </summary>
    public async Task<EntryDto> IncrementAsync(string userId, long id, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // xmin hay que pedirlo aparte: "SELECT *" no incluye las columnas de sistema de PostgreSQL.
        var entry = await db.ListEntries
            .FromSql($"SELECT *, xmin FROM list_entries WHERE \"Id\" = {id} AND \"UserId\" = {userId} FOR UPDATE")
            .SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Esa entrada no está en tu lista.");
        await db.Entry(entry).Reference(e => e.Media).LoadAsync(ct);

        var advanced = entry.Increment(entry.Media!.Total, clock.Today);
        entry.UpdatedAt = time.GetUtcNow();
        AddProgressEvent(entry, advanced);
        // La entrada y su evento, en la misma transacción que el bloqueo.
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return EntryDto.From(entry);
    }

    public async Task<EntryDto> StartRewatchAsync(string userId, long id, CancellationToken ct)
    {
        var entry = await LoadAsync(userId, id, ct);
        entry.StartRewatch();
        entry.UpdatedAt = time.GetUtcNow();
        await SaveAsync(ct);
        return EntryDto.From(entry);
    }

    public async Task DeleteAsync(string userId, long id, CancellationToken ct)
    {
        var deleted = await db.ListEntries.Where(e => e.UserId == userId && e.Id == id).ExecuteDeleteAsync(ct);
        if (deleted == 0) throw new NotFoundException("Esa entrada no está en tu lista.");
    }

    public async Task<ListStats> StatsAsync(string userId, MediaType type, CancellationToken ct)
    {
        var entries = await db.ListEntries.AsNoTracking().Include(e => e.Media)
            .Where(e => e.UserId == userId && e.Media!.Type == type).ToListAsync(ct);
        var since = time.GetUtcNow().AddMonths(-12);
        var events = await db.ProgressEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.Type == type && e.At >= since).ToListAsync(ct);
        return StatsCalculator.Compute(type, entries, events, activityMonths: 12, clock.Today);
    }

    /// <summary>La entrada tiene que ser del usuario: la de otro da 404, igual que si no existiera.</summary>
    private async Task<ListEntry> LoadAsync(string userId, long id, CancellationToken ct) =>
        await db.ListEntries.Include(e => e.Media).FirstOrDefaultAsync(e => e.UserId == userId && e.Id == id, ct)
        ?? throw new NotFoundException("Esa entrada no está en tu lista.");

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("La has cambiado desde otro sitio; recarga y vuelve a intentarlo.");
        }
    }

    /// <summary>Se añade al contexto antes de guardar: entrada y evento van en la misma transacción.</summary>
    private void AddProgressEvent(ListEntry entry, int amount)
    {
        if (amount <= 0) return;
        db.ProgressEvents.Add(new ProgressEvent
        {
            UserId = entry.UserId, Entry = entry, Type = entry.Media!.Type, Amount = amount, At = time.GetUtcNow(),
        });
    }
}
