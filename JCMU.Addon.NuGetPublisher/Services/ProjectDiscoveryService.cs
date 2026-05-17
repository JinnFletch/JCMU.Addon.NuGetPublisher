using JinnDev.JCMU.SDK.Interfaces;
using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.Addon.NuGetPublisher.Services;

public static class ProjectDiscoveryService
{
    private static readonly string[] IgnoreFolders = { ".git", ".vs", "Publish", "bin", "obj", ".github" };

    /// <summary>
    /// Intelligently finds packable projects based on the user's right-click target.
    /// </summary>
    public static async Task<Maybe<string>> DiscoverProjectAsync(string targetDirectory, IHostServices host)
    {
        return await Maybe.TryAsync<string>(async () =>
        {
            var candidates = new List<string>();

            // Scenario 1: They right-clicked directly on a project folder
            var directCsproj = Directory.GetFiles(targetDirectory, "*.csproj").FirstOrDefault();
            if (directCsproj != null && IsPackable(directCsproj))
            {
                candidates.Add(directCsproj);
            }

            // Scenario 2: They right-clicked on a Solution or root repo folder
            if (candidates.Count == 0)
            {
                var directories = new DirectoryInfo(targetDirectory).GetDirectories();
                foreach (var dir in directories)
                {
                    if (IgnoreFolders.Any(f => dir.Name.Equals(f, StringComparison.OrdinalIgnoreCase))) continue;

                    var subCsproj = dir.GetFiles("*.csproj").FirstOrDefault();
                    if (subCsproj != null && IsPackable(subCsproj.FullName))
                    {
                        candidates.Add(subCsproj.FullName);
                    }
                }
            }

            if (candidates.Count == 0)
                throw new Exception("No projects found with <GeneratePackageOnBuild>true</GeneratePackageOnBuild>.");

            if (candidates.Count == 1) return candidates[0];

            // If multiple found, prompt the user
            return await PromptForProjectSelectionAsync(candidates, host).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private static bool IsPackable(string csprojPath)
    {
        return File.ReadAllText(csprojPath).Contains("<GeneratePackageOnBuild>true</GeneratePackageOnBuild>", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> PromptForProjectSelectionAsync(List<string> candidates, IHostServices host)
    {
        host.Logger.LogInfo("\nMultiple package projects detected:");
        for (int i = 0; i < candidates.Count; i++)
        {
            host.Logger.LogInfo($"{i + 1}. {Path.GetFileName(candidates[i])}");
        }

        var result = await host.PromptUserAsync($"\nSelect project (1-{candidates.Count}):").ConfigureAwait(false);

        // Match the Monad. If they input something, validate it.
        return result.Match(
            some: input =>
            {
                if (int.TryParse(input, out int choice) && choice > 0 && choice <= candidates.Count)
                    return candidates[choice - 1];

                throw new Exception("Invalid project selection.");
            },
            none: err => throw new Exception("Selection cancelled.")
        );
    }
}