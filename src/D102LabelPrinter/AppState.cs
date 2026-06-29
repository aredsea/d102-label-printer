using System.Text.Json;

namespace D102LabelPrinter;

/// <summary>설정·레이아웃 영속화. %AppData%\D102LabelPrinter\</summary>
public static class AppState
{
    // 실제 어셈블리 버전(csproj <Version>)을 읽는다. 하드코딩 금지(표시 버전 불일치 방지).
    public static string Version
    {
        get
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return v != null ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";
        }
    }

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
            try { var l = JsonSerializer.Deserialize<List<Field>>(File.ReadAllText(LayoutPath), Camel); if (l is { Count: > 0 }) Layout = LabelModel.Merge(l); } catch { }
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
