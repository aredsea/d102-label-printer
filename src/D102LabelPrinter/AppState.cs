using System.Text.Json;

namespace D102LabelPrinter;

/// <summary>설정·레이아웃 영속화. %AppData%\D102LabelPrinter\</summary>
public static class AppState
{
    public const string Version = "0.1.3";

    static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "D102LabelPrinter");
    static string CfgPath => Path.Combine(Dir, "config.json");
    static string LayoutPath => Path.Combine(Dir, "layout.json");
    // camelCase: 편집기(JS)의 키(x,y,fs,visible,text…)와 layout.json 을 일치시킨다.
    public static readonly JsonSerializerOptions Camel = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static FixedConfig Config = new();
    public static List<Field> Layout = LabelModel.DefaultLayout();

    public static void Load()
    {
        Directory.CreateDirectory(Dir);
        if (File.Exists(CfgPath))
            try { Config = JsonSerializer.Deserialize<FixedConfig>(File.ReadAllText(CfgPath), Camel) ?? new(); } catch { }
        if (File.Exists(LayoutPath))
            try { var l = JsonSerializer.Deserialize<List<Field>>(File.ReadAllText(LayoutPath), Camel); if (l is { Count: > 0 }) Layout = l; } catch { }
    }

    public static void SaveConfig()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(CfgPath, JsonSerializer.Serialize(Config, Camel));
    }

    public static void SaveLayout()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(LayoutPath, JsonSerializer.Serialize(Layout, Camel));
    }
}
