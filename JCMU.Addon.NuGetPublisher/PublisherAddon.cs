using JinnDev.JCMU.Addon.NuGetPublisher.Models;
using JinnDev.JCMU.Addon.NuGetPublisher.Services;
using JinnDev.JCMU.SDK.Interfaces;
using JinnDev.JCMU.SDK.Models;
using JinnDev.Utilities.CommandLine;
using JinnDev.Utilities.Monad;

namespace JCMU.Addon.NuGetPublisher;

public class PublisherAddon : IJcmuAddon
{
    public async Task<Maybe<int>> ExecuteAsync(ActionContext context)
    {
        var host = context.HostServices;
        var runner = new StatelessRunner();

        host.Logger.LogInfo("==================================================");
        host.Logger.LogInfo("    JinnDev Publisher: Dynamic Discovery Mode     ");
        host.Logger.LogInfo("==================================================\n");


        var result = await host.Settings.GetValueAsync<PublishConfig>("PublishConfig")
            .OrElseAsync(() => UserInteractionService.RunFirstTimeSetupAsync(host))
            .BindAsync(config => PrepareContextAsync(config, context.TargetDirectory, host))
            .BindAsync(ctx => UserInteractionService.ConfirmPlanAsync(ctx, host))
            .BindAsync(ctx => ProjectMetadataService.UpdateProjectVersion(ctx.ProjectPath, ctx.TargetVersion).WithValueAsync(ctx))
            .BindAsync(ctx => CommandLineExecutionService.ExecuteBuildAsync(ctx, host, runner))
            .BindAsync(ctx => CommandLineExecutionService.LocatePackage(ctx, host)
                .BindAsync(pkgPath => CommandLineExecutionService.ExecutePushAsync(pkgPath, ctx.SelectedSource, host, runner)))
            .ConfigureAwait(false);

        // Final Output Unwrap
        host.Logger.LogInfo("\n");
        if (result.HasValue)
        {
            host.Logger.LogInfo("**************************************************");
            host.Logger.LogInfo("      YOUR PACKAGE WAS PUSHED SUCCESSFULLY");
            host.Logger.LogInfo("**************************************************");
        }

        return result.WithValue(-1);
    }

    private static async Task<Maybe<PublishContext>> PrepareContextAsync(PublishConfig config, string targetDirectory, IHostServices host)
    {
        // 1. Flattened asynchronous execution (no pyramid of doom)
        var projRes = await ProjectDiscoveryService.DiscoverProjectAsync(targetDirectory, host).ConfigureAwait(false);
        if (!projRes.HasValue) return Maybe.PropagateFailure<PublishContext, Maybe<string>>(projRes);

        var sourceRes = await UserInteractionService.SelectSourceAsync(config, host).ConfigureAwait(false);
        if (!sourceRes.HasValue) return Maybe.PropagateFailure<PublishContext, Maybe<PublishSource>>(sourceRes);

        // 2. Safe unwrapping using Bind to pass the values into the next step without ever touching .Value
        return await projRes.BindAsync(projPath =>
            sourceRes.BindAsync(source =>
                BuildContextAsync(projPath, source, host)
            )
        ).ConfigureAwait(false);
    }

    private static async Task<Maybe<PublishContext>> BuildContextAsync(string projectPath, PublishSource source, IHostServices host)
    {
        return await ProjectMetadataService.GetPackageId(projectPath)
            .BindAsync(packageId => ProjectMetadataService.GetCurrentVersion(projectPath)
                .BindAsync(async currentVersion =>
                {
                    var nextVersion = new Version(currentVersion.Major, currentVersion.Minor, currentVersion.Build + 1);

                    host.Logger.LogInfo($"\n--- Project Identity ---");
                    host.Logger.LogInfo($"Target Project: {Path.GetFileNameWithoutExtension(projectPath)}");
                    host.Logger.LogInfo($"Package ID:     {packageId}");
                    host.Logger.LogInfo($"Destination:    {source.Name} ({source.Url})");
                    host.Logger.LogInfo($"Current Version: {currentVersion}");

                    var inputResult = await host.PromptUserAsync($"Target Version [{nextVersion}]:").ConfigureAwait(false);

                    // Match cleanly handles both valid user input and 'None' 
                    // (which happens if they just press Enter for the default)
                    return inputResult.Match(
                        some: input =>
                        {
                            if (Version.TryParse(input, out var v)) return Maybe.Some(v);
                            return Maybe.None<Version>("Invalid version format.");
                        },
                        none: err => Maybe.Some(nextVersion)
                    ).Map(finalVersion =>
                    {
                        var outputDir = Path.Combine(Path.GetDirectoryName(projectPath)!, "bin", "Release");
                        return new PublishContext(projectPath, packageId, outputDir, finalVersion, source);
                    });
                })
            ).ConfigureAwait(false);
    }
}