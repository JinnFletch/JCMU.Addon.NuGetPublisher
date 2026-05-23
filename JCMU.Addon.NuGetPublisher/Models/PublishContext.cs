namespace JinnDev.JCMU.Addon.NuGetPublisher.Models;

public record PublishContext(
    string ProjectPath,
    string PackageId,
    string OutputDir,
    Version CurrentLocalVersion,
    Version HighestRemoteVersion,
    Version TargetVersion,
    PublishSource SelectedSource);