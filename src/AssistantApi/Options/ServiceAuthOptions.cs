using System.ComponentModel.DataAnnotations;

namespace AssistantApi.Options;

public sealed class ServiceAuthOptions
{
    public const string SectionName = "Assistant";

    [Required]
    [MinLength(16)]
    public string ServiceKey { get; set; } = string.Empty;
}
