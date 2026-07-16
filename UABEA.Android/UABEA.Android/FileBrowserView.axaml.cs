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
    /// </summary>
    public partial class FileBrowserView : UserControl
    {
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

        /// <summary>选中的文件路径（确认后有效）</summary>
        public string? SelectedFilePath { get; private set; }

        /// <summary>是否已确认</summary>
        public bool Confirmed { get; private set; }

        /// <summary>文件过滤扩展名（小写无点，如 "bundle"。null 表示不过滤）</summary>
        public HashSet<string>? FilterExtensions { get; set; }

        /// <summary>确认选择事件</summary>
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

            // 默认起始目录
            _currentDir = GetDefaultStartDir();
        }

        /// <summary>设置起始目录并加载</summary>
        public void Initialize(string? startDir = null, HashSet<string>? extensions = null)
        {
            FilterExtensions = extensions;
            if (!string.IsNullOrEmpty(startDir) && Directory.Exists(startDir))
                _currentDir = startDir;
            else
                _currentDir = GetDefaultStartDir();
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
            btnConfirm.IsEnabled = false;

            try
            {
                pathBox.Text = _currentDir;
                titleText.Text = "选择文件 - " + _currentDir;

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

                foreach (var f in Directory.GetFiles(_currentDir))
                {
                    var name = Path.GetFileName(f);
                    if (string.IsNullOrEmpty(name)) continue;
                    if (name.StartsWith(".")) continue;

                    // 应用过滤
                    if (FilterExtensions != null && FilterExtensions.Count > 0)
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
                    btnConfirm.IsEnabled = false;
                }
                else if (!string.IsNullOrEmpty(entry.FullPath))
                {
                    _selectedFile = entry;
                    btnConfirm.IsEnabled = true;
                }
            }
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
                    ConfirmSelection();
                }
            }
        }

        private void ConfirmSelection()
        {
            if (_selectedFile == null) return;
            SelectedFilePath = _selectedFile.FullPath;
            Confirmed = true;
            FileSelected?.Invoke(this, SelectedFilePath);
        }
    }
}
