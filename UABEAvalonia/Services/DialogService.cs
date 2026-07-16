using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace UABEAvalonia
{
    /// <summary>
    /// IDialogService 的桌面端实现，封装 Avalonia StorageProvider 和现有 MessageBox。
    /// Android 端可提供基于 overlay 的替代实现。
    /// </summary>
    public class DialogService : IDialogService
    {
        private Window? _mainWindow;

        public DialogService(Window? mainWindow = null)
        {
            _mainWindow = mainWindow;
        }

        public void SetMainWindow(Window window)
        {
            _mainWindow = window;
        }

        private Window GetActiveWindow()
        {
            if (_mainWindow != null)
                return _mainWindow;

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                return desktop.MainWindow ?? throw new InvalidOperationException("No main window available");
            }

            throw new InvalidOperationException("No desktop lifetime available");
        }

        public async Task<bool> ShowDialog<T>(T context) where T : class
        {
            // P1 Docking 改造时实现具体的 ViewModel -> View 映射。
            // 当前阶段仅提供基础设施，不强制现有窗口迁移。
            await Task.CompletedTask;
            return false;
        }

        public async Task<int> ShowMessageDialog(string title, string message, string[]? buttons = null)
        {
            Window win = GetActiveWindow();
            if (buttons == null || buttons.Length <= 1)
            {
                MessageBoxResult res = await MessageBoxUtil.ShowDialog(win, title, message);
                return (int)res;
            }

            // 多按钮用 Custom 模式（最多支持 3 个）
            string chosen = await MessageBoxUtil.ShowDialogCustom(win, title, message, buttons);
            for (int i = 0; i < buttons.Length && i < 3; i++)
            {
                if (buttons[i] == chosen)
                    return i;
            }
            return -1;
        }

        public async Task<List<string>> ShowOpenFileDialog(
            string title = "",
            string? directory = null,
            List<string>? extensions = null,
            bool multiSelect = false)
        {
            Window win = GetActiveWindow();
            IStorageProvider sp = win.StorageProvider;

            var opts = new FilePickerOpenOptions
            {
                Title = string.IsNullOrEmpty(title) ? "Open" : title,
                AllowMultiple = multiSelect,
                FileTypeFilter = BuildFileTypeFilter(extensions)
            };

            if (directory != null)
            {
                IStorageFolder? folder = await sp.TryGetFolderFromPathAsync(directory);
                if (folder != null)
                    opts.SuggestedStartLocation = folder;
            }

            IReadOnlyList<IStorageFile> files = await sp.OpenFilePickerAsync(opts);
            if (files == null || files.Count == 0)
                return new List<string>();

            return FileDialogUtils.GetOpenFileDialogFiles(files).ToList();
        }

        public async Task<string> ShowSaveFileDialog(
            string title = "",
            string? directory = null,
            string? defaultFileName = null,
            List<string>? extensions = null)
        {
            Window win = GetActiveWindow();
            IStorageProvider sp = win.StorageProvider;

            var opts = new FilePickerSaveOptions
            {
                Title = string.IsNullOrEmpty(title) ? "Save" : title,
                DefaultExtension = extensions?.FirstOrDefault(),
                FileTypeChoices = BuildFileTypeFilter(extensions),
                SuggestedFileName = defaultFileName
            };

            if (directory != null)
            {
                IStorageFolder? folder = await sp.TryGetFolderFromPathAsync(directory);
                if (folder != null)
                    opts.SuggestedStartLocation = folder;
            }

            IStorageFile? file = await sp.SaveFilePickerAsync(opts);
            string? path = FileDialogUtils.GetSaveFileDialogFile(file);
            return path ?? string.Empty;
        }

        public async Task<string> ShowOpenFolderDialog(string title = "", string? directory = null)
        {
            Window win = GetActiveWindow();
            IStorageProvider sp = win.StorageProvider;

            var opts = new FolderPickerOpenOptions
            {
                Title = string.IsNullOrEmpty(title) ? "Select Folder" : title,
                AllowMultiple = false
            };

            if (directory != null)
            {
                IStorageFolder? folder = await sp.TryGetFolderFromPathAsync(directory);
                if (folder != null)
                    opts.SuggestedStartLocation = folder;
            }

            IReadOnlyList<IStorageFolder> folders = await sp.OpenFolderPickerAsync(opts);
            if (folders == null || folders.Count == 0)
                return string.Empty;

            string[] paths = FileDialogUtils.GetOpenFolderDialogFiles(folders);
            return paths.Length > 0 ? paths[0] : string.Empty;
        }

        private static List<FilePickerFileType>? BuildFileTypeFilter(List<string>? extensions)
        {
            if (extensions == null || extensions.Count == 0)
                return null;

            var allTypes = new List<FilePickerFileType>();
            foreach (string ext in extensions)
            {
                string cleanExt = ext.TrimStart('.');
                allTypes.Add(new FilePickerFileType($"{cleanExt} files")
                {
                    Patterns = new[] { $"*.{cleanExt}" }
                });
            }
            return allTypes;
        }
    }
}
