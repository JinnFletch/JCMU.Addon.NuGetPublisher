using JinnDev.JCMU.Addon.NuGetPublisher.Models;
using JinnDev.Utilities.Monad;
using System.Text.Json;

namespace JinnDev.JCMU.Addon.NuGetPublisher.Services;

public static class ConfigurationManager
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "JCMU", "Addons", "NuGetPublisher");

    private static readonly string ConfigFile = Path.Combine(ConfigDir, "config.json");

    /// <summary>
    /// Loads the configuration. Will return a 'Fail' Maybe if the file does not exist, 
    /// which we will use to trigger the First-Time Setup flow later.
    /// </summary>
    public static Task<Maybe<PublishConfig>> LoadConfig()
    {
        return Task.FromResult(Maybe.Try<PublishConfig>(() =>
        {
            if (!File.Exists(ConfigFile))
                return Maybe.None<PublishConfig>("Config file not found. First-time setup required.");

            var json = File.ReadAllText(ConfigFile);
            var config = JsonSerializer.Deserialize<PublishConfig>(json)
                         ?? throw new Exception("Config file is corrupted or empty.");

            return config;
        }));
    }

    /// <summary>
    /// Saves the configuration to the user's AppData roaming profile.
    /// </summary>
    public static Maybe SaveConfig(PublishConfig config)
    {
        return Maybe.Try(() =>
        {
            if (!Directory.Exists(ConfigDir)) Directory.CreateDirectory(ConfigDir);

            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigFile, json);
        });
    }
}