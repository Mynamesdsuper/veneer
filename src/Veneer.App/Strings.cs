using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;

namespace Veneer.App;

/// <summary>
/// Every text the app shows, in the Windows display language: <c>Strings.resx</c> (English) and <c>Strings.it.resx</c>
/// (Italian). Each property is named after its key; a new language is one more <c>Strings.&lt;culture&gt;.resx</c>.
/// </summary>
static class Strings
{
    static readonly ResourceManager Resources = new("Veneer.App.Strings", typeof(Strings).Assembly);
    public static string MenuProfile => Get();

    public static string MenuTheme => Get();

    public static string MenuStartWithWindows => Get();

    public static string MenuRestoreTaskbar => Get();

    public static string MenuRestoreWindowsSettings => Get();

    public static string MenuSettings => Get();

    public static string MenuQuit => Get();

    public static string LogoAbout => Get();

    public static string LogoSystemSettings => Get();

    public static string LogoSleep => Get();

    public static string LogoRestart => Get();

    public static string LogoShutDown => Get();

    public static string LogoLock => Get();

    public static string LogoVeneer => Get();

    public static string ConfirmSleep => Get();

    public static string ConfirmRestart => Get();

    public static string ConfirmShutDown => Get();

    public static string AppCloseWindow => Get();

    public static string AppMinimize => Get();

    public static string AppQuit(string app) => Format(app);

    public static string TipVolume(double percent) => Format(percent);

    public static string TipVolumeMuted(double percent) => Format(percent);

    public static string TipWiFi => Get();

    public static string TipEthernet => Get();

    public static string TipOffline => Get();

    public static string TipBattery(int percent) => Format(percent);

    public static string TipBatteryCharging(int percent) => Format(percent);

    public static string DockKeep => Get();

    public static string DockRemove => Get();

    public static string DockClose => Get();

    public static string DockCloseAll => Get();

    public static string SettingsTitle => Get();

    public static string PageGeneral => Get();

    public static string PageProfiles => Get();

    public static string PageTheme => Get();

    public static string PageCustomize => Get();

    public static string PageWindows => Get();

    public static string PageDiagnostics => Get();

    public static string StartWithWindows => Get();

    public static string TaskbarSection => Get();

    public static string TaskbarNote => Get();

    public static string TaskbarShow => Get();

    public static string WindowsSettingsSection => Get();

    public static string WindowsSettingsNote => Get();

    public static string WindowsSettingsNothing => Get();

    public static string ShortcutsSection => Get();

    public static string ShortcutQuit => Get();

    public static string ShortcutTaskbar => Get();

    public static string ShortcutProfile => Get();

    public static string ShortcutEmergency => Get();

    public static string ProfilesNote => Get();

    public static string ProfilesOff => Get();

    public static string ProfileActive(string name) => Format(name);

    public static string ProfileUse => Get();

    public static string ProfileNew => Get();

    public static string ProfileDuplicate => Get();

    public static string ProfileRename => Get();

    public static string ProfileDelete => Get();

    public static string ProfileNewTitle => Get();

    public static string ProfileDuplicateTitle => Get();

    public static string ProfileRenameTitle => Get();

    public static string ProfileNamePrompt => Get();

    public static string ProfileCopyName(string name) => Format(name);

    public static string ProfileDeleteConfirm(string name) => Format(name);

    public static string Ok => Get();

    public static string Cancel => Get();

    public static string ThemeNote(string profile) => Format(profile);

    public static string ThemeInstall => Get();

    public static string ThemeZipFilter => Get();

    public static string ThemeInstalling => Get();

    public static string ThemeInstalled(string name) => Format(name);

    public static string ThemeNotInstalled => Get();

    public static string ThemeProblems => Get();

    public static string ThemeProblemsNote => Get();

    public static string ThemeNoProblems => Get();

    public static string ThemeNotLoaded => Get();

    public static string SeverityError => Get();

    public static string SeverityWarning => Get();

    public static string CustomizeNote(string profile) => Format(profile);

    public static string CustomizeBuiltIn => Get();

    public static string SurfaceBar(string id) => Format(id);

    public static string SurfaceDock(string id) => Format(id);

    public static string SettingEdge => Get();

    public static string SettingThickness => Get();

    public static string SettingReserveSpace => Get();

    public static string SettingIconSize => Get();

    public static string SettingMagnification => Get();

    public static string SettingMagnificationScale => Get();

    public static string SettingAutoHide => Get();

    public static string EdgeTop => Get();

    public static string EdgeBottom => Get();

    public static string EdgeLeft => Get();

    public static string EdgeRight => Get();

    public static string OriginTheme => Get();

    public static string OriginYours => Get();

    public static string OriginDefault => Get();

    public static string Reset => Get();

    public static string ResetTip => Get();

    public static string OrphansSection => Get();

    public static string OrphansNote => Get();

    public static string OrphanRemove => Get();

    public static string NotSaved(string error) => Format(error);

    public static string WindowsNote => Get();

    public static string WindowsWallpaper => Get();

    public static string WindowsAccent => Get();

    public static string WindowsAppearance => Get();

    public static string ChoiceFromTheme => Get();

    public static string ChoiceLeave => Get();

    public static string ChoiceLight => Get();

    public static string ChoiceDark => Get();

    public static string ChoiceFollow => Get();

    public static string ThemeSets(string value) => Format(value);

    public static string ThemeSetsNothing => Get();

    public static string AppearanceLight => Get();

    public static string AppearanceDark => Get();

    public static string AppearanceSystem => Get();

    public static string WindowsRestoreNote => Get();

    public static string DiagnosticsNote => Get();

    public static string Copy => Get();

    public static string Copied => Get();

    public static string Refresh => Get();

    public static string PinImportTitle => Get();

    public static string PinImportHeading => Get();

    public static string PinImportNote => Get();

    public static string PinImportImport => Get();

    public static string PinImportNotNow => Get();

    public static string PinImportSection => Get();

    public static string PinImportSettingsNote => Get();

    public static string PinImportButton => Get();

    public static string PinImportNothing => Get();

    public static string PinImportDone(int count) => Format(count);

    // A missing key shows its name instead of crashing; the resource tests catch it first.
    static string Get([CallerMemberName] string key = "") => Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    static string Format(object arg, [CallerMemberName] string key = "") => string.Format(CultureInfo.CurrentCulture, Get(key), arg);
}