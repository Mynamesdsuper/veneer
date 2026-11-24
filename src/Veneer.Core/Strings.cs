using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;

namespace Veneer.Core;

/// <summary>
/// Messages from Veneer.Core that the user reads, in the Windows display language: <c>Strings.resx</c> (English) and
/// <c>Strings.it.resx</c> (Italian). Each property is named after its key. Theme diagnostics stay in English: they are
/// for theme authors and match docs/theme-format.md.
/// </summary>
internal static class Strings
{
    static readonly ResourceManager Resources = new("Veneer.Core.Strings", typeof(Strings).Assembly);

    public static string NameEmpty => Get();

    public static string NameTooLong(int max) => Format(max);

    public static string NameBadCharacter => Get();

    public static string NameEdges => Get();

    public static string NameReserved(string name) => Format(name);

    public static string ProfileMissing(string name) => Format(name);

    public static string ProfileExists(string name) => Format(name);

    public static string ProfileDeleteActive => Get();

    public static string ProfileDeleteLast => Get();

    // A missing key shows its name instead of crashing; the resource tests catch it first.
    static string Get([CallerMemberName] string key = "") => Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    static string Format(object arg, [CallerMemberName] string key = "") => string.Format(CultureInfo.CurrentCulture, Get(key), arg);
}
