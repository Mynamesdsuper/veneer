namespace Veneer.Core.Themes;

public enum Severity
{
    Warning,
    Error,
}

/// <summary>A readable problem found while loading a theme. <see cref="Path"/> points at the value, e.g. <c>surfaces[id=dock].iconSize</c>.</summary>
public sealed record Diagnostic(Severity Severity, string Path, string Message)
{
    public static Diagnostic Error(string path, string message) => new(Severity.Error, path, message);

    public static Diagnostic Warning(string path, string message) => new(Severity.Warning, path, message);

    public override string ToString() => $"{Severity}: {Path}: {Message}";
}

/// <summary>Outcome of a theme load. Loading never throws: on any error <see cref="Theme"/> is null.</summary>
public sealed record ThemeLoadResult(Theme? Theme, IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<string> OrphanOverrides)
{
    public bool Success => Theme is not null;

    public IEnumerable<Diagnostic> Errors => Diagnostics.Where(d => d.Severity == Severity.Error);

    public IEnumerable<Diagnostic> Warnings => Diagnostics.Where(d => d.Severity == Severity.Warning);
}
