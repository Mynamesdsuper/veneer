namespace Veneer.Core;

/// <summary>Where Veneer keeps its files. <c>VENEER_HOME</c> overrides the root (useful for development and tests).</summary>
public static class AppPaths
{
    public static string Root { get; } =
        Environment.GetEnvironmentVariable("VENEER_HOME") is { Length: > 0 } home
            ? home
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Veneer");

    public static string Themes => Path.Combine(Root, "themes");

    public static string Profiles => Path.Combine(Root, "profiles");

    public static string State => Path.Combine(Root, "state");

    public static string Logs => Path.Combine(Root, "logs");
}
