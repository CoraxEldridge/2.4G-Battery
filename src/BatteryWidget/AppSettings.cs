using System.Text.Json;

namespace BatteryWidget;

/// <summary>本地设置：勾选的设备、自动刷新开关。</summary>
public sealed class AppSettings
{
    public List<string> SelectedDevices { get; set; } = new();

    /// <summary>自动刷新间隔（秒），0 表示关闭。</summary>
    public int RefreshSeconds { get; set; } = 5;

    public bool FirstRunCompleted { get; set; }

    private static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatteryWidget");

    private static string FilePath => Path.Combine(Directory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                    return loaded;
            }
        }
        catch
        {
            // 配置损坏时回到默认值
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 写不进去不影响主流程
        }
    }
}

