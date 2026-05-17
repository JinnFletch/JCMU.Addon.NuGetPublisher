namespace JinnDev.JCMU.Addon.NuGetPublisher.Models;

public record PublishConfig
{
    public List<PublishSource> Sources { get; init; } = new();
}
