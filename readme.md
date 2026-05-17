# JCMU NuGet Publisher

**Publishing to NuGet should be a click, not a chore.**

The JCMU NuGet Publisher is an addon for the Jinn Context Menu Utility that brings the power of the .NET CLI directly into your Windows Explorer right-click menu. Stop digging for API keys and hunting through `bin/Release` folders; now you can build, pack, and push your libraries without ever opening a terminal.

## Why use this addon?

*   **Zero-Config Discovery:** Right-click a folder and the addon automatically finds any project configured for NuGet packaging.
*   **Smart Versioning:** It reads your current project version and automatically suggests the next logical patch (e.g., `1.0.4` -> `1.0.5`), while still giving you the freedom to type a custom version on the fly.
*   **Safety First:** Before any files are modified or uploaded, the addon presents a clear "Publish Plan" for you to confirm.
*   **Clean Workflow:** It handles the entire pipeline—updating the version in your `.csproj`, building the project in Release mode, generating the package, and pushing it to your feed.

## How to use it

### 1. The Right-Click
Navigate to any folder containing a .NET project (or a solution containing multiple projects). Right-click the folder and select **NuGet > Publish**.

### 2. First-Time Setup
If it's your first time running the addon, it will ask you for two things:
1.  **NuGet Feed URL:** Your destination (like `https://api.nuget.org/v3/index.json`).
2.  **API Key:** Your secret upload key (which is stored safely in your local User Profile).

### 3. Choose Your Target
If the folder contains multiple projects, the addon will list them and ask you which one you want to publish.

### 4. Set the Version
The addon displays your current version and suggests the next one. 
*   **Press Enter** to accept the suggestion.
*   **Type a new version** (like `2.0.0-beta`) if you need a specific release.

### 5. Confirm and Push
A final summary will appear showing exactly what is about to happen. Type `y` to proceed. You will see the live build logs as the project compiles, followed by a success message once the package is live on your NuGet feed.

## Prerequisites

*   **JCMU Core:** Must be installed and initialized.
*   **.NET SDK:** You must have the .NET SDK installed on your machine (the addon uses your local `dotnet` command).
*   **Packable Projects:** Your `.csproj` files should have `<GeneratePackageOnBuild>true</GeneratePackageOnBuild>` enabled.

---
*Developed by JinnDev for the JCMU Ecosystem.*