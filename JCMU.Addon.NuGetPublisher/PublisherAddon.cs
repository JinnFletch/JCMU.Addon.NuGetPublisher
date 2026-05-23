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

        host.UI.WriteLine("==================================================", ConsoleColor.Cyan);
        host.UI.WriteLine("    JinnDev Publisher: Dynamic Discovery Mode     ", ConsoleColor.Cyan);
        host.UI.WriteLine("==================================================\n", ConsoleColor.Cyan);

        var result = await host.Settings.GetValueAsync<PublishConfig>("PublishConfig")
            .OrElseAsync(() => UserInteractionService.RunFirstTimeSetupAsync(host))
            .BindAsync(config => PrepareContextAsync(config, context.TargetDirectory, host))
            .BindAsync(ctx => UserInteractionService.ConfirmPlanAsync(ctx, host))
            .BindAsync(ctx => ProjectMetadataService.UpdateProjectVersion(ctx.ProjectPath, ctx.TargetVersion).WithValueAsync(ctx))
            .BindAsync(ctx => CommandLineExecutionService.ExecuteBuildAsync(ctx, host, runner))
            .BindAsync(ctx => CommandLineExecutionService.LocatePackage(ctx, host)
                .BindAsync(pkgPath => CommandLineExecutionService.ExecutePushAsync(pkgPath, ctx.SelectedSource, host, runner)
                    .WithValueAsync(() => ctx)))
            .BindAsync(ctx => GitIntegrationService.CommitAndPushVersionBumpAsync(ctx, runner, host))
            .ConfigureAwait(false);

        // Final Output Formatting
        host.UI.WriteLine("\n");
        return result.Match(
            some: _ =>
            {
                host.UI.WriteLine("**************************************************", ConsoleColor.Green);
                host.UI.WriteLine("      YOUR PACKAGE WAS PUSHED SUCCESSFULLY", ConsoleColor.Green);
                host.UI.WriteLine("**************************************************", ConsoleColor.Green);
                return Maybe.Some(-1); // -1 usually signifies waiting for user to close/auto-close
            },
            none: err =>
            {
                host.UI.WriteLine($"Publish Failed: {err.Message}", ConsoleColor.Red);
                return Maybe.None<int>(err.Message);
            });
    }

    private static async Task<Maybe<PublishContext>> PrepareContextAsync(PublishConfig config, string targetDirectory, IHostServices host)
    {
        // 1. Find Project -> Select Source -> Build Context (Pure Monadic Chain)
        return await ProjectDiscoveryService.DiscoverProjectAsync(targetDirectory, host)
            .BindAsync(projPath => UserInteractionService.SelectSourceAsync(config, host)
                .BindAsync(source => BuildContextAsync(projPath, source, host)))
            .ConfigureAwait(false);
    }

    private static async Task<Maybe<PublishContext>> BuildContextAsync(string projectPath, PublishSource source, IHostServices host)
    {
        // 1. Gather all required data linearly using Query Syntax
        var contextDataQuery =
            from packageId in Task.FromResult(ProjectMetadataService.GetPackageId(projectPath))
            from currentLocalVersion in Task.FromResult(ProjectMetadataService.GetCurrentVersion(projectPath))
            from highestRemoteVersion in NuGetFeedService.GetLatestVersionAsync(packageId, source, host)
            from targetVersion in UserInteractionService.PromptForTargetVersionAsync(currentLocalVersion, highestRemoteVersion, host)
            select new { packageId, currentLocalVersion, highestRemoteVersion, targetVersion };

        // 2. Await the query and validate the results
        return await contextDataQuery.BindAsync(data =>
        {
            // ==========================================================
            // VALIDATION GATE
            // ==========================================================
            if (data.highestRemoteVersion.Major != 0 && data.targetVersion <= data.highestRemoteVersion)
            {
                return Maybe.None<PublishContext>(
                    $"Validation Failed: Target version ({data.targetVersion}) must be greater than the Remote Latest version ({data.highestRemoteVersion}).");
            }

            // 3. Construct and return the final Context
            var outputDir = Path.Combine(Path.GetDirectoryName(projectPath)!, "bin", "Release");

            var ctx = new PublishContext(
                projectPath,
                data.packageId,
                outputDir,
                data.currentLocalVersion,
                data.highestRemoteVersion,
                data.targetVersion,
                source);

            return Maybe.Some(ctx);

        }).ConfigureAwait(false);
    }
}