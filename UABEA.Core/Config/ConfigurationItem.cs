using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace UABEAvalonia
{
    /// <summary>
    /// 配置项抽象基类：包装一个 [ConfigTitle] 标记的属性，
    /// 提供给 SettingsView 数据绑定的 Title/Description/Value。
    /// 与 UABEANext 的 ConfigurationItemBase 等价。
    /// </summary>
    public abstract class ConfigurationItemBase : ObservableObject
    {
        public string Title { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;

        protected static (string, string?) GetBaseAttrs(PropertyInfo property)
        {
            var titleAttr = property.GetCustomAttribute<ConfigTitle>();
            var descAttr = property.GetCustomAttribute<ConfigDesc>();
            if (titleAttr is null)
            {
                throw new InvalidOperationException("Missing title for settings property");
            }

            return (titleAttr.Title, descAttr?.Description);
        }
    }

    /// <summary>
    /// 整数类型配置项，可选 [ConfigRange] 限定取值范围。
    /// </summary>
    public partial class ConfigurationIntegerItem : ConfigurationItemBase
    {
        private readonly PropertyInfo _property;

        [ObservableProperty] private int? _rangeMin;
        [ObservableProperty] private int? _rangeMax;

        public ConfigurationIntegerItem(PropertyInfo property)
        {
            _property = property;

            var (title, desc) = GetBaseAttrs(property);
            Title = title;
            Description = desc ?? "No description.";

            var range = property.GetCustomAttribute<ConfigRange>();
            RangeMin = range?.Minimum;
            RangeMax = range?.Maximum;

            ConfigurationManager.Settings.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == _property.Name)
                {
                    OnPropertyChanged(nameof(Value));
                }
            };
        }

        public int Value
        {
            get => (int)_property.GetValue(ConfigurationManager.Settings)!;
            set => _property.SetValue(ConfigurationManager.Settings, value);
        }
    }

    /// <summary>
    /// 布尔类型配置项。
    /// </summary>
    public class ConfigurationBooleanItem : ConfigurationItemBase
    {
        private readonly PropertyInfo _property;

        public ConfigurationBooleanItem(PropertyInfo property)
        {
            _property = property;

            var (title, desc) = GetBaseAttrs(property);
            Title = title;
            Description = desc ?? "No description.";

            ConfigurationManager.Settings.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == _property.Name)
                {
                    OnPropertyChanged(nameof(Value));
                }
            };
        }

        public bool Value
        {
            get => (bool)_property.GetValue(ConfigurationManager.Settings)!;
            set => _property.SetValue(ConfigurationManager.Settings, value);
        }
    }

    /// <summary>
    /// 枚举类型配置项，UI 用 ComboBox 列出所有枚举名。
    /// </summary>
    public partial class ConfigurationEnumItem : ConfigurationItemBase
    {
        private readonly PropertyInfo _property;
        private readonly Type _enumType;

        [ObservableProperty] private IReadOnlyList<string> _enumValues;

        public ConfigurationEnumItem(PropertyInfo property)
        {
            _property = property;
            _enumType = property.PropertyType;
            _enumValues = Enum.GetNames(_enumType).ToList().AsReadOnly();

            var (title, desc) = GetBaseAttrs(property);
            Title = title;
            Description = desc ?? "No description.";

            ConfigurationManager.Settings.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == _property.Name)
                {
                    OnPropertyChanged(nameof(Value));
                }
            };
        }

        public string Value
        {
            get => Enum.GetName(_enumType, _property.GetValue(ConfigurationManager.Settings)!)
                ?? "Unknown enum value";
            set
            {
                if (Enum.TryParse(_enumType, value, out var enumValue))
                {
                    _property.SetValue(ConfigurationManager.Settings, enumValue);
                }
                else
                {
                    var zeroValue = Enum.GetValues(_enumType).GetValue(0);
                    _property.SetValue(ConfigurationManager.Settings, zeroValue);
                }
            }
        }
    }
}
