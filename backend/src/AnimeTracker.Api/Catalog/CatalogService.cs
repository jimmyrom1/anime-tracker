using AnimeTracker.Api.AniList;
using AnimeTracker.Api.Data;
using AnimeTracker.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AnimeTracker.Api.Catalog;

/// <summary>
/// Catálogo de anime y manga. AniList es la fuente, pero cada ficha se guarda en PostgreSQL: la
/// lista de un usuario se sirve sin llamar a AniList, y una obra añadida por mil usuarios se
/// descarga una vez.
/// </summary>
public class CatalogService(AppDbContext db, AniListClient aniList, IMemoryCache cache, TimeProvider time)
{
    /// <summary>Las búsquedas se repiten mucho mientras se escribe: 10 minutos en memoria.</summary>
    public static readonly TimeSpan SearchCacheDuration = TimeSpan.FromMinutes(10);

    /// <summary>Una serie en emisión gana episodios cada semana; una terminada no cambia.</summary>
    public static readonly TimeSpan AiringRefreshAfter = TimeSpan.FromHours(12);

    public async Task<IReadOnlyList<Media>> SearchAsync(string query, MediaType type, CancellationToken ct)
    {
        var key = $"search:{type}:{query.Trim().ToLowerInvariant()}";
        if (cache.TryGetValue(key, out IReadOnlyList<Media>? cached) && cached is not null) return cached;

        var results = await aniList.SearchAsync(query.Trim(), type, ct);
        cache.Set(key, results, SearchCacheDuration);
        return results;
    }

    /// <summary>
    /// La ficha guardada; si no existe o está en emisión y es antigua, se pide a AniList.
    /// Si AniList no responde pero hay una copia, se usa la copia.
    /// </summary>
    public async Task<Media?> GetOrFetchAsync(int id, CancellationToken ct)
    {
        var stored = await db.Media.FirstOrDefaultAsync(m => m.Id == id, ct);
        var now = time.GetUtcNow();
        var stale = stored is not null && stored.ReleaseStatus is "RELEASING" or "NOT_YET_RELEASED"
                                       && now - stored.FetchedAt > AiringRefreshAfter;
        if (stored is not null && !stale) return stored;

        Media? fresh;
        try
        {
            fresh = await aniList.GetAsync(id, ct);
        }
        catch (Exception e) when (stored is not null && e is AniListUnavailableException or HttpRequestException)
        {
            return stored;
        }
        if (fresh is null) return stored;

        fresh.FetchedAt = now;
        if (stored is null)
        {
            db.Media.Add(fresh);
        }
        else
        {
            db.Entry(stored).CurrentValues.SetValues(fresh);
            stored.Genres = fresh.Genres;
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (stored is null && e.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // Dos peticiones han descargado la misma obra a la vez y la otra la ha guardado antes:
            // se usa la suya.
            db.Entry(fresh).State = EntityState.Detached;
            return await db.Media.FirstAsync(m => m.Id == id, ct);
        }
        return stored ?? fresh;
    }
}
