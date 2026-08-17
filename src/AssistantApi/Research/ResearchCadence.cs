namespace AssistantApi.Research;

/// <summary>14-day (default) research schedule math. No Hangfire.</summary>
public static class ResearchCadence
{
    public const int DefaultDays = ResearchSettingsDefaults.DefaultCadenceDays;

    public static int NormalizeDays(int cadenceDays) =>
        cadenceDays <= 0 ? DefaultDays : Math.Clamp(cadenceDays, 1, 90);

    /// <summary>
    /// Idempotency period key for a due window (UTC date of the scheduled NextRunAt).
    /// Combined with userId in the run store → one successful capture per window.
    /// </summary>
    public static string PeriodKey(DateTimeOffset windowDueAt) =>
        windowDueAt.UtcDateTime.ToString("yyyy-MM-dd");

    public static DateTimeOffset AdvanceNextRun(DateTimeOffset dueAt, int cadenceDays) =>
        dueAt.AddDays(NormalizeDays(cadenceDays));

    public static bool IsDue(ResearchSettings settings, DateTimeOffset now) =>
        settings.Enabled
        && settings.NextRunAt is { } due
        && due <= now;
}
