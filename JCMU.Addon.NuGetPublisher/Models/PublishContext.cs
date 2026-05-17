namespace JinnDev.JCMU.Addon.NuGetPublisher.Models;

public record PublishContext(
    string ProjectPath,
    string PackageId,
    string OutputDir,
    Version TargetVersion,
    PublishSource SelectedSource);