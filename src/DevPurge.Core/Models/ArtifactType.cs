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
    Custom
}
