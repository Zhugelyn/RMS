namespace TelegramGateway.Options;

/// <summary>Shared research image volume for sendPhoto (ADR-011). Same path as assistant-api.</summary>
public sealed class ResearchImageOptions
{
    public const string SectionName = "Research";

    public string ImageVolumePath { get; set; } = string.Empty;

    public int MaxImageBytes { get; set; } = 8 * 1024 * 1024;

    public int MaxPhotosPerNotify { get; set; } = 14;
}
