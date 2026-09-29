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
            ["bin", "obj", "TestResults", "BenchmarkDotNet.Artifacts"],
            "Compiled binaries, intermediate object files, and benchmark artifacts"
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
            ["build", ".gradle", ".kotlin"],
            "Gradle build outputs, wrapper caches, and intermediate classes"
        ),
        new(
            "Python Virtual Envs & Cache",
            ArtifactType.PythonVenv,
            "Python",
            [".venv", "venv", "__pycache__", ".pytest_cache", ".mypy_cache", ".ruff_cache", ".tox", "htmlcov", ".nox", ".hypothesis", ".uv_cache", ".uv", ".pixi", "__pypackages__"],
            "Python virtual environments, bytecode cache, uv/pixi/pdm caches, and test coverage"
        ),
        new(
            "Frontend Framework Caches",
            ArtifactType.CacheAndTemp,
            "Web Frameworks",
            [".next", ".nuxt", ".turbo", ".cache", ".svelte-kit", "dist", ".angular", ".astro", ".parcel-cache", ".vite", ".nx", ".docusaurus", ".rollup.cache", ".swc", ".rspack-cache", ".nyc_output"],
            "Bundler, build tools, SSR framework, and test coverage cache folders"
        ),
        new(
            "Vendor Directories",
            ArtifactType.Vendor,
            "Composer / Go",
            ["vendor", ".gocache"],
            "Third-party packages (PHP Composer, Go vendor) and Go compiler build cache"
        ),
        new(
            "Visual Studio Cache",
            ArtifactType.CacheAndTemp,
            "Visual Studio",
            [".vs"],
            "Visual Studio IDE workspace cache and symbol indexes"
        ),
        new(
            "IDE & Editor Caches",
            ArtifactType.IdeCache,
            "IDE Caches",
            [".idea", ".fleet", ".bloop", ".metals"],
            "JetBrains IntelliJ, Rider, WebStorm, Fleet, Bloop, and Metals workspace indexes and caches"
        ),
        new(
            "Dart & Flutter Build",
            ArtifactType.DartFlutter,
            "Dart / Flutter",
            [".dart_tool"],
            "Flutter and Dart package configurations and build caches"
        ),
        new(
            "C++ & CMake Build",
            ArtifactType.CppBuild,
            "C++ / CMake",
            ["cmake-build-debug", "cmake-build-release", "cmake-build-relwithdebinfo", "cmake-build-minsizerel", ".cxx"],
            "CMake and C++ compiler intermediate build outputs across all profiles"
        ),
        new(
            "Zig Build Output",
            ArtifactType.ZigBuild,
            "Zig",
            ["zig-cache", "zig-out"],
            "Zig compiler build cache and compiled binary outputs"
        ),
        new(
            "Swift & Xcode Build",
            ArtifactType.SwiftBuild,
            "Swift / Apple",
            ["DerivedData", ".build"],
            "Xcode intermediate build files, SPM build directories, module caches, and index data"
        ),
        new(
            "Elixir Build Output",
            ArtifactType.ElixirBuild,
            "Elixir",
            ["_build"],
            "Mix build outputs and compiled beam bytecode"
        ),
        new(
            "Ruby Bundler Dependencies",
            ArtifactType.RubyBundle,
            "Ruby / Bundler",
            [".bundle"],
            "Installed Ruby gem bundle configuration and local path bundles"
        ),
        new(
            "Haskell Build Artifacts",
            ArtifactType.HaskellBuild,
            "Haskell",
            ["dist-newstyle", ".stack-work"],
            "Cabal and Stack compiled Haskell build products and package caches"
        ),
        new(
            "Terraform & OpenTofu Cache",
            ArtifactType.TerraformCache,
            "Terraform / OpenTofu",
            [".terraform"],
            "Terraform provider plugins, modules, and local state caches"
        )
    ];

    /// <summary>
    /// Creates a custom purge rule with user-specified folder names.
    /// </summary>
    public static PurgeRule CreateCustomRule(
        string name,
        string[] folderNames,
        string categoryName = "Custom",
        ArtifactType artifactType = ArtifactType.Custom,
        string description = "") =>
        new(name, artifactType, categoryName, folderNames, description);

    /// <summary>
    /// Attempts to find a matching rule for a target folder name.
    /// </summary>
    public static bool TryMatchFolder(string folderName, out PurgeRule? matchedRule, IEnumerable<PurgeRule>? rules = null)
    {
        var activeRules = rules ?? GetDefaultRules();
        foreach (var rule in activeRules)
        {
            if (!rule.IsEnabled) continue;
            foreach (var name in rule.FolderNames)
            {
                if (string.Equals(name, folderName, StringComparison.OrdinalIgnoreCase))
                {
                    matchedRule = rule;
                    return true;
                }
            }
        }

        matchedRule = null;
        return false;
    }

    /// <summary>
    /// Retrieves all default rules belonging to a specified category.
    /// </summary>
    public static IReadOnlyList<PurgeRule> GetRulesByCategory(string categoryName, IEnumerable<PurgeRule>? rules = null)
    {
        var activeRules = rules ?? GetDefaultRules();
        return activeRules
            .Where(r => string.Equals(r.CategoryName, categoryName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
