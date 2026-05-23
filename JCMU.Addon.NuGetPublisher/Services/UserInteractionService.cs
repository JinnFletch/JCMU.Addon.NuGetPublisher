using JinnDev.JCMU.Addon.NuGetPublisher.Models;
using JinnDev.JCMU.SDK.Interfaces;
using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.Addon.NuGetPublisher.Services;

public static class UserInteractionService
{
    public static async Task<Maybe<PublishConfig>> RunFirstTimeSetupAsync(IHostServices host)
    {
        host.UI.WriteLine("=== First Time Setup ===", ConsoleColor.Cyan);
        host.UI.WriteLine("It looks like this is your first time publishing a package with JCMU.");

        return await PromptForNewSourceAsync(host, isDefault: true)
            .BindAsync(newSource =>
            {
                var config = new PublishConfig { Sources = new List<PublishSource> { newSource } };

                return host.Settings.SetValueAsync("PublishConfig", config)
                    .BindAsync(_ => string.IsNullOrWhiteSpace(newSource.ApiKey)
                        ? Task.FromResult(Maybe.SUCCESS) // No API Key, just move on
                        : host.Settings.SetSecretAsync($"ApiKey_{newSource.Name}", newSource.ApiKey))
                    .TapAsync(_ => {
                        host.UI.WriteLine("Configuration saved successfully!\n", ConsoleColor.Green);
                        return Task.CompletedTask;
                    })
                    .WithValueAsync(() => config);
            }).ConfigureAwait(false);
    }

    public static async Task<Maybe<PublishSource>> SelectSourceAsync(PublishConfig config, IHostServices host)
    {
        var defaultSource = config.Sources.First(x => x.IsDefault);

        host.UI.WriteLine("\nAvailable Publish Sources:", ConsoleColor.Cyan);
        for (int i = 0; i < config.Sources.Count; i++)
        {
            var s = config.Sources[i];
            var suffix = s.IsDefault ? " [DEFAULT]" : "";
            host.UI.WriteLine($"{i + 1}. {s.Name} ({s.Url}){suffix}");
        }

        int addOptionIndex = config.Sources.Count + 1;
        host.UI.WriteLine($"{addOptionIndex}. Add a new NuGet package source");

        var inputResult = await host.PromptUserAsync($"\nSelect Source (1-{addOptionIndex}) or Enter for Default:").ConfigureAwait(false);

        return await inputResult.MatchAsync(
            someAsync: async input =>
            {
                if (int.TryParse(input, out int choice))
                {
                    if (choice > 0 && choice <= config.Sources.Count)
                    {
                        var selected = config.Sources[choice - 1];
                        return await LoadApiKeyAsync(selected, host).ConfigureAwait(false);
                    }

                    if (choice == addOptionIndex)
                        return await AddAndSaveNewSourceAsync(config, host).ConfigureAwait(false);
                }

                return Maybe.None<PublishSource>("Invalid source selection.");
            },
            noneAsync: async err => await LoadApiKeyAsync(defaultSource, host).ConfigureAwait(false)
        ).ConfigureAwait(false);
    }

    /// <summary>
    /// Re-hydrates the ApiKey from the secure DPAPI vault into memory since it was ignored by JSON.
    /// </summary>
    private static async Task<Maybe<PublishSource>> LoadApiKeyAsync(PublishSource source, IHostServices host)
    {
        var secret = await host.Settings.GetSecretAsync($"ApiKey_{source.Name}").ConfigureAwait(false);

        // Rebuild the immutable record, popping the API Key back in if it exists
        return Maybe.Some(source with { ApiKey = secret.Match(some: k => k, none: err => null!) });
    }

    private static async Task<Maybe<PublishSource>> AddAndSaveNewSourceAsync(PublishConfig config, IHostServices host)
    {
        return await PromptForNewSourceAsync(host, isDefault: false)
            .BindAsync(newSource =>
            {
                config.Sources.Add(newSource);

                return host.Settings.SetValueAsync("PublishConfig", config)
                    .BindAsync(_ => string.IsNullOrWhiteSpace(newSource.ApiKey)
                        ? Task.FromResult(Maybe.SUCCESS)
                        : host.Settings.SetSecretAsync($"ApiKey_{newSource.Name}", newSource.ApiKey))
                    .TapAsync(_ => {
                        host.UI.WriteLine($"\nSource '{newSource.Name}' saved successfully!", ConsoleColor.Green);
                        return Task.CompletedTask; })
                    .WithValueAsync(() => newSource);
            }).ConfigureAwait(false);
    }

    /// <summary>
    /// Helper to gather the 3 pieces of data needed for a new source.
    /// </summary>
    private static async Task<Maybe<PublishSource>> PromptForNewSourceAsync(IHostServices host, bool isDefault)
    {
        // 1. Get the inputs sequentially
        var nameRes = await host.PromptUserAsync("Enter a Name/Label for this source (e.g., Local Folder, NuGet.org):").ConfigureAwait(false);
        if (!nameRes.HasValue) return Maybe.PropagateFailure<PublishSource, Maybe<string>>(nameRes);

        var urlRes = await host.PromptUserAsync("Enter the URL or Folder Path (e.g., https://api.nuget.org/v3/index.json):").ConfigureAwait(false);
        if (!urlRes.HasValue) return Maybe.PropagateFailure<PublishSource, Maybe<string>>(urlRes);

        var apiRes = await host.PromptUserAsync("Enter your API Key (Leave blank if publishing to a local folder):").ConfigureAwait(false);
        var apiKey = apiRes.Match(some: val => val, none: err => string.Empty);

        // 2. Safely unwrap and assemble using a flat Bind/Map
        return nameRes.Bind(name =>
               urlRes.Map(url => new PublishSource
               {
                   Name = name,
                   Url = url,
                   ApiKey = apiKey,
                   IsDefault = isDefault
               }));
    }

    /// <summary>
    /// Analyzes the local vs remote versions and intelligently prompts the user for the target version.
    /// </summary>
    public static async Task<Maybe<Version>> PromptForTargetVersionAsync(
        Version localVersion,
        Version remoteVersion,
        IHostServices host)
    {
        Version defaultSuggestion;

        host.UI.WriteLine("\n--- Version Analysis ---", ConsoleColor.Cyan);
        host.UI.WriteLine($"Local .csproj:  {localVersion}");

        if (remoteVersion.Major == 0 && remoteVersion.Minor == 0)
        {
            host.UI.WriteLine("Remote Server:  [Never Published]");
            defaultSuggestion = localVersion;
        }
        else
        {
            host.UI.WriteLine($"Remote Server:  {remoteVersion}");

            if (localVersion <= remoteVersion)
            {
                host.UI.WriteLine("⚠️ WARNING: Your local .csproj version is equal to or behind the remote server.", ConsoleColor.Yellow);
                defaultSuggestion = new Version(remoteVersion.Major, remoteVersion.Minor, remoteVersion.Build + 1);
            }
            else
            {
                host.UI.WriteLine("✓ Local version is ahead of remote. Ready to publish.", ConsoleColor.Green);
                defaultSuggestion = localVersion;
            }
        }

        var inputResult = await host.PromptUserAsync($"\nTarget Version [{defaultSuggestion}]:").ConfigureAwait(false);

        return inputResult.Match(
            some: input =>
            {
                if (Version.TryParse(input, out var v)) return Maybe.Some(v);
                return Maybe.None<Version>("Invalid version format.");
            },
            none: err => Maybe.Some(defaultSuggestion)
        );
    }

    /// <summary>
    /// Presents the final plan to the user for confirmation before mutating files or pushing.
    /// </summary>
    public static async Task<Maybe<PublishContext>> ConfirmPlanAsync(PublishContext ctx, IHostServices host)
    {
        host.UI.WriteLine("\n================== PUBLISH PLAN ==================", ConsoleColor.Cyan);

        host.UI.Write("Package:        ");
        host.UI.WriteLine($"{ctx.PackageId}", ConsoleColor.White);

        host.UI.Write("Destination:    ");
        host.UI.WriteLine($"{ctx.SelectedSource.Name}", ConsoleColor.White);
        host.UI.WriteLine($"                ({ctx.SelectedSource.Url})\n", ConsoleColor.DarkGray);

        host.UI.WriteLine($"Remote Latest:  {(ctx.HighestRemoteVersion.Major == 0 ? "None" : ctx.HighestRemoteVersion)}");

        var localWarn = ctx.CurrentLocalVersion <= ctx.HighestRemoteVersion ? " <-- (Out of sync!)" : "";
        host.UI.Write("Local .csproj:  ");
        host.UI.WriteLine($"{ctx.CurrentLocalVersion}{localWarn}", ctx.CurrentLocalVersion <= ctx.HighestRemoteVersion ? ConsoleColor.Yellow : null);

        var mutateWarn = ctx.CurrentLocalVersion != ctx.TargetVersion ? " (Will mutate .csproj)" : "";
        host.UI.Write("Target Version: ");
        host.UI.WriteLine($"{ctx.TargetVersion}{mutateWarn}", ConsoleColor.Magenta);

        host.UI.WriteLine("==================================================", ConsoleColor.Cyan);

        var confirmResult = await host.PromptUserAsync("\nProceed with execution? (y/n):").ConfigureAwait(false);

        return confirmResult.Bind(input =>
        {
            if (input.Equals("y", StringComparison.OrdinalIgnoreCase))
                return Maybe.Some(ctx);

            return Maybe.None<PublishContext>("Operation cancelled by user.");
        });
    }
}