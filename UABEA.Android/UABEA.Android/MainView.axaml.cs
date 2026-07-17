using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UABEAvalonia;

namespace UABEA.Android
{
    /// <summary>
    /// UABEA Android 文字翻译工具主视图(竖屏)。
    /// 流程:选目录 → 扫描所有 Unity 文件 → 提取 TextAsset+MonoBehaviour 字符串
    ///       → 列表展示 → 应用内编辑 或 导出/导入 TXT → 保存到 _edited 子目录。
    /// </summary>
    public partial class MainView : UserControl
    {
        private AssetsManager _am = new AssetsManager();

        /// <summary>所有文字条目(可被搜索过滤;_allEntries 始终为全集)</summary>
        private ObservableCollection<TextEntry> _entries = new();
        private List<TextEntry> _allEntries = new();

        /// <summary>已加载的所有 Unity 文件信息(用于保存时定位)</summary>
        private List<LoadedUnityFile> _loadedFiles = new();

        /// <summary>当前扫描的根目录</summary>
        private string _rootDir = "";

        /// <summary>当前选中的条目</summary>
        private TextEntry? _selectedEntry;

        /// <summary>当前挂起的文件选择动作</summary>
        private enum PendingAction
        {
            None,
            OpenDirectory,      // 选扫描目录
            SaveAllDir,         // 保存全部选输出目录
            ExportTxtFile,      // 导出 TXT 选文件
            ImportTxtFile       // 导入 TXT 选文件
        }
        private PendingAction _pending = PendingAction.None;

        public MainView()
        {
            InitializeComponent();

            entryList.ItemsSource = _entries;
            entryList.SelectionChanged += EntryList_SelectionChanged;

            btnOpen.Click += BtnOpen_Click;
            btnSaveAll.Click += BtnSaveAll_Click;
            btnExportTxt.Click += BtnExportTxt_Click;
            btnImportTxt.Click += BtnImportTxt_Click;

            btnPrevEntry.Click += BtnPrevEntry_Click;
            btnNextEntry.Click += BtnNextEntry_Click;
            btnCopyToTrans.Click += BtnCopyToTrans_Click;
            btnApplyEntry.Click += BtnApplyEntry_Click;

            btnClearSearch.Click += (s, e) => { searchBox.Text = ""; };
            searchBox.TextChanged += (s, e) => ApplySearchFilter();
            translatedBox.TextChanged += (s, e) => UpdateEditStatus();

            Loaded += MainView_Loaded;
        }

        private void MainView_Loaded(object? sender, RoutedEventArgs e)
        {
            try
            {
                var cdPath = AppPaths.ClassDataPath;
                if (File.Exists(cdPath))
                {
                    _am.LoadClassPackage(cdPath);
                    Log($"classdata.tpk 已加载: {cdPath}");
                    statusText.Text = "就绪 - 点击\"打开\"选择文件目录";
                }
                else
                {
                    Log($"警告:classdata.tpk 未找到: {cdPath}");
                    statusText.Text = "警告:classdata.tpk 缺失,MonoBehaviour 可能无法解析";
                }
            }
            catch (Exception ex)
            {
                Log("初始化异常: " + ex);
            }
        }

        private void Log(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            try { CrashLogger.Log("MainView", msg); } catch { }
            logBox.Text = line + "\n" + logBox.Text;
            if (logBox.Text.Length > 5000) logBox.Text = logBox.Text.Substring(0, 5000);
        }

        private void ShowProgress(string msg)
        {
            progressText.Text = msg;
            progressOverlay.IsVisible = true;
        }

        private void HideProgress() => progressOverlay.IsVisible = false;

        // ==================== 1. 打开目录 ====================
        private void BtnOpen_Click(object? sender, RoutedEventArgs e)
        {
            _pending = PendingAction.OpenDirectory;
            ShowFileBrowser(FileBrowserView.BrowserMode.Directory);
        }

        // ==================== 2. 保存全部 ====================
        private void BtnSaveAll_Click(object? sender, RoutedEventArgs e)
        {
            int modifiedCount = _allEntries.Count(x => x.IsModified);
            if (modifiedCount == 0)
            {
                statusText.Text = "没有修改需要保存";
                return;
            }
            _pending = PendingAction.SaveAllDir;
            ShowFileBrowser(FileBrowserView.BrowserMode.Directory, startDir: _rootDir);
        }

        // ==================== 3. 导出/导入 TXT ====================
        private void BtnExportTxt_Click(object? sender, RoutedEventArgs e)
        {
            if (_allEntries.Count == 0) { statusText.Text = "无文字可导出"; return; }
            _pending = PendingAction.ExportTxtFile;
            ShowFileBrowser(FileBrowserView.BrowserMode.Save,
                extensions: new HashSet<string> { "txt" },
                suggestedName: $"uabea-export-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        }

        private void BtnImportTxt_Click(object? sender, RoutedEventArgs e)
        {
            _pending = PendingAction.ImportTxtFile;
            ShowFileBrowser(FileBrowserView.BrowserMode.Open,
                extensions: new HashSet<string> { "txt" });
        }

        // ==================== 文件浏览器 ====================
        private void ShowFileBrowser(FileBrowserView.BrowserMode mode,
            HashSet<string>? extensions = null, string? suggestedName = null, string? startDir = null)
        {
            fileBrowser.Initialize(startDir: startDir, extensions: extensions, mode: mode, suggestedFileName: suggestedName);
            fileBrowser.FileSelected -= FileBrowser_FileSelected;
            fileBrowser.Cancelled -= FileBrowser_Cancelled;
            fileBrowser.FileSelected += FileBrowser_FileSelected;
            fileBrowser.Cancelled += FileBrowser_Cancelled;
            fileBrowserOverlay.IsVisible = true;
        }

        private async void FileBrowser_FileSelected(object? sender, string? path)
        {
            fileBrowserOverlay.IsVisible = false;
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                switch (_pending)
                {
                    case PendingAction.OpenDirectory:
                        await ScanDirectoryAsync(path);
                        break;
                    case PendingAction.SaveAllDir:
                        await SaveAllToDirectoryAsync(path);
                        break;
                    case PendingAction.ExportTxtFile:
                        ExportToTxt(path);
                        break;
                    case PendingAction.ImportTxtFile:
                        await ImportFromTxtAsync(path);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log("操作失败: " + ex);
                statusText.Text = "操作失败: " + ex.Message;
            }
            finally
            {
                _pending = PendingAction.None;
            }
        }

        private void FileBrowser_Cancelled(object? sender, EventArgs e)
        {
            fileBrowserOverlay.IsVisible = false;
            _pending = PendingAction.None;
        }

        // ==================== 扫描目录 + 提取文字 ====================
        private async Task ScanDirectoryAsync(string dir)
        {
            _rootDir = dir;
            _allEntries.Clear();
            _entries.Clear();
            _loadedFiles.Clear();
            UnloadAllFiles();

            ShowProgress("扫描目录...");
            Log($"开始扫描: {dir}");

            // 找所有 Unity 资产文件
            var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bundle", "assets", "unity3d", "ab", "dat" };
            var files = await Task.Run(() =>
            {
                var result = new List<string>();
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
                    {
                        string ext = Path.GetExtension(f).TrimStart('.').ToLowerInvariant();
                        if (exts.Contains(ext))
                            result.Add(f);
                    }
                }
                catch (Exception ex) { Log("枚举文件异常: " + ex.Message); }
                return result;
            });

            Log($"找到 {files.Count} 个候选文件");
            topStatus.Text = $"扫描到 {files.Count} 个文件,正在提取文字...";

            // 逐个打开并提取
            await Task.Run(() =>
            {
                int processed = 0;
                foreach (var file in files)
                {
                    processed++;
                    Dispatcher.UIThread.Post(() =>
                    {
                        progressText.Text = $"提取 {processed}/{files.Count}\n{Path.GetFileName(file)}";
                    });
                    try
                    {
                        ExtractFromFile(file, dir);
                    }
                    catch (Exception ex)
                    {
                        Log($"跳过 {Path.GetFileName(file)}: {ex.Message}");
                    }
                }
            });

            ApplySearchFilter();
            UpdateListStats();
            HideProgress();

            Log($"扫描完成,共 {_allEntries.Count} 条文字");
            statusText.Text = $"扫描完成: {_allEntries.Count} 条文字 (来自 {_loadedFiles.Count} 个文件)";
            topStatus.Text = $"共 {_allEntries.Count} 条 · {_loadedFiles.Count} 文件";

            btnSaveAll.IsEnabled = _allEntries.Count > 0;
            btnExportTxt.IsEnabled = _allEntries.Count > 0;
            btnImportTxt.IsEnabled = _allEntries.Count > 0;
        }

        /// <summary>从单个 Unity 文件提取所有文字</summary>
        private void ExtractFromFile(string filePath, string rootDir)
        {
            var fileType = FileTypeDetector.DetectFileType(filePath);
            if (fileType != DetectedFileType.BundleFile && fileType != DetectedFileType.AssetsFile)
                return;

            string relPath = Path.GetRelativePath(rootDir, filePath);
            BundleFileInstance? bundleInst = null;
            BundleWorkspace? bundleWorkspace = null;
            List<AssetsFileInstance> fileInstances = new();

            if (fileType == DetectedFileType.BundleFile)
            {
                bundleInst = _am.LoadBundleFile(filePath, false);
                bundleWorkspace = new BundleWorkspace();
                bundleWorkspace.Reset(bundleInst);
                foreach (var name in bundleInst.file.BlockAndDirInfo.DirectoryInfos.Select(d => d.Name))
                {
                    try
                    {
                        var inst = _am.LoadAssetsFileFromBundle(bundleInst, name, true);
                        if (inst != null) fileInstances.Add(inst);
                    }
                    catch { }
                }
            }
            else
            {
                var inst = _am.LoadAssetsFile(filePath, true);
                fileInstances.Add(inst);
            }

            var loadedFile = new LoadedUnityFile
            {
                FilePath = filePath,
                RelativePath = relPath,
                FileType = fileType,
                BundleInstance = bundleInst,
                BundleWorkspace = bundleWorkspace,
                FileInstances = fileInstances,
                ModifiedAssetIds = new HashSet<AssetID>()
            };

            // 创建临时 workspace 用于按需加载 BaseField
            var assetWs = new AssetWorkspace(_am, bundleInst != null);
            foreach (var fi in fileInstances)
                assetWs.LoadAssetsFile(fi, false);

            foreach (var fi in fileInstances)
            {
                foreach (var info in fi.file.AssetInfos)
                {
                    try
                    {
                        int classId = info.TypeId;
                        // 只处理 TextAsset(49) 和 MonoBehaviour(114)
                        if (classId != 49 && classId != 114) continue;

                        var cont = new AssetContainer(info, fi);
                        var baseField = assetWs.GetBaseField(cont);
                        if (baseField == null) continue;

                        if (classId == 49)
                        {
                            // TextAsset:取 m_Script 大文本
                            var es = StringExtractor.ExtractTextAsset(baseField);
                            if (es != null && !string.IsNullOrEmpty(es.Original))
                            {
                                _allEntries.Add(new TextEntry
                                {
                                    FilePath = relPath,
                                    PathId = info.PathId,
                                    FieldPath = "m_Script",
                                    Original = es.Original,
                                    Translated = ""
                                });
                            }
                        }
                        else
                        {
                            // MonoBehaviour:递归提取所有字符串
                            var extractor = new StringExtractor();
                            extractor.Extract(baseField);
                            foreach (var s in extractor.Strings)
                            {
                                if (string.IsNullOrEmpty(s.Original)) continue;
                                // 跳过明显的代码字段(m_Name 在 MonoBehaviour 里一般是资产名,不翻译)
                                if (s.Path == "m_Name") continue;

                                _allEntries.Add(new TextEntry
                                {
                                    FilePath = relPath,
                                    PathId = info.PathId,
                                    FieldPath = s.Path,
                                    Original = s.Original,
                                    Translated = ""
                                });
                            }
                        }
                    }
                    catch { /* 单个资产失败跳过 */ }
                }
            }

            _loadedFiles.Add(loadedFile);
        }

        private void UnloadAllFiles()
        {
            try
            {
                _am.UnloadAllAssetsFiles(true);
                _am.UnloadAllBundleFiles();
            }
            catch { }
        }

        // ==================== 列表 / 搜索 / 选中 ====================
        private void ApplySearchFilter()
        {
            string keyword = searchBox.Text?.Trim() ?? "";
            var filtered = string.IsNullOrEmpty(keyword)
                ? _allEntries
                : _allEntries.Where(e =>
                    e.Original.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    e.Translated.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    e.FieldPath.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    e.FilePath.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();

            _entries.Clear();
            foreach (var e in filtered)
                _entries.Add(e);

            resultCount.Text = _allEntries.Count > 0
                ? $"显示 {filtered.Count} / {_allEntries.Count}"
                : "";
            UpdateListStats();
        }

        private void UpdateListStats()
        {
            int total = _allEntries.Count;
            int modified = _allEntries.Count(x => x.IsModified);
            listStats.Text = total > 0 ? $"{total} 条 · 已改 {modified}" : "";
        }

        private void EntryList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (entryList.SelectedItem is TextEntry entry)
            {
                _selectedEntry = entry;
                originalBox.Text = entry.Original;
                translatedBox.Text = entry.Translated;
                editStatus.Text = $"{entry.FilePath}#{entry.PathId}";
            }
        }

        // ==================== 编辑操作 ====================
        private void BtnCopyToTrans_Click(object? sender, RoutedEventArgs e)
        {
            if (_selectedEntry == null) return;
            translatedBox.Text = _selectedEntry.Original;
        }

        private void BtnApplyEntry_Click(object? sender, RoutedEventArgs e)
        {
            if (_selectedEntry == null) { statusText.Text = "请先选中条目"; return; }
            _selectedEntry.Translated = translatedBox.Text ?? "";
            UpdateListStats();
            statusText.Text = "已应用当前条目译文";

            // 触发列表刷新(让译文显示在左侧)
            int idx = _entries.IndexOf(_selectedEntry);
            if (idx >= 0)
            {
                var tmp = _entries[idx];
                _entries[idx] = tmp; // 直接重赋值触发绑定刷新
            }
        }

        private void BtnPrevEntry_Click(object? sender, RoutedEventArgs e)
        {
            int idx = entryList.SelectedIndex;
            if (idx > 0) entryList.SelectedIndex = idx - 1;
        }

        private void BtnNextEntry_Click(object? sender, RoutedEventArgs e)
        {
            int idx = entryList.SelectedIndex;
            if (idx >= 0 && idx < _entries.Count - 1) entryList.SelectedIndex = idx + 1;
        }

        private void UpdateEditStatus()
        {
            if (_selectedEntry == null) return;
            string orig = originalBox.Text ?? "";
            string trans = translatedBox.Text ?? "";
            if (trans == orig)
                editStatus.Text = "译文=原文(未修改)";
            else if (string.IsNullOrEmpty(trans))
                editStatus.Text = "译文为空";
            else
                editStatus.Text = "已修改(待应用)";
        }

        // ==================== 保存到目录 ====================
        private async Task SaveAllToDirectoryAsync(string outDir)
        {
            // 收集每个文件的修改
            var modifiedEntries = _allEntries.Where(x => x.IsModified).ToList();
            if (modifiedEntries.Count == 0)
            {
                statusText.Text = "没有修改";
                return;
            }

            // 按文件分组:同一文件里的多处修改一次写回
            var byFile = modifiedEntries.GroupBy(x => x.FilePath).ToList();
            Log($"保存:{modifiedEntries.Count} 条修改,涉及 {byFile.Count} 个文件");

            ShowProgress("保存中...");

            // 重新加载文件,应用修改并写出
            int savedFiles = await Task.Run(() =>
            {
                int count = 0;
                foreach (var group in byFile)
                {
                    try
                    {
                        SaveOneFile(group.Key, group.ToList(), outDir);
                        count++;
                        Dispatcher.UIThread.Post(() => { progressText.Text = $"保存 {count}/{byFile.Count}"; });
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.UIThread.Post(() => Log($"保存失败 {group.Key}: {ex.Message}"));
                    }
                }
                return count;
            });

            HideProgress();
            Log($"保存完成: {savedFiles}/{byFile.Count} 个文件 → {outDir}");
            statusText.Text = $"已保存 {savedFiles} 个文件到 {Path.GetFileName(outDir)}";
        }

        /// <summary>把单个文件的修改写回,输出到 outDir(保持原相对路径结构)</summary>
        private void SaveOneFile(string relPath, List<TextEntry> entries, string outDir)
        {
            string srcPath = Path.Combine(_rootDir, relPath);
            string destPath = Path.Combine(outDir, relPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            var fileType = FileTypeDetector.DetectFileType(srcPath);
            var am = new AssetsManager();
            var cdPath = AppPaths.ClassDataPath;
            if (File.Exists(cdPath)) am.LoadClassPackage(cdPath);

            // 按 PathId 分组:同资产的修改合并
            var byAsset = entries.GroupBy(e => e.PathId).ToList();

            if (fileType == DetectedFileType.BundleFile)
            {
                var bundleInst = am.LoadBundleFile(srcPath, false);
                var bw = new BundleWorkspace();
                bw.Reset(bundleInst);

                var dirInfos = bundleInst.file.BlockAndDirInfo.DirectoryInfos;
                foreach (var di in dirInfos)
                {
                    var inst = am.LoadAssetsFileFromBundle(bundleInst, di.Name, false);
                    if (inst == null) continue;
                    var replacers = BuildReplacersForFile(am, inst, byAsset);
                    if (replacers.Count > 0)
                    {
                        using var ms = new MemoryStream();
                        using var w = new AssetsFileWriter(ms);
                        inst.file.Write(w, 0, replacers);
                        bw.AddOrReplaceFile(new MemoryStream(ms.ToArray()), di.Name, true);
                    }
                }

                var bundleReplacers = bw.GetReplacers();
                using (var fs = File.Open(destPath, FileMode.Create))
                using (var w = new AssetsFileWriter(fs))
                    bundleInst.file.Write(w, bundleReplacers);
            }
            else if (fileType == DetectedFileType.AssetsFile)
            {
                var inst = am.LoadAssetsFile(srcPath, false);
                var ws = new AssetWorkspace(am, false);
                ws.LoadAssetsFile(inst, false);
                var replacers = BuildReplacersForFile(am, inst, byAsset);
                if (replacers.Count > 0)
                {
                    using var fs = File.Open(destPath, FileMode.Create);
                    using var w = new AssetsFileWriter(fs);
                    inst.file.Write(w, 0, replacers);
                }
                else
                {
                    // 没改动 .assets 也要复制原文件?不,只保存有改动的
                    File.Copy(srcPath, destPath, true);
                }
            }
        }

        /// <summary>为一个 .assets 文件构建所有 replacer</summary>
        private List<AssetsReplacer> BuildReplacersForFile(AssetsManager am, AssetsFileInstance inst,
            List<IGrouping<long, TextEntry>> byAsset)
        {
            var ws = new AssetWorkspace(am, false);
            ws.LoadAssetsFile(inst, false);
            var replacers = new List<AssetsReplacer>();

            foreach (var group in byAsset)
            {
                long pathId = group.Key;
                AssetFileInfo? info = null;
                foreach (var ai in inst.file.AssetInfos)
                {
                    if (ai.PathId == pathId) { info = ai; break; }
                }
                if (info == null) continue;

                var cont = new AssetContainer(info, inst);
                var baseField = ws.GetBaseField(cont);
                if (baseField == null) continue;

                int classId = info.TypeId;

                if (classId == 49)
                {
                    // TextAsset m_Script:单条
                    var entry = group.FirstOrDefault();
                    if (entry != null && entry.IsModified)
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes(entry.Translated);
                        baseField["m_Script"].AsByteArray = bytes;
                    }
                }
                else if (classId == 114)
                {
                    // MonoBehaviour:可能多处修改
                    var modifiedByPath = group.Where(x => x.IsModified)
                        .ToDictionary(x => x.FieldPath, x => x.Translated);
                    if (modifiedByPath.Count > 0)
                    {
                        ApplyTranslationsToField(baseField, "", modifiedByPath);
                    }
                }

                byte[] savedBytes = baseField.WriteToByteArray();
                replacers.Add(new AssetsReplacerFromMemory(
                    info.PathId, info.TypeId, info.ScriptTypeIndex, savedBytes));
            }
            return replacers;
        }

        /// <summary>递归把翻译应用到对应字段路径(与 StringExtractor 路径生成保持一致)</summary>
        private void ApplyTranslationsToField(AssetTypeValueField field, string currentPath,
            Dictionary<string, string> translations)
        {
            if (field == null) return;

            if (field.Value != null && field.Value.ValueType == AssetValueType.String)
            {
                if (translations.TryGetValue(currentPath, out string? val))
                {
                    field.AsString = val;
                }
                return;
            }

            if (field.Value != null && field.Value.ValueType == AssetValueType.ManagedReferencesRegistry)
            {
                var registry = field.AsManagedReferencesRegistry;
                if (registry?.references != null)
                {
                    for (int i = 0; i < registry.references.Count; i++)
                    {
                        ApplyTranslationsToField(registry.references[i].data,
                            $"{currentPath}[{i}].", translations);
                    }
                }
                return;
            }

            var template = field.TemplateField;
            bool isArray = template != null && template.IsArray;
            var children = field.Children;
            if (children == null || children.Count == 0) return;

            if (isArray)
            {
                for (int i = 0; i < children.Count; i++)
                    ApplyTranslationsToField(children[i], $"{currentPath}[{i}]", translations);
            }
            else
            {
                foreach (var child in children)
                {
                    if (child == null) continue;
                    string childName = child.TemplateField?.Name ?? "?";
                    string childPath = string.IsNullOrEmpty(currentPath) ? childName : $"{currentPath}.{childName}";
                    ApplyTranslationsToField(child, childPath, translations);
                }
            }
        }

        // ==================== 导出 / 导入 TXT ====================
        private void ExportToTxt(string path)
        {
            try
            {
                TextEntryTxtIo.ExportToFile(_allEntries, path);
                Log($"已导出 {_allEntries.Count} 条到 {path}");
                statusText.Text = $"已导出 {_allEntries.Count} 条 → {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                Log("导出失败: " + ex);
                statusText.Text = "导出失败: " + ex.Message;
            }
        }

        private async Task ImportFromTxtAsync(string path)
        {
            int applied = await Task.Run(() =>
            {
                var map = TextEntryTxtIo.ImportFromFile(path);
                int count = 0;
                foreach (var e in _allEntries)
                {
                    string key = $"{e.FilePath}::{e.PathId}::{e.FieldPath}";
                    if (map.TryGetValue(key, out string? translated))
                    {
                        if (!string.IsNullOrEmpty(translated) && translated != e.Original)
                        {
                            e.Translated = translated;
                            count++;
                        }
                    }
                }
                return count;
            });

            ApplySearchFilter();
            UpdateListStats();
            Log($"已导入翻译: {applied} 条");
            statusText.Text = $"已导入 {applied} 条翻译";

            // 刷新当前选中
            if (_selectedEntry != null)
            {
                originalBox.Text = _selectedEntry.Original;
                translatedBox.Text = _selectedEntry.Translated;
            }
        }
    }

    /// <summary>已加载的 Unity 文件信息</summary>
    internal class LoadedUnityFile
    {
        public string FilePath { get; set; } = "";
        public string RelativePath { get; set; } = "";
        public DetectedFileType FileType { get; set; }
        public BundleFileInstance? BundleInstance { get; set; }
        public BundleWorkspace? BundleWorkspace { get; set; }
        public List<AssetsFileInstance> FileInstances { get; set; } = new();
        public HashSet<AssetID> ModifiedAssetIds { get; set; } = new();
    }
}
