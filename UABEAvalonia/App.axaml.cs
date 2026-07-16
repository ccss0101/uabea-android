using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.ComponentModel;
using UABEAvalonia.Plugins;
using UABEAvalonia.ViewModels;

namespace UABEAvalonia
{
    public class App : Application
    {
        public static IServiceProvider Services { get; private set; } = new ServiceCollection().BuildServiceProvider();

        /// <summary>
        /// 是否启用 Dock 多文档界面入口。
        /// 通过环境变量 UABEA_USE_DOCK=1 / true 切换到 DockMainWindow，
        /// 否则保持现有 MainWindow 作为默认入口（向后兼容）。
        /// </summary>
        public static bool UseDockInterface { get; } =
            IsDockFlagEnabled(Environment.GetEnvironmentVariable("UABEA_USE_DOCK"));

        private static bool IsDockFlagEnabled(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }
            return value.Equals("1", StringComparison.OrdinalIgnoreCase)
                || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
            // P2-C：启动时按配置应用主题（不再硬编码 Light）
            ThemeHandler.ApplyConfigurationTheme(ConfigurationManager.Settings.ThemeType);

            // 订阅 ThemeType 变更：用户在 Settings 中切换主题后立即生效
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
            ConfigureServices();

#if ANDROID
            if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
            {
                singleView.MainView = new MainWindow();
            }
#else
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // 按环境变量选择主窗口入口，保留旧版 MainWindow 作为默认。
                if (UseDockInterface)
                {
                    var dockWindow = new DockMainWindow();
                    // 从 DI 取 MainViewModel（构造时已完成 Dock 布局初始化）
                    var mainVm = Services.GetService<MainViewModel>();
                    if (mainVm != null)
                    {
                        dockWindow.SetViewModel(mainVm);
                    }
                    desktop.MainWindow = dockWindow;

                    var dialogService = Services.GetService<IDialogService>() as DialogService;
                    dialogService?.SetMainWindow(dockWindow);
                }
                else
                {
                    desktop.MainWindow = new MainWindow();

                    var dialogService = Services.GetService<IDialogService>() as DialogService;
                    dialogService?.SetMainWindow(desktop.MainWindow);
                }
            }
#endif
            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>
        /// 配置 DI 容器，注册平台无关服务与桌面端实现。
        /// Android 端可在自己的 App.axaml.cs 中覆盖此方法注册触屏实现。
        /// </summary>
        protected virtual void ConfigureServices()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IDialogService, DialogService>();
            // 新版插件加载器（与旧版 PluginManager 并行，供 Dock PreviewerTool 使用）
            services.AddSingleton<PluginLoader>();
            // P1 Docking：注册 MainViewModel（Workspace + MainDockFactory 在其构造函数内完成初始化）
            // MainViewModel 通过 DI 注入 PluginLoader，再转发给 PreviewerToolViewModel。
            services.AddSingleton<MainViewModel>();
            Services = services.BuildServiceProvider();

            // 扫描并加载 plugins 目录下的插件 dll。
            // 与旧版 PluginManager（InfoWindow 使用）互不影响。
            // 加载失败不应阻断主程序启动，故用 try-catch 兜底。
            try
            {
                var pluginLoader = Services.GetRequiredService<PluginLoader>();
                pluginLoader.LoadPluginsInDirectory("plugins");
            }
            catch
            {
                // 插件目录加载失败时忽略，主程序继续运行
            }
        }
    }
}
