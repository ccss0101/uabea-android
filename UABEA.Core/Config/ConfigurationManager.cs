using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UABEAvalonia
{
    /// <summary>
    /// 配置管理器：负责加载/保存 config.json，并暴露全局 <see cref="Settings"/>。
    /// P2-C 改造：迁移到 System.Text.Json + JsonStringEnumConverter，
    /// 并通过 <see cref="DebounceUtils"/> 在属性变更后 500ms 自动写盘，
    /// 避免每次 setter 触发同步 IO。
    /// 与 UABEANext 的 ConfigurationManager 行为一致。
    /// </summary>
    public static class ConfigurationManager
    {
        public const string CONFIG_FILENAME = "config.json";

        public static ConfigurationValues Settings { get; }
        public static bool IsInitialized { get; private set; }

        private static readonly JsonSerializerOptions OPTIONS = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        // 自动保存防抖：500ms 内多次属性变更只触发一次写盘
        private static readonly Action<int> _saveDebounced = DebounceUtils.Debounce(
            (int _) => SaveConfig(), 500);

        static ConfigurationManager()
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CONFIG_FILENAME);
            if (!File.Exists(configPath))
            {
                Settings = new ConfigurationValues();
            }
            else
            {
                try
                {
                    string configText = File.ReadAllText(configPath);
                    Settings = JsonSerializer.Deserialize<ConfigurationValues>(configText, OPTIONS)
                        ?? new ConfigurationValues();
                }
                catch
                {
                    // 配置文件损坏时回退默认值，避免阻断启动
                    Settings = new ConfigurationValues();
                }
            }

            // 订阅属性变更触发防抖保存
            Settings.PropertyChanged += OnSettingPropertyChanged;

            IsInitialized = true;
        }

        private static void OnSettingPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!IsInitialized)
                return;

            _saveDebounced(0);
        }

        public static void SaveConfig()
        {
            if (!IsInitialized || Settings == null)
                return;

            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CONFIG_FILENAME);
                string configText = JsonSerializer.Serialize(Settings, OPTIONS);
                File.WriteAllText(configPath, configText);
            }
            catch
            {
                // 写盘失败不应阻断程序运行
            }
        }

        /// <summary>
        /// 返回所有带 [ConfigTitle] attribute 的配置项，供 SettingsViewModel 反射生成 UI。
        /// 顺序按属性声明顺序。
        /// </summary>
        public static List<ConfigurationItemBase> GetConfigurationItems()
        {
            var items = new List<ConfigurationItemBase>();
            foreach (var prop in typeof(ConfigurationValues).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var titleAttr = prop.GetCustomAttribute<ConfigTitle>();
                if (titleAttr is null)
                    continue;

                ConfigurationItemBase? item = prop.PropertyType switch
                {
                    Type t when t == typeof(bool) => new ConfigurationBooleanItem(prop),
                    Type t when t == typeof(int) => new ConfigurationIntegerItem(prop),
                    Type t when t.IsEnum => new ConfigurationEnumItem(prop),
                    _ => null
                };

                if (item != null)
                    items.Add(item);
            }
            return items;
        }
    }

    /// <summary>
    /// 配置值集合。所有可配置项以 [ObservableProperty] 暴露，
    /// 并通过 [ConfigTitle]/[ConfigDesc]/[ConfigRange] attribute 描述元数据，
    /// 供 SettingsView 反射生成 UI。
    /// </summary>
    public partial class ConfigurationValues : ObservableObject
    {
        // ---- 主题 ----
        [ObservableProperty]
        [property: ConfigTitle("Theme Type")]
        [property: ConfigDesc("The theme to use. Auto follows the system theme.")]
        private ConfigurationThemeType _themeType = ConfigurationThemeType.Auto;

        // ---- 反编译 ----
        [ObservableProperty]
        [property: ConfigTitle("Use Managed over IL2CPP")]
        [property: ConfigDesc("Use the Managed folder if it exists, rather than use CPP2IL.")]
        private bool _useManagedOverIl2cpp = false;

        [ObservableProperty]
        [property: ConfigTitle("Use Cpp2Il")]
        [property: ConfigDesc("Legacy flag retained for backward compatibility. When true, CPP2IL is preferred for IL2CPP assemblies.")]
        private bool _useCpp2Il = true;

        // ---- 文件名长度 ----
        [ObservableProperty]
        [property: ConfigTitle("Listing Filename Length Limit")]
        [property: ConfigDesc("Maximum length for the asset name when generating asset list.")]
        [property: ConfigRange(0, int.MaxValue)]
        private int _listingNameLength = 300;

        [ObservableProperty]
        [property: ConfigTitle("Export Filename Length Limit")]
        [property: ConfigDesc("Maximum length for the asset name when exporting assets.")]
        [property: ConfigRange(0, int.MaxValue)]
        private int _exportNameLength = 150;

        // ---- 导入导出行为 ----
        [ObservableProperty]
        [property: ConfigTitle("Export/Import Plain Filenames")]
        [property: ConfigDesc("Export as a name only, instead of a name + source file + path ID.\nTwo assets with the same name will cause issues, use at your own risk!")]
        private bool _exportImportJustNames = false;

        [ObservableProperty]
        [property: ConfigTitle("Load Container Paths")]
        [property: ConfigDesc("Load container paths, which may take a while when loading many assets.")]
        private bool _loadContainerPaths = true;

        [ObservableProperty]
        [property: ConfigTitle("Full Crop Sprites")]
        [property: ConfigDesc("When unchecked, sprites are placed on a virtual canvas with padding. When checked, sprites are fully cropped.")]
        private bool _fullCropSprites = true;

        /// <summary>
        /// 向后兼容：旧代码 (MainWindow.axaml.cs) 通过 UseDarkTheme 切换主题。
        /// 映射到 ThemeType：true -> Dark, false -> Light。Auto 状态下读取时返回
        /// 实际生效值（Dark 或 Light，由外部订阅者决定）。
        /// </summary>
        [JsonIgnore]
        public bool UseDarkTheme
        {
            get => ThemeType == ConfigurationThemeType.Dark;
            set => ThemeType = value ? ConfigurationThemeType.Dark : ConfigurationThemeType.Light;
        }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class ConfigTitle(string title) : Attribute
    {
        public string Title { get; } = title;
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class ConfigDesc(string description) : Attribute
    {
        public string Description { get; } = description;
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class ConfigRange(int min, int max) : Attribute
    {
        public int Minimum { get; } = min;
        public int Maximum { get; } = max;
    }
}
