using System.Text.Json;

namespace FishReader;

internal sealed class AppConfig
{
    public string? FilePath { get; set; }
    public string? EncodingName { get; set; }
    public int CurrentOffset { get; set; }
    public double WindowLeft { get; set; } = 12;
    public double WindowTop { get; set; } = 600;
    public double Width { get; set; } = 230;
    public int VisibleRows { get; set; } = 5;
    public double FontSize { get; set; } = 16;
    public double RowGap { get; set; } = 10;
    public double TextOpacity { get; set; } = 0.94;
    public string FontFamily { get; set; } = "Microsoft YaHei UI";
    public string FontWeightName { get; set; } = "Normal";
    public bool FadeTrailingPunctuation { get; set; }
    public string DarkPageTextColor { get; set; } = "#DEDEDE";
    public string LightPageTextColor { get; set; } = "#333333";
    public bool UseDarkPageTheme { get; set; } = true;
    public string BossHotKey { get; set; } = "Alt+B";
    public string NextLineHotKey { get; set; } = "Alt+Down";
    public string PreviousLineHotKey { get; set; } = "Alt+Up";
    public string NextPageHotKey { get; set; } = "Alt+PageDown";
    public string PreviousPageHotKey { get; set; } = "Alt+PageUp";
    public string LayoutHotKey { get; set; } = "Alt+L";
    public string ThemeHotKey { get; set; } = "Alt+T";
}

internal sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string DataDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "data");
    public string ConfigPath => Path.Combine(DataDirectory, "config.json");

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
                return new AppConfig();

            var value = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), JsonOptions);
            return Normalize(value ?? new AppConfig());
        }
        catch
        {
            return new AppConfig();
        }
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(DataDirectory);
        var tempPath = ConfigPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(Normalize(config), JsonOptions));
        File.Move(tempPath, ConfigPath, true);
    }

    public static AppConfig Normalize(AppConfig config)
    {
        config.Width = Math.Clamp(config.Width, 120, 900);
        config.VisibleRows = Math.Clamp(config.VisibleRows, 1, 8);
        config.FontSize = Math.Clamp(config.FontSize, 12, 28);
        config.RowGap = Math.Clamp(config.RowGap, 2, 32);
        config.TextOpacity = Math.Clamp(config.TextOpacity, 0.25, 1);
        if (string.IsNullOrWhiteSpace(config.FontFamily))
            config.FontFamily = "Microsoft YaHei UI";
        if (config.FontWeightName is not ("Normal" or "Medium" or "SemiBold"))
            config.FontWeightName = "Normal";
        NormalizeHotKeys(config);
        config.CurrentOffset = Math.Max(0, config.CurrentOffset);
        return config;
    }

    private static void NormalizeHotKeys(AppConfig config)
    {
        var values = new[]
        {
            config.BossHotKey, config.NextLineHotKey, config.PreviousLineHotKey, config.NextPageHotKey,
            config.PreviousPageHotKey, config.LayoutHotKey, config.ThemeHotKey
        };
        var parsed = new HotKeyBinding[values.Length];
        var valid = true;
        var unique = new HashSet<(uint Modifiers, uint Key)>();
        for (var i = 0; i < values.Length; i++)
        {
            if (!HotKeyBinding.TryParse(values[i], out parsed[i], out _) ||
                !unique.Add((parsed[i].Modifiers, parsed[i].VirtualKey)))
            {
                valid = false;
                break;
            }
        }

        if (!valid)
        {
            config.BossHotKey = "Alt+B";
            config.NextLineHotKey = "Alt+Down";
            config.PreviousLineHotKey = "Alt+Up";
            config.NextPageHotKey = "Alt+PageDown";
            config.PreviousPageHotKey = "Alt+PageUp";
            config.LayoutHotKey = "Alt+L";
            config.ThemeHotKey = "Alt+T";
            return;
        }

        config.BossHotKey = parsed[0].Display;
        config.NextLineHotKey = parsed[1].Display;
        config.PreviousLineHotKey = parsed[2].Display;
        config.NextPageHotKey = parsed[3].Display;
        config.PreviousPageHotKey = parsed[4].Display;
        config.LayoutHotKey = parsed[5].Display;
        config.ThemeHotKey = parsed[6].Display;
    }
}
