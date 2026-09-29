namespace DevPurge.Core.Models;

/// <summary>
/// Categories of disposable developer artifacts.
/// </summary>
public enum ArtifactType
{
    NodeModules,
    DotNetBuild,
    RustTarget,
    GradleBuild,
    PythonVenv,
    CacheAndTemp,
    Vendor,
    Custom,
    DartFlutter,
    IdeCache,
    CppBuild,
    ZigBuild,
    SwiftBuild,
    ElixirBuild,
    RubyBundle,
    HaskellBuild,
    TerraformCache
}
