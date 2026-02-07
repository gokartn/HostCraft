using System.Text.Json.Serialization;

namespace HostCraft.Web.Models;

/// <summary>
/// Mirrors the API's standardized error payload shape.
/// </summary>
public class ApiError
{
    [JsonPropertyName("status")]
    public int? Status { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("errors")]
    public Dictionary<string, string[]>? Errors { get; set; }

    [JsonPropertyName("traceId")]
    public string? TraceId { get; set; }

    [JsonPropertyName("instance")]
    public string? Instance { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}
