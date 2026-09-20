namespace DevPurge.Core.Models;

/// <summary>
/// A rule defining target directories and their artifact category.
/// </summary>
public record PurgeRule(
    string Name,
    ArtifactType ArtifactType,
    string CategoryName,
    string[] FolderNames,
    string Description,
    bool IsEnabled = true
)
{
    /// <summary>
    /// Gets default built-in rules covering major developer ecosystems.
    /// </summary>
    public static IReadOnlyList<PurgeRule> GetDefaultRules() =>
    [
        new(
            "Node.js Dependencies",
            ArtifactType.NodeModules,
            "JavaScript / Node.js",
            ["node_modules"],
            "Dependencies installed via npm, pnpm, or yarn"
        ),
        new(
            ".NET Build Output",
            ArtifactType.DotNetBuild,
            ".NET / C#",
            ["bin", "obj", "TestResults"],
            "Compiled binaries and intermediate object files"
        ),
        new(
            "Rust Cargo Target",
            ArtifactType.RustTarget,
            "Rust",
            ["target"],
            "Compiled Rust binaries, dependencies, and incremental caches"
        ),
        new(
            "Gradle / Java Build",
            ArtifactType.GradleBuild,
            "Java / Android",
            ["build", ".gradle"],
            "Gradle build outputs, wrapper caches, and intermediate classes"
        ),
        new(
            "Python Virtual Envs & Cache",
            ArtifactType.PythonVenv,
            "Python",
            [".venv", "venv", "__pycache__", ".pytest_cache", ".mypy_cache"],
            "Python virtual environments and bytecode cache"
        ),
        new(
            "Frontend Framework Caches",
            ArtifactType.CacheAndTemp,
            "Web Frameworks",
            [".next", ".nuxt", ".turbo", ".cache", ".svelte-kit", "dist"],
            "Bundler and SSR framework cache folders"
        ),
        new(
            "Vendor Directories",
            ArtifactType.Vendor,
            "Composer / Go",
            ["vendor"],
            "Third-party packages (PHP Composer, Go vendor)"
        ),
        new(
            "Visual Studio Cache",
            ArtifactType.CacheAndTemp,
            "Visual Studio",
            [".vs"],
            "Visual Studio IDE workspace cache and symbol indexes"
        )
    ];
}
