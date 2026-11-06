using System.Reflection;

[assembly: AssemblyTitle("LiteDPI")]
[assembly: AssemblyProduct("LiteDPI")]
[assembly: AssemblyDescription("Per-game mouse DPI for Logitech mice, without G HUB")]
[assembly: AssemblyCopyright("Copyright (c) 2026 JC Reyes. MIT License.")]
[assembly: AssemblyVersion(LiteDPI.AppInfo.Version + ".0")]
[assembly: AssemblyFileVersion(LiteDPI.AppInfo.Version + ".0")]
[assembly: AssemblyInformationalVersion(LiteDPI.AppInfo.Version)]

// To release a new version: bump Version here, add a CHANGELOG.md entry, then push a tag "v<Version>".
namespace LiteDPI
{
    static class AppInfo
    {
        public const string Version = "1.1.0";
        public const string Repo = "CARLOSDAJER/LiteDPI";
    }
}
