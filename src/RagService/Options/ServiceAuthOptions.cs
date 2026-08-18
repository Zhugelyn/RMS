using System.ComponentModel.DataAnnotations;

namespace RagService.Options;

public sealed class ServiceAuthOptions
{
    public const string SectionName = "Rag";

    [Required]
    [MinLength(16)]
    public string ServiceKey { get; set; } = string.Empty;
}
