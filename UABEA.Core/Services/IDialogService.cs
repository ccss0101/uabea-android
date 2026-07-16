using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace UABEAvalonia
{
    /// <summary>
    /// 对话框服务抽象接口，平台无关。
    /// 主项目用 Avalonia 实现，Android 端可提供触屏 overlay 实现，
    /// 测试环境可提供 Dummy 实现。
    /// </summary>
    public interface IDialogService
    {
        /// <summary>显示一个实现了 IDialogAware 的对话框视图。</summary>
        Task<bool> ShowDialog<T>(T context) where T : class;

        /// <summary>显示消息框，返回用户选择的按钮索引。</summary>
        Task<int> ShowMessageDialog(
            string title,
            string message,
            string[]? buttons = null);

        /// <summary>打开文件选择对话框，返回所选文件路径列表（空表示取消）。</summary>
        Task<List<string>> ShowOpenFileDialog(
            string title = "",
            string? directory = null,
            List<string>? extensions = null,
            bool multiSelect = false);

        /// <summary>保存文件对话框，返回目标路径（空字符串表示取消）。</summary>
        Task<string> ShowSaveFileDialog(
            string title = "",
            string? directory = null,
            string? defaultFileName = null,
            List<string>? extensions = null);

        /// <summary>选择文件夹对话框，返回所选文件夹路径（空字符串表示取消）。</summary>
        Task<string> ShowOpenFolderDialog(
            string title = "",
            string? directory = null);
    }

    /// <summary>
    /// 对话框上下文协议，ViewModel 实现此接口以参与对话框交互。
    /// 标题、关闭逻辑等由具体 ViewModel 提供。
    /// </summary>
    public interface IDialogAware
    {
        string Title { get; }
        event EventHandler? RequestClose;
    }
}
