using System.Text.Json.Serialization;

namespace JinnDev.JCMU.Addon.NuGetPublisher.Models;

public record PublishSource
{
    public string Name { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;

    [JsonIgnore] // Prevents the API key from writing to plain-text JSON
    public string? ApiKey { get; init; }
    public bool IsDefault { get; init; }
}
