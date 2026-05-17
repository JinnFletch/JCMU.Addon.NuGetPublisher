using JinnDev.Utilities.Monad;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace JinnDev.JCMU.Addon.NuGetPublisher.Services;

public static class ProjectMetadataService
{
    /// <summary>
    /// Locates the PackageId, falling back to AssemblyName or the Project Name itself.
    /// </summary>
    public static Maybe<string> GetPackageId(string projectPath)
    {
        return Maybe.Try<string>(() =>
        {
            var xml = XDocument.Load(projectPath);
            var projectName = Path.GetFileNameWithoutExtension(projectPath);

            var packageId = xml.Descendants("PackageId").FirstOrDefault()?.Value;
            if (!string.IsNullOrWhiteSpace(packageId)) return ResolveMsBuildVars(packageId, projectName);

            var assemblyName = xml.Descendants("AssemblyName").FirstOrDefault()?.Value;
            if (!string.IsNullOrWhiteSpace(assemblyName)) return ResolveMsBuildVars(assemblyName, projectName);

            return projectName;
        });
    }

    /// <summary>
    /// Extracts the semantic version from the csproj.
    /// </summary>
    public static Maybe<Version> GetCurrentVersion(string projectPath)
    {
        return Maybe.Try<Version>(() =>
        {
            string content = File.ReadAllText(projectPath);
            var match = Regex.Match(content, @"<(?:Package)?Version>(.+?)<\/(?:Package)?Version>");

            if (match.Success && Version.TryParse(match.Groups[1].Value, out var v))
                return v;

            throw new Exception($"Could not find <Version> or <PackageVersion> tag in {Path.GetFileName(projectPath)}.");
        });
    }

    /// <summary>
    /// Physically overwrites the csproj file with the new target version.
    /// </summary>
    public static Maybe UpdateProjectVersion(string projectPath, Version newVersion)
    {
        return Maybe.Try(() =>
        {
            string content = File.ReadAllText(projectPath);
            string pattern = @"(<(?:Package)?Version>)(.+?)(<\/(?:Package)?Version>)";
            string newContent = Regex.Replace(content, pattern, $"${{1}}{newVersion}$3");

            File.WriteAllText(projectPath, newContent);
        });
    }

    /// <summary>
    /// Resolves common MSBuild variables like $(MSBuildProjectName) inside the XML text.
    /// </summary>
    private static string ResolveMsBuildVars(string input, string projectName)
    {
        var regex = new Regex(@"\$\(MSBuildProjectName(?:\.Replace\(['""]([^'""]*)['""],\s*['""]([^'""]*)['""]\))?\)");

        return regex.Replace(input, match =>
        {
            var resolvedName = projectName;
            if (match.Groups[1].Success && match.Groups[2].Success)
            {
                resolvedName = resolvedName.Replace(match.Groups[1].Value, match.Groups[2].Value);
            }
            return resolvedName;
        });
    }
}