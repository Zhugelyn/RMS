using System.ComponentModel.DataAnnotations;

namespace AssistantApi.Contracts;

public sealed class ChatRequest
{
    [Required]
    [Range(1, 1)]
    public int SchemaVersion { get; set; } = 1;

    [Required]
    [MaxLength(128)]
    public string ConversationId { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [MaxLength(4000)]
    public string Text { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string TraceId { get; set; } = string.Empty;

    public ChatIntent? Intent { get; set; }
}

public enum ChatIntent
{
    Salon,
    Marketing,
    Tasks,
    General
}

public sealed class ChatResponse
{
    public int SchemaVersion { get; set; } = 1;
    public string ConversationId { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Provider { get; set; } = "stub";
}
