using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using UABEAvalonia;

namespace UABEA.Android
{
    /// <summary>
    /// 内置文件选择系统。
    /// 直接通过 System.IO 遍历存储目录，不调用系统 StorageProvider，
    /// 避免应用切到后台被回收（Android 上系统文件选择器是独立 Activity，
    /// 切换过程中 UABEA 进程容易被系统杀掉）。
    /// 支持三种模式：Open（选文件）、Save（选目录+输入文件名）、Directory（选目录）。
    /// </summary>
    public partial class FileBrowserView : UserControl
    {
        /// <summary>文件选择模式</summary>
        public enum BrowserMode
        {
            /// <summary>打开文件：选择一个已存在的文件</summary>
            Open,
            /// <summary>保存文件：选择目录并输入文件名</summary>
            Save,
            /// <summary>选择目录</summary>
            Directory
        }

        /// <summary>文件列表项</summary>
        public class FileEntry
        {
            public string Name { get; set; } = "";
            public string FullPath { get; set; } = "";
            public bool IsDirectory { get; set; }
            public string Icon => IsDirectory ? "\U0001F4C1" : "\U0001F4C4";
            public long Size { get; set; }
        }

        private ObservableCollection<FileEntry> _entries = new();
        private FileEntry? _selectedFile;
        private string _currentDir;
        private BrowserMode _mode = BrowserMode.Open;

        /// <summary>选中的文件路径（确认后有效）</summary>
        public string? SelectedFilePath { get; private set; }

        /// <summary>选中的目录路径（Directory 模式确认后有效）</summary>
        public string? SelectedDirectoryPath { get; private set; }

        /// <summary>是否已确认</summary>
        public bool Confirmed { get; private set; }

        /// <summary>文件过滤扩展名（小写无点，如 "bundle"。null 表示不过滤）</summary>
        public HashSet<string>? FilterExtensions { get; set; }

        /// <summary>确认选择事件（参数为最终路径：Open=文件路径，Save=目录/文件名完整路径，Directory=目录路径）</summary>
        public event EventHandler<string?>? FileSelected;

        /// <summary>取消事件</summary>
        public event EventHandler? Cancelled;

        public FileBrowserView()
        {
            InitializeComponent();
            fileList.ItemsSource = _entries;
            fileList.SelectionChanged += FileList_SelectionChanged;
            fileList.DoubleTapped += FileList_DoubleTapped;

            btnUp.Click += (s, e) => GoUp();
            btnHome.Click += (s, e) => GoHome();
            btnRefresh.Click += (s, e) => Refresh();
            btnCancel.Click += (s, e) => { Confirmed = false; Cancelled?.Invoke(this, EventArgs.Empty); };
            btnConfirm.Click += (s, e) => ConfirmSelection();

            fileNameBox.TextChanged += (s, e) => UpdateConfirmState();

            // 默认起始目录
            _currentDir = GetDefaultStartDir();
        }

        /// <summary>初始化文件选择器</summary>
        /// <param name="startDir">起始目录（null 用默认）</param>
        /// <param name="extensions">文件扩展名过滤（仅 Open 模式生效）</param>
        /// <param name="mode">选择模式</param>
        /// <param name="suggestedFileName">建议文件名（Save 模式下预填到 fileNameBox）</param>
        public void Initialize(string? startDir = null, HashSet<string>? extensions = null,
            BrowserMode mode = BrowserMode.Open, string? suggestedFileName = null)
        {
            _mode = mode;
            FilterExtensions = extensions;

            if (!string.IsNullOrEmpty(startDir) && Directory.Exists(startDir))
                _currentDir = startDir;
            else
                _currentDir = GetDefaultStartDir();

            // 根据模式调整 UI
            saveNamePanel.IsVisible = (mode == BrowserMode.Save);
            if (mode == BrowserMode.Save)
            {
                fileNameBox.Text = suggestedFileName ?? "";
                btnConfirm.Content = "保存";
                titleText.Text = "保存到...";
            }
            else if (mode == BrowserMode.Directory)
            {
                btnConfirm.Content = "选择此目录";
                titleText.Text = "选择目录";
            }
            else
            {
                btnConfirm.Content = "选择此文件";
                titleText.Text = "选择文件";
            }

            Refresh();
        }

        private static string GetDefaultStartDir()
        {
            // Android 存储路径优先级：
            // 1. /storage/emulated/0（内部存储根目录）
            // 2. /sdcard
            // 3. AppPaths.CacheDir 上两级
            // 4. 用户目录
            var candidates = new[]
            {
                "/storage/emulated/0",
                "/sdcard",
                "/storage",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                AppPaths.GetCacheDir()
            };
            foreach (var c in candidates)
            {
                if (!string.IsNullOrEmpty(c) && Directory.Exists(c))
                    return c;
            }
            return "/";
        }

        private void Refresh()
        {
            _entries.Clear();
            _selectedFile = null;
            UpdateConfirmState();

            try
            {
                pathBox.Text = _currentDir;

                // 先列出目录
                var dirs = new List<FileEntry>();
                var files = new List<FileEntry>();

                foreach (var d in Directory.GetDirectories(_currentDir))
                {
                    var name = Path.GetFileName(d);
                    if (string.IsNullOrEmpty(name)) continue;
                    // 跳过隐藏目录
                    if (name.StartsWith(".")) continue;
                    dirs.Add(new FileEntry { Name = name, FullPath = d, IsDirectory = true });
                }

                // Save 和 Directory 模式下也显示文件（方便用户定位），但 Open 模式才过滤
                foreach (var f in Directory.GetFiles(_currentDir))
                {
                    var name = Path.GetFileName(f);
                    if (string.IsNullOrEmpty(name)) continue;
                    if (name.StartsWith(".")) continue;

                    // 应用过滤（仅 Open 模式）
                    if (_mode == BrowserMode.Open && FilterExtensions != null && FilterExtensions.Count > 0)
                    {
                        var ext = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
                        if (!FilterExtensions.Contains(ext))
                            continue;
                    }

                    var fi = new FileInfo(f);
                    files.Add(new FileEntry { Name = name, FullPath = f, IsDirectory = false, Size = fi.Length });
                }

                // 目录在前，按名称排序
                foreach (var d in dirs.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                    _entries.Add(d);
                foreach (var f in files.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                    _entries.Add(f);
            }
            catch (UnauthorizedAccessException)
            {
                // 无权限访问该目录，尝试回退
                _entries.Add(new FileEntry { Name = "（无权限访问此目录）", FullPath = "", IsDirectory = false });
            }
            catch (Exception ex)
            {
                _entries.Add(new FileEntry { Name = "（读取失败: " + ex.Message + "）", FullPath = "", IsDirectory = false });
            }
        }

        private void GoUp()
        {
            try
            {
                var parent = Directory.GetParent(_currentDir);
                if (parent != null && parent.Exists)
                {
                    _currentDir = parent.FullName;
                    Refresh();
                }
                else if (_currentDir != "/")
                {
                    _currentDir = "/";
                    Refresh();
                }
            }
            catch { }
        }

        private void GoHome()
        {
            _currentDir = GetDefaultStartDir();
            Refresh();
        }

        private void FileList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (fileList.SelectedItem is FileEntry entry)
            {
                if (entry.IsDirectory)
                {
                    _selectedFile = null;
                }
                else if (!string.IsNullOrEmpty(entry.FullPath))
                {
                    _selectedFile = entry;
                    // Save 模式：点击文件时把文件名填入 fileNameBox（方便覆盖）
                    if (_mode == BrowserMode.Save)
                        fileNameBox.Text = entry.Name;
                }
            }
            UpdateConfirmState();
        }

        private void FileList_DoubleTapped(object? sender, RoutedEventArgs e)
        {
            if (fileList.SelectedItem is FileEntry entry)
            {
                if (entry.IsDirectory && !string.IsNullOrEmpty(entry.FullPath))
                {
                    _currentDir = entry.FullPath;
                    Refresh();
                }
                else if (!string.IsNullOrEmpty(entry.FullPath))
                {
                    _selectedFile = entry;
                    if (_mode == BrowserMode.Open)
                        ConfirmSelection();
                    else if (_mode == BrowserMode.Save)
                        fileNameBox.Text = entry.Name;
                }
            }
        }

        private void UpdateConfirmState()
        {
            switch (_mode)
            {
                case BrowserMode.Open:
                    btnConfirm.IsEnabled = _selectedFile != null;
                    break;
                case BrowserMode.Save:
                    btnConfirm.IsEnabled = !string.IsNullOrWhiteSpace(fileNameBox.Text);
                    break;
                case BrowserMode.Directory:
                    // Directory 模式：当前目录即为可选项
                    btnConfirm.IsEnabled = true;
                    break;
            }
        }

        private void ConfirmSelection()
        {
            switch (_mode)
            {
                case BrowserMode.Open:
                    if (_selectedFile == null) return;
                    SelectedFilePath = _selectedFile.FullPath;
                    Confirmed = true;
                    FileSelected?.Invoke(this, SelectedFilePath);
                    break;
                case BrowserMode.Save:
                    var name = fileNameBox.Text?.Trim();
                    if (string.IsNullOrEmpty(name)) return;
                    foreach (var c in Path.GetInvalidFileNameChars())
                        name = name.Replace(c, '_');
                    SelectedFilePath = Path.Combine(_currentDir, name);
                    Confirmed = true;
                    FileSelected?.Invoke(this, SelectedFilePath);
                    break;
                case BrowserMode.Directory:
                    SelectedDirectoryPath = _currentDir;
                    Confirmed = true;
                    FileSelected?.Invoke(this, _currentDir);
                    break;
            }
        }
    }
}
