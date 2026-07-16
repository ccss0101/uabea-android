using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace UABEAvalonia.Plugins
{
    /// <summary>
    /// <see cref="IUavPluginFunctions"/> 的实现。参考自 UABEANext4 的 UavPluginFunctions，
    /// 但通过 <see cref="IServiceProvider"/> 获取 <see cref="IDialogService"/>，
    /// 并用 <see cref="Lazy{T}"/> 延迟初始化——主窗口在 App 启动后才可用，
    /// 插件构造时不应立即解析对话框服务。
    ///
    /// 注意：本文件同时被桌面端与 Android 端编译（UABEA.Android 通过源码链接引入）。
    /// - 桌面端提供无参构造函数，从 <c>App.Services</c> 解析服务，供插件选项反射调用。
    /// - Android 端尚未配置 DI，无参构造被 <c>#if !ANDROID</c> 隐藏，
    ///   需通过带 <see cref="IServiceProvider"/> 的构造函数显式注入。
    /// 服务解析使用 <see cref="IServiceProvider.GetService(Type)"/>（BCL 实例方法），
    /// 避免依赖 Microsoft.Extensions.DependencyInjection 扩展包。
    /// </summary>
    public class UavPluginFunctions : IUavPluginFunctions
    {
        private readonly Lazy<IDialogService> _dialogService;

#if !ANDROID
        /// <summary>
        /// 无参构造：从桌面端 <c>App.Services</c> 解析服务。
        /// 供插件选项执行（<see cref="IUavPluginOption.Execute"/>）反射调用，
        /// 对应参考实现 <c>new UavPluginFunctions()</c> 的用法。
        /// </summary>
        public UavPluginFunctions()
            : this(App.Services)
        {
        }
#endif

        /// <summary>
        /// 通过指定 <see cref="IServiceProvider"/> 构造，便于测试 / Android 端注入。
        /// </summary>
        public UavPluginFunctions(IServiceProvider services)
        {
            _dialogService = new Lazy<IDialogService>(() =>
                (IDialogService?)services.GetService(typeof(IDialogService))
                ?? throw new InvalidOperationException(
                    "IDialogService 未在服务容器中注册，无法初始化 UavPluginFunctions。"));
        }

        public Task<List<string>> ShowOpenFileDialog(
            string title = "",
            string? directory = null,
            List<string>? extensions = null,
            bool multiSelect = false)
        {
            return _dialogService.Value.ShowOpenFileDialog(title, directory, extensions, multiSelect);
        }

        public Task<string> ShowSaveFileDialog(
            string title = "",
            string? directory = null,
            string? defaultFileName = null,
            List<string>? extensions = null)
        {
            return _dialogService.Value.ShowSaveFileDialog(title, directory, defaultFileName, extensions);
        }

        public Task<string> ShowOpenFolderDialog(string title = "", string? directory = null)
        {
            return _dialogService.Value.ShowOpenFolderDialog(title, directory);
        }

        public Task<int> ShowMessageDialog(string title, string message, string[]? buttons = null)
        {
            return _dialogService.Value.ShowMessageDialog(title, message, buttons);
        }
    }
}
