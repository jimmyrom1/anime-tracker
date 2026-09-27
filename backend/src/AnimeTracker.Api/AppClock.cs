namespace AnimeTracker.Api;

/// <summary>
/// "Hoy" en la zona horaria de la app (Europe/Madrid por defecto), no en la del servidor: un
/// episodio visto a las 00:30 en España es de ese día aunque el servidor vaya en UTC.
/// </summary>
public class AppClock(TimeProvider time, IConfiguration config)
{
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(config["App:TimeZone"] ?? "Europe/Madrid");

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), _zone).DateTime);
}
