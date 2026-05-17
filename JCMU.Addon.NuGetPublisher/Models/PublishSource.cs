namespace JinnDev.JCMU.Addon.NuGetPublisher.Models;

public record PublishSource
{
    public string Name { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string? ApiKey { get; init; }
    public bool IsDefault { get; init; }
}
