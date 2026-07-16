using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UABEAvalonia
{
    /// <summary>
    /// 主题切换处理器。
    /// P2-C 改造：新增 <see cref="ApplyConfigurationTheme"/>，
    /// 将 <see cref="ConfigurationThemeType"/> 映射到 Avalonia ThemeVariant；
    /// 并由 App.axaml.cs 在启动时与 Settings.ThemeType 变更时调用。
    /// 保留 <see cref="UseDarkTheme"/> 静态属性以兼容旧菜单 toggle 调用。
    /// </summary>
    public static class ThemeHandler
    {
        private static bool _useDarkTheme;
        public static bool UseDarkTheme
        {
            get
            {
                return _useDarkTheme;
            }
            set
            {
                if (Application.Current == null)
                    return;

                Application.Current.RequestedThemeVariant = value ? ThemeVariant.Dark : ThemeVariant.Light;
                _useDarkTheme = value;
            }
        }

        /// <summary>
        /// 根据 <see cref="ConfigurationThemeType"/> 应用主题。
        /// Auto -> ThemeVariant.Default（跟随系统），Light/Dark -> 对应 ThemeVariant。
        /// 同步更新内部 <see cref="UseDarkTheme"/> 状态以保持向后兼容。
        /// </summary>
        public static void ApplyConfigurationTheme(ConfigurationThemeType themeType)
        {
            if (Application.Current == null)
                return;

            Application.Current.RequestedThemeVariant = themeType switch
            {
                ConfigurationThemeType.Auto => ThemeVariant.Default,
                ConfigurationThemeType.Light => ThemeVariant.Light,
                ConfigurationThemeType.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };

            // Auto 模式下 _useDarkTheme 不能简单判定，这里仅记录 Light/Dark 二值
            _useDarkTheme = themeType == ConfigurationThemeType.Dark;
        }
    }
}
