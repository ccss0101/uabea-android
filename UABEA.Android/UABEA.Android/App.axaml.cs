using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using System;
using System.ComponentModel;
using UABEAvalonia;

namespace UABEA.Android
{
    public class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
            // P3-A 适配 Android：启动时按配置应用主题（不再硬编码 Light）
            ThemeHandler.ApplyConfigurationTheme(ConfigurationManager.Settings.ThemeType);

            // 订阅 ThemeType 变更：用户在 Settings 面板切换主题后立即生效
            ConfigurationManager.Settings.PropertyChanged += OnSettingsPropertyChanged;
        }

        private static void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ConfigurationValues.ThemeType))
            {
                ThemeHandler.ApplyConfigurationTheme(ConfigurationManager.Settings.ThemeType);
            }
        }

        public override void OnFrameworkInitializationCompleted()
        {
            try
            {
                if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
                {
                    // 如果 OnCreate 阶段崩溃了，直接显示崩溃界面
                    if (CrashLogger.IsCrashed)
                    {
                        singleView.MainView = new CrashView();
                    }
                    else
                    {
                        singleView.MainView = new MainView();
                    }
                }
                base.OnFrameworkInitializationCompleted();
            }
            catch (Exception ex)
            {
                CrashLogger.Log("App.OnFrameworkInitializationCompleted", ex);
                // 崩溃了直接写剪贴板，不依赖 UI 能不能起来
                CrashLogger.CopyCrashToClipboard(
                    CrashLogger.BuildCrashReport("App.OnFrameworkInitializationCompleted", ex));
                // 尝试最后兜底：切换到崩溃视图
                try
                {
                    if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
                        singleView.MainView = new CrashView();
                }
                catch { }
            }
        }
    }
}
