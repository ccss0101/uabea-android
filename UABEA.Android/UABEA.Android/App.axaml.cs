using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using System;
using UABEAvalonia;

namespace UABEA.Android
{
    public class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
            Current.RequestedThemeVariant = ThemeVariant.Light;
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
