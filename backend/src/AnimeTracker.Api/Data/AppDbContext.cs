using AnimeTracker.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AnimeTracker.Api.Data;

public class AppUser : IdentityUser
{
    /// <summary>Nombre público (para /u/{nombre}); null hasta que el usuario elige uno.</summary>
    public string? ProfileName { get; set; }

    /// <summary>Si es false, nadie más puede ver la lista aunque sepa el nombre.</summary>
    public bool IsProfilePublic { get; set; }
}

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Media> Media => Set<Media>();
    public DbSet<ListEntry> ListEntries => Set<ListEntry>();
    public DbSet<ProgressEvent> ProgressEvents => Set<ProgressEvent>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);

        model.Entity<AppUser>(user =>
        {
            user.Property(u => u.ProfileName).HasMaxLength(30);
            // Se guarda en minúsculas (lo valida el endpoint), así "Jose" y "jose" no pueden ser dos perfiles.
            user.HasIndex(u => u.ProfileName).IsUnique().HasFilter("\"ProfileName\" IS NOT NULL");
            user.ToTable(t => t.HasCheckConstraint("ck_users_profile_name", "\"ProfileName\" IS NULL OR \"ProfileName\" ~ '^[a-z0-9_-]{3,30}$'"));
        });

        model.Entity<Media>(media =>
        {
            media.ToTable("media");
            // El id es el de AniList, no se genera.
            media.Property(m => m.Id).ValueGeneratedNever();
            media.Property(m => m.Type).HasConversion<string>().HasMaxLength(10);
            media.Property(m => m.Title).HasMaxLength(300);
            media.Property(m => m.TitleEnglish).HasMaxLength(300);
            media.Property(m => m.Format).HasMaxLength(20);
            media.Property(m => m.ReleaseStatus).HasMaxLength(20);
            media.Ignore(m => m.Total);
            media.HasIndex(m => m.Type);
        });

        model.Entity<ListEntry>(entry =>
        {
            entry.ToTable("list_entries", t =>
            {
                // Las reglas también en la base de datos: ni un bug ni un UPDATE a mano las saltan.
                t.HasCheckConstraint("ck_list_entries_progress", "\"Progress\" >= 0");
                t.HasCheckConstraint("ck_list_entries_score", "\"Score\" IS NULL OR \"Score\" BETWEEN 1 AND 10");
                t.HasCheckConstraint("ck_list_entries_dates", "\"FinishedOn\" IS NULL OR \"StartedOn\" IS NULL OR \"FinishedOn\" >= \"StartedOn\"");
            });
            entry.Property(e => e.Status).HasConversion<string>().HasMaxLength(12);
            entry.Property(e => e.Platform).HasMaxLength(ListEntry.MaxPlatformLength);
            entry.Property(e => e.Notes).HasMaxLength(ListEntry.MaxNotesLength);
            // xmin de PostgreSQL como token de concurrencia: dos "+1" a la vez no se pisan.
            entry.Property(e => e.Version).IsRowVersion();
            entry.HasOne(e => e.Media).WithMany().HasForeignKey(e => e.MediaId).OnDelete(DeleteBehavior.Restrict);
            entry.HasOne<AppUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            // Una obra solo puede estar una vez en tu lista.
            entry.HasIndex(e => new { e.UserId, e.MediaId }).IsUnique();
            entry.HasIndex(e => new { e.UserId, e.Status });
        });

        model.Entity<ProgressEvent>(evt =>
        {
            evt.ToTable("progress_events", t => t.HasCheckConstraint("ck_progress_events_amount", "\"Amount\" > 0"));
            evt.Property(e => e.Type).HasConversion<string>().HasMaxLength(10);
            evt.HasOne<AppUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            evt.HasOne(e => e.Entry).WithMany().HasForeignKey(e => e.EntryId).OnDelete(DeleteBehavior.Cascade);
            evt.HasIndex(e => new { e.UserId, e.At });
        });
    }
}
