using JinnDev.JCMU.Addon.NuGetPublisher.Models;
using JinnDev.JCMU.SDK.Interfaces;
using JinnDev.Utilities.CommandLine;
using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.Addon.NuGetPublisher.Services;

public static class CommandLineExecutionService
{
    /// <summary>
    /// Executes the dotnet build command, streaming the output to the logger in real-time.
    /// </summary>
    public static async Task<Maybe<PublishContext>> ExecuteBuildAsync(PublishContext ctx, IHostServices host, IStatelessRunner runner)
    {
        host.UI.WriteLine("\n--- Step 1: Cleaning & Building (Release) ---", ConsoleColor.Cyan);

        var request = CommandBuilder.Create("dotnet")
            .WithArgument("build")
            .WithQuotedArgument(ctx.ProjectPath)
            .WithArgument("-c Release")
            .Build();

        return await Maybe.TryAsync<PublishContext>(async () =>
        {
            // Stream the output as it arrives so the user knows it hasn't frozen
            await foreach (var line in runner.StreamAsync(request).ConfigureAwait(false))
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    host.UI.WriteLine($"  [build] {line}", ConsoleColor.DarkGray);
                }
            }

            return ctx;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Locates the generated .nupkg file in the output directory using fuzzy matching.
    /// </summary>
    public static Maybe<string> LocatePackage(PublishContext ctx, IHostServices host)
    {
        host.UI.WriteLine("\n--- Step 2: Locating Package ---", ConsoleColor.Cyan);

        return Maybe.Try<string>(() =>
        {
            if (!Directory.Exists(ctx.OutputDir))
                throw new Exception($"Output directory not found: {ctx.OutputDir}");

            string expectedFileName = $"{ctx.PackageId}.{ctx.TargetVersion}.nupkg";
            string fullPath = Path.Combine(ctx.OutputDir, expectedFileName);

            if (File.Exists(fullPath))
            {
                host.UI.WriteLine($"[✓] Found package: {expectedFileName}", ConsoleColor.Green);
                return fullPath;
            }

            // Fallback for normalized versions (e.g. 1.0.0.0 -> 1.0.0)
            var package = new DirectoryInfo(ctx.OutputDir)
                .GetFiles($"{ctx.PackageId}*.nupkg")
                .Where(f => f.Name.Contains(ctx.TargetVersion.ToString()))
                .OrderByDescending(f => f.LastWriteTime)
                .FirstOrDefault();

            if (package == null)
                throw new Exception($"Could not locate {expectedFileName} or any recent match.");

            host.UI.WriteLine($"[✓] Found fuzzy match: {package.Name}", ConsoleColor.Green);
            return package.FullName;
        });
    }

    /// <summary>
    /// Executes the nuget push command.
    /// </summary>
    public static async Task<Maybe> ExecutePushAsync(string packagePath, PublishSource source, IHostServices host, IStatelessRunner runner)
    {
        host.UI.WriteLine("\n--- Step 3: Pushing to Destination ---", ConsoleColor.Cyan);

        var requestBuilder = CommandBuilder.Create("dotnet")
            .WithArgument("nuget push")
            .WithQuotedArgument(packagePath)
            .WithArgument("--source")
            .WithQuotedArgument(source.Url);

        if (!string.IsNullOrWhiteSpace(source.ApiKey))
        {
            requestBuilder.WithArgument("--api-key").WithArgument(source.ApiKey);
        }

        var request = requestBuilder.Build();

        var safePrintUrl = string.IsNullOrWhiteSpace(source.ApiKey) ? source.Url : $"{source.Url} --api-key ***";
        host.UI.WriteLine($"Executing: dotnet nuget push \"{Path.GetFileName(packagePath)}\" --source {safePrintUrl}");

        return await runner.RunBufferedAsync(request)
            .BindAsync(result =>
            {
                if (result.ExitCode == 0) return Maybe.SUCCESS;

                var errorMsg = string.IsNullOrWhiteSpace(result.StandardError)
                    ? result.StandardOutput
                    : result.StandardError;

                return Maybe.Fail($"Push failed with code {result.ExitCode}: {errorMsg}");
            })
            .ConfigureAwait(false);
    }
}