using System.Collections.Generic;
using System.Threading.Tasks;

namespace UABEAvalonia.Plugins
{
    /// <summary>
    /// 宿主提供给插件的服务接口。平台无关，方法签名复用自 <see cref="IDialogService"/>，
    /// 不引用任何 Avalonia 类型，便于插件在桌面端与 Android 端共用。
    /// 桌面端的实现可由 <see cref="IDialogService"/> 直接代理。
    /// </summary>
    public interface IUavPluginFunctions
    {
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

        /// <summary>显示消息框，返回用户选择的按钮索引。</summary>
        Task<int> ShowMessageDialog(
            string title,
            string message,
            string[]? buttons = null);
    }
}
