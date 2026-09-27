using System.Security.Claims;
using System.Text.RegularExpressions;
using AnimeTracker.Api.Catalog;
using AnimeTracker.Api.Data;
using AnimeTracker.Api.Lists;
using AnimeTracker.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AnimeTracker.Api;

public record SearchResult(MediaDto Media, long? EntryId, ListStatus? Status);
public record AddEntryRequest(int MediaId, ListStatus Status = ListStatus.Planning);
public record MeResponse(string Email, string? ProfileName, bool IsProfilePublic);
public record ProfileUpdate(string? ProfileName, bool IsProfilePublic);
public record PublicProfile(string ProfileName, IReadOnlyList<EntryDto> Entries, ListStats Stats);

public static partial class Endpoints
{
    [GeneratedRegex("^[a-z0-9_-]{3,30}$")]
    private static partial Regex ProfileNamePattern();

    private static string UserId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public static void MapAppEndpoints(this WebApplication app)
    {
        app.MapGet("/api/health", async (AppDbContext db, CancellationToken ct) =>
            await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ok" }) : Results.StatusCode(503))
            .WithTags("sistema");

        app.MapGroup("/api/auth").MapIdentityApi<AppUser>().WithTags("cuenta");

        var api = app.MapGroup("/api").RequireAuthorization();

        api.MapGet("/me", async (ClaimsPrincipal principal, UserManager<AppUser> users) =>
        {
            var user = await users.GetUserAsync(principal);
            return user is null ? Results.Unauthorized() : Results.Ok(new MeResponse(user.Email!, user.ProfileName, user.IsProfilePublic));
        }).WithTags("cuenta");

        api.MapPut("/me/profile", async (ProfileUpdate body, ClaimsPrincipal principal, UserManager<AppUser> users, AppDbContext db, CancellationToken ct) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();
            var name = body.ProfileName?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(name)) name = null;
            if (name is not null && !ProfileNamePattern().IsMatch(name))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["profileName"] = ["De 3 a 30 caracteres: letras minúsculas, números, - y _."],
                });
            if (body.IsProfilePublic && name is null)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["profileName"] = ["Para hacer público el perfil necesita un nombre."] });
            if (name is not null && await db.Users.AnyAsync(u => u.ProfileName == name && u.Id != user.Id, ct))
                return Results.Conflict(new { error = "profile_name_taken", message = "Ese nombre ya está cogido." });

            user.ProfileName = name;
            user.IsProfilePublic = body.IsProfilePublic;
            await users.UpdateAsync(user);
            return Results.Ok(new MeResponse(user.Email!, user.ProfileName, user.IsProfilePublic));
        }).WithTags("cuenta");

        var catalog = api.MapGroup("/catalog").WithTags("catálogo");
        catalog.MapGet("/search", async (string q, MediaType type, ClaimsPrincipal principal, CatalogService service, AppDbContext db, CancellationToken ct) =>
        {
            if (q.Trim().Length < 2)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["q"] = ["Escribe al menos 2 letras."] });
            var results = await service.SearchAsync(q, type, ct);
            // Se marca lo que ya está en tu lista para no ofrecer "añadir" dos veces.
            var ids = results.Select(m => m.Id).ToList();
            var userId = UserId(principal);
            var mine = await db.ListEntries.AsNoTracking()
                .Where(e => e.UserId == userId && ids.Contains(e.MediaId))
                .Select(e => new { e.MediaId, e.Id, e.Status })
                .ToDictionaryAsync(e => e.MediaId, ct);
            return Results.Ok(results.Select(m => new SearchResult(MediaDto.From(m), mine.GetValueOrDefault(m.Id)?.Id, mine.GetValueOrDefault(m.Id)?.Status)));
        });
        catalog.MapGet("/{id:int}", async (int id, CatalogService service, CancellationToken ct) =>
            await service.GetOrFetchAsync(id, ct) is { } media ? Results.Ok(MediaDto.From(media)) : Results.NotFound());

        var list = api.MapGroup("/list").WithTags("lista");
        list.MapGet("", (MediaType type, ListStatus? status, ClaimsPrincipal user, ListService service, CancellationToken ct) =>
            service.GetListAsync(UserId(user), type, status, ct));
        list.MapPost("", async (AddEntryRequest body, ClaimsPrincipal user, ListService service, CancellationToken ct) =>
        {
            var entry = await service.AddAsync(UserId(user), body.MediaId, body.Status, ct);
            return Results.Created($"/api/list/{entry.Id}", entry);
        });
        list.MapPut("/{id:long}", (long id, EntryUpdate body, ClaimsPrincipal user, ListService service, CancellationToken ct) =>
            service.UpdateAsync(UserId(user), id, body, ct));
        list.MapPost("/{id:long}/increment", (long id, ClaimsPrincipal user, ListService service, CancellationToken ct) =>
            service.IncrementAsync(UserId(user), id, ct));
        list.MapPost("/{id:long}/rewatch", (long id, ClaimsPrincipal user, ListService service, CancellationToken ct) =>
            service.StartRewatchAsync(UserId(user), id, ct));
        list.MapDelete("/{id:long}", async (long id, ClaimsPrincipal user, ListService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(UserId(user), id, ct);
            return Results.NoContent();
        });

        api.MapGet("/stats", (MediaType type, ClaimsPrincipal user, ListService service, CancellationToken ct) =>
            service.StatsAsync(UserId(user), type, ct)).WithTags("estadísticas");

        // Perfil público: sin login. Si no existe o no es público, 404 en ambos casos (no se revela cuál).
        app.MapGet("/api/profiles/{name}", async (string name, MediaType type, AppDbContext db, ListService service, CancellationToken ct) =>
        {
            var normalized = name.Trim().ToLowerInvariant();
            var owner = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.ProfileName == normalized && u.IsProfilePublic, ct);
            if (owner is null) return Results.NotFound();
            var entries = await service.GetListAsync(owner.Id, type, null, ct);
            var stats = await service.StatsAsync(owner.Id, type, ct);
            // Las notas son personales: no salen en el perfil público.
            return Results.Ok(new PublicProfile(owner.ProfileName!, entries.Select(e => e with { Notes = null }).ToList(), stats));
        }).WithTags("perfil público");
    }
}
