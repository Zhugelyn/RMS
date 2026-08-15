using System.ComponentModel.DataAnnotations;

namespace AssistantApi.Packs;

public sealed class AgentPacksOptions
{
    public const string SectionName = "AgentPacks";

    /// <summary>
    /// Absolute or content-root-relative path to AgentPacks directory.
    /// Empty = auto-resolve (output AgentPacks/, then ../AgentPacks from content root).
    /// </summary>
    [MaxLength(1024)]
    public string RootPath { get; set; } = string.Empty;

    /// <summary>
    /// When true, missing/invalid packs fail host startup.
    /// Phase 3 layout slice keeps default true for assistant-api once packs ship in image.
    /// </summary>
    public bool ValidateOnStart { get; set; } = true;
}
