using JinnDev.JCMU.Addon.NuGetPublisher.Models;
using JinnDev.JCMU.SDK.Interfaces;
using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.Addon.NuGetPublisher.Services;

public static class UserInteractionService
{
    public static async Task<Maybe<PublishConfig>> RunFirstTimeSetupAsync(IHostServices host)
    {
        host.Logger.LogInfo("=== First Time Setup ===");
        host.Logger.LogInfo("It looks like this is your first time publishing a package with JCMU.");

        return await PromptForNewSourceAsync(host, isDefault: true)
            .BindAsync(newSource =>
            {
                var config = new PublishConfig { Sources = new List<PublishSource> { newSource } };

                return host.Settings.SetValueAsync("PublishConfig", config)
                    .BindAsync(_ => string.IsNullOrWhiteSpace(newSource.ApiKey)
                        ? Task.FromResult(Maybe.SUCCESS) // No API Key, just move on
                        : host.Settings.SetSecretAsync($"ApiKey_{newSource.Name}", newSource.ApiKey))
                    .TapAsync(_ => {
                        host.Logger.LogInfo("Configuration saved successfully!\n");
                        return Task.CompletedTask;
                    })
                    .WithValueAsync(() => config);
            }).ConfigureAwait(false);
    }

    public static async Task<Maybe<PublishSource>> SelectSourceAsync(PublishConfig config, IHostServices host)
    {
        var defaultSource = config.Sources.First(x => x.IsDefault);

        host.Logger.LogInfo("\nAvailable Publish Sources:");
        for (int i = 0; i < config.Sources.Count; i++)
        {
            var s = config.Sources[i];
            var suffix = s.IsDefault ? " [DEFAULT]" : "";
            host.Logger.LogInfo($"{i + 1}. {s.Name} ({s.Url}){suffix}");
        }

        int addOptionIndex = config.Sources.Count + 1;
        host.Logger.LogInfo($"{addOptionIndex}. Add a new NuGet package source");

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
                        host.Logger.LogInfo($"\nSource '{newSource.Name}' saved successfully!");
                        return Task.CompletedTask; })
                    .WithValueAsync(() => newSource);
            }).ConfigureAwait(false);
    }

    /// <summary>
    /// Presents the final plan to the user for confirmation before mutating files or pushing.
    /// </summary>
    public static async Task<Maybe<PublishContext>> ConfirmPlanAsync(PublishContext ctx, IHostServices host)
    {
        host.Logger.LogInfo("\n================== PUBLISH PLAN ==================");
        host.Logger.LogInfo($"1. Update .csproj version to: {ctx.TargetVersion}");
        host.Logger.LogInfo($"2. Build & Pack (Release) to: {ctx.OutputDir}");
        host.Logger.LogInfo($"3. Push to Source:            {ctx.SelectedSource.Name}");
        host.Logger.LogInfo($"   Target URL:                {ctx.SelectedSource.Url}");
        host.Logger.LogInfo("==================================================");

        var confirmResult = await host.PromptUserAsync("\nProceed with execution? (y/n):").ConfigureAwait(false);

        return confirmResult.Bind(input =>
        {
            if (input.Equals("y", StringComparison.OrdinalIgnoreCase))
                return Maybe.Some(ctx);

            return Maybe.None<PublishContext>("Operation cancelled by user.");
        });
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
}