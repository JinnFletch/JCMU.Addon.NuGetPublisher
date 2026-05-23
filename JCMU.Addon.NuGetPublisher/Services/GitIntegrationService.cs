using JinnDev.JCMU.Addon.NuGetPublisher.Models;
using JinnDev.JCMU.SDK.Interfaces;
using JinnDev.Utilities.CommandLine;
using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.Addon.NuGetPublisher.Services;

public static class GitIntegrationService
{
    /// <summary>
    /// Stages, commits, and attempts to push the .csproj version bump to keep the working tree clean.
    /// </summary>
    public static async Task<Maybe<PublishContext>> CommitAndPushVersionBumpAsync(
        PublishContext ctx,
        IStatelessRunner runner,
        IHostServices host)
    {
        host.UI.WriteLine("\n--- Step 4: Git Housekeeping ---", ConsoleColor.Cyan);

        var projectDir = Path.GetDirectoryName(ctx.ProjectPath)!;
        var fileName = Path.GetFileName(ctx.ProjectPath);

        // 1. Check if we are actually in a git repository
        var isGitRepoReq = CommandBuilder.Create("git")
            .WithArgument("rev-parse --is-inside-work-tree")
            .InDirectory(projectDir)
            .Build();

        await runner.RunBufferedAsync(isGitRepoReq)
            .EnsureSuccessAsync("  -> Not a Git repository. Skipping Git chore.")
            .BindAsync(async isGitRepo =>
            {
                host.UI.WriteLine($"  -> Staging and committing version bump for {fileName}...");

                var addReq = CommandBuilder.Create("git")
                    .WithArgument("add")
                    .WithQuotedArgument(fileName)
                    .InDirectory(projectDir)
                    .Build();

                var commitReq = CommandBuilder.Create("git")
                    .WithArgument("commit")
                    .WithArgument("-m")
                    .WithQuotedArgument($"chore: bump package version to {ctx.TargetVersion}")
                    .InDirectory(projectDir).Build();

                return await runner.RunBufferedAsync(addReq)
                    .EnsureSuccessAsync("Failed to stage .csproj change.")
                    .BindAsync(_ => runner.RunBufferedAsync(commitReq))
                    .EnsureSuccessAsync("Failed to commit version bump.")
                    .BindAsync(async _ =>
                    {
                        host.UI.WriteLine("  -> Pushing to remote...");
                        var pushReq = CommandBuilder.Create("git")
                            .WithArgument("push")
                            .InDirectory(projectDir)
                            .Build();

                        return await runner.RunBufferedAsync(pushReq)
                            .EnsureSuccessAsync("  -> [Warning] Could not push to remote. You may need to push manually.")
                            .TapAsync(_ => host.UI.WriteLine("  -> [✓] Git chore pushed successfully.", ConsoleColor.Green))
                            .ConfigureAwait(false);
                    }).ConfigureAwait(false);
            })
            .ConfigureAwait(false);

        return ctx;
    }
}