using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace VideoQualityAnalyzer.App;

internal static class DisplayStyleSettings
{
    private static readonly string[] Modes = ["None", "System", "Light", "Dark"];
    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VideoQualityAnalyzer", "display-style.txt");

    public static string ActiveMode { get; private set; } = "System";
    public static string ActivePalette { get; private set; } = PaletteFor("System", SystemUsesLightTheme());

    public static string Load() => Load(SettingsPath);

    internal static string Load(string path)
    {
        try
        {
            var value = File.Exists(path) ? File.ReadAllText(path).Trim() : "System";
            return Modes.Contains(value, StringComparer.Ordinal) ? value : "System";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return "System"; }
    }

    public static void Save(string mode) => Save(mode, SettingsPath);

    internal static void Save(string mode, string path)
    {
        if (!Modes.Contains(mode, StringComparer.Ordinal))
            throw new ArgumentException("表示スタイルを選択してください。", nameof(mode));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, mode);
    }

    public static void Apply(Application app, string mode)
    {
        if (!Modes.Contains(mode, StringComparer.Ordinal))
            throw new ArgumentException("表示スタイルが不正です。", nameof(mode));
        ActiveMode = mode;
        ActivePalette = PaletteFor(mode, SystemUsesLightTheme());
#pragma warning disable WPF0001 // .NET 10のFluentテーマは実験的API。
        app.ThemeMode = mode switch
        {
            "None" => ThemeMode.None,
            "System" => ThemeMode.System,
            "Light" => ThemeMode.Light,
            _ => ThemeMode.Dark
        };
#pragma warning restore WPF0001
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri($"Styles/Palettes/{ActivePalette}.xaml", UriKind.Relative) });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("Styles/UiTheme.xaml", UriKind.Relative) });
    }

    internal static string PaletteFor(string mode, bool systemUsesLightTheme) => mode switch
    {
        "Dark" => "Dark",
        "Light" or "None" => "Light",
        "System" => systemUsesLightTheme ? "Light" : "Dark",
        _ => throw new ArgumentException("表示スタイルが不正です。", nameof(mode))
    };

    private static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return true; }
    }
}
