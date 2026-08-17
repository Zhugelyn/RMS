namespace AssistantApi.Options;

/// <summary>Phase 4 research image generation via Cursor GenerateImage + local volume (ADR-011).</summary>
public sealed class ResearchOptions
{
    public const string SectionName = "Research";

    /// <summary>
    /// Shared Docker volume root for GenerateImage artifacts (e.g. /data/marketing-images).
    /// Empty → skip image generation; compose stays green without Cursor key / volume.
    /// Env: RESEARCH__IMAGEVOLUMEPATH
    /// </summary>
    public string ImageVolumePath { get; set; } = string.Empty;

    /// <summary>Max images per research run (14-day plan). Soft-capped at 14.</summary>
    public int ImageCap { get; set; } = 14;

    /// <summary>Max bytes per generated/downloaded image before Telegram send.</summary>
    public int MaxImageBytes { get; set; } = 8 * 1024 * 1024;

    public string PackId { get; set; } = "marketing";
}

public static class ResearchImageLimits
{
    public const int MaxImages = 14;
    public const string SoftFailErrorCode = "image-tool-missing";
    public const string RelativeMediaRoot = "research-media";
}
