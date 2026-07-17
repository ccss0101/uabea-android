using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UABEAvalonia;
using Image = SixLabors.ImageSharp.Image;

namespace UABEA.Android
{
    public partial class MainView : UserControl
    {
        private AssetsManager _am = new AssetsManager();
        private BundleFileInstance? _bundleInst;
        private BundleWorkspace? _workspace;
        private AssetsFileInstance? _standaloneAssetsInst;
        private AssetWorkspace? _assetWorkspace;
        // 搜索态
        private string _navSearchText = "";
        private int _navSearchStart;
        private bool _navSearchDown = true;
        private bool _navSearchCaseSensitive;
        private bool _navSearching;
        // 类型过滤
        private HashSet<int> _filteredOutClassIds = new();
        private ObservableCollection<AssetListItem> _items = new ObservableCollection<AssetListItem>();
        // 完整未过滤列表（搜索时据此过滤 _items）
        private List<AssetListItem> _allItems = new List<AssetListItem>();

        // 修改状态跟踪
        private bool _changesUnsaved;
        private bool _changesMade;

        // 弹出层相关
        private AssetListItem? _renameTarget;
        private string? _importTempPath;       // 导入文件复制到缓存的路径
        private string? _importOrigName;       // 要替换的原文件名（如果有）

        // 内置文件选择器的挂起操作（区分打开/导入/导出/保存等）
        private PendingFileAction _pendingFileAction = PendingFileAction.Open;
        private AssetListItem? _pendingExportItem;       // 单项导出时的目标项
        private AssetBundleCompressionType _pendingCompType; // 压缩保存时的目标类型
        private AssetListItem? _pendingDumpItem;         // Dump 保存时的目标项
        private string? _pendingTextContent;             // 保存文本时的内容

        // 预览/替换相关
        private AssetsFileInstance? _previewFileInst;
        private AssetFileInfo? _previewAssetInf;
        private bool _previewIsFromBundle;
        private string? _previewBundleEntryName;
        private TextureFormat _previewTexFormat;
        private int _previewTexWidth;
        private int _previewTexHeight;

        // 新视图相关：编辑数据 / 单项 Raw 导入 / Dump 批量导入
        private AssetContainer? _editDataTarget;
        private AssetListItem? _pendingImportRawItem;
        private List<AssetContainer> _pendingDumpSelection = new();
        private ImportDumpView.DumpFormat _pendingDumpFormat = ImportDumpView.DumpFormat.Text;

        /// <summary>内置文件选择器的挂起操作类型</summary>
        private enum PendingFileAction
        {
            Open,        // 打开 bundle/assets 文件
            Import,      // 导入替换文件
            ImportAll,   // 批量导入到 bundle
            BatchImportRaw, // 资产级批量导入 raw
            ImportRawAsset,  // 单项 Raw 导入
            ImportDumpDir,   // Dump 批量导入目录
            ExportItem,  // 导出单项
            ExportAll,   // 批量导出到目录
            SaveBundle,  // 保存 bundle
            CompressSave,// 压缩保存
            DumpSave,    // Dump 保存到文件
            ReplaceTextureOpen, // 选择替换贴图源文件
            SaveTextFile // 保存文本到文件
        }

        public MainView()
        {
            InitializeComponent();
            Loaded += MainView_Loaded;

            assetList.ItemsSource = _items;
            assetList.SelectionChanged += AssetList_SelectionChanged;

            btnOpen.Click += BtnOpen_Click;
            btnSave.Click += BtnSave_Click;
            btnCompress.Click += BtnCompress_Click;
            btnClose.Click += BtnClose_Click;
            btnExportAll.Click += BtnExportAll_Click;
            btnImportAll.Click += BtnImportAll_Click;
            btnBatchImportRaw.Click += BtnBatchImportRaw_Click;
            btnSearch.Click += BtnSearch_Click;
            btnContinueSearch.Click += BtnContinueSearch_Click;
            btnGoTo.Click += BtnGoTo_Click;
            btnFilterType.Click += BtnFilterType_Click;
            btnInfo.Click += BtnInfo_Click;
            btnExport.Click += BtnExport_Click;
            btnImport.Click += BtnImport_Click;
            btnDump.Click += BtnDump_Click;
            btnRename.Click += BtnRename_Click;
            btnRemove.Click += BtnRemove_Click;
            btnPreview.Click += BtnPreview_Click;
            btnPreview3D.Click += BtnPreview3D_Click;

            btnReplaceTexture.Click += BtnReplaceTexture_Click;
            btnSaveText.Click += BtnSaveText_Click;
            btnClosePreview.Click += (s, e) => { previewOverlay.IsVisible = false; };

            btnViewData.Click += BtnViewData_Click;
            btnEditData.Click += BtnEditData_Click;
            btnImportRaw.Click += BtnImportRaw_Click;
            btnImportDump.Click += BtnImportDump_Click;
            btnAddAsset.Click += BtnAddAsset_Click;
            btnHierarchy.Click += BtnHierarchy_Click;

            btnSettings.Click += BtnSettings_Click;
            btnSettingsClose.Click += (s, e) =>
            {
                settingsOverlay.IsVisible = false;
                // 关闭时立即保存，避免 500ms 防抖丢失最后一项变更
                ConfigurationManager.SaveConfig();
                Log("设置已保存");
            };

            renameOk.Click += RenameOk_Click;
            renameCancel.Click += RenameCancel_Click;

            importYes.Click += (s, e) => DoImport(true);
            importNo.Click += (s, e) => DoImport(false);
            importCancel.Click += (s, e) => { importOverlay.IsVisible = false; CleanupImport(); };

            compLz4.Click += (s, e) => DoCompress(AssetBundleCompressionType.LZ4);
            compLzma.Click += (s, e) => DoCompress(AssetBundleCompressionType.LZMA);
            compCancel.Click += (s, e) => { compressOverlay.IsVisible = false; };

            // 搜索框：输入时实时过滤资产列表
            searchBox.TextChanged += (s, e) => ApplySearchFilter();
        }

        // ==================== 设置面板 ====================
        // P2-C/P3-A 适配 Android：复用 ConfigurationManager + ConfigurationItem 反射系统，
        // 通过 overlay 而非 Window 展示（Android 单视图模式）。
        private void BtnSettings_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var items = ConfigurationManager.GetConfigurationItems();
                settingsItems.ItemsSource = items;
                settingsOverlay.IsVisible = true;
            }
            catch (Exception ex)
            {
                Log("打开设置异常: " + ex);
            }
        }

        private void MainView_Loaded(object? sender, RoutedEventArgs e)
        {
            try
            {
                Log("MainView Loaded");
                var cdPath = AppPaths.ClassDataPath;
                if (File.Exists(cdPath))
                {
                    _am.LoadClassPackage(cdPath);
                    Log($"classdata.tpk 已加载: {cdPath}");
                    statusText.Text = "就绪 - 点击\"打开\"选择 AssetBundle 或 .assets 文件";
                }
                else
                {
                    Log($"classdata.tpk 未找到: {cdPath}");
                    statusText.Text = "警告：classdata.tpk 缺失，部分功能不可用";
                }
            }
            catch (Exception ex)
            {
                Log("Loaded 异常: " + ex);
                statusText.Text = "初始化失败: " + ex.Message;
            }
        }

        private void Log(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            try { CrashLogger.Log("MainView", msg); } catch { }
            logBox.Text = line + "\n" + logBox.Text;
            if (logBox.Text.Length > 5000) logBox.Text = logBox.Text.Substring(0, 5000);
        }

        private void SetChanges(bool unsaved)
        {
            _changesUnsaved = unsaved;
            if (unsaved) _changesMade = true;
            UpdateStatusText();
            // 有修改且为 bundle 时启用保存按钮
            btnSave.IsEnabled = unsaved && _bundleInst != null;
        }

        private void UpdateStatusText()
        {
            if (_bundleInst != null)
            {
                var mark = _changesUnsaved ? " *" : "";
                statusText.Text = $"Bundle: {Path.GetFileName(_bundleInst.path)} ({_items.Count} 项){mark}";
            }
            else if (_standaloneAssetsInst != null)
            {
                statusText.Text = $".assets: {Path.GetFileName(_standaloneAssetsInst.path)} ({_items.Count} 项)";
            }
        }

        // ==================== 打开文件 ====================
        // 使用内置文件选择器，避免系统文件选择器（独立 Activity）导致应用掉后台被回收
        private void BtnOpen_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (_changesUnsaved && _bundleInst != null)
                {
                    Log("有未保存的修改，继续打开将丢弃");
                }

                _pendingFileAction = PendingFileAction.Open;
                ShowFileBrowser(
                    mode: FileBrowserView.BrowserMode.Open,
                    extensions: new HashSet<string> { "bundle", "assets", "dat", "unity3d", "ab", "" });
            }
            catch (Exception ex)
            {
                Log("打开文件选择器异常: " + ex);
                statusText.Text = "打开失败: " + ex.Message;
            }
        }

        /// <summary>显示内置文件浏览器 overlay 并绑定回调</summary>
        private void ShowFileBrowser(FileBrowserView.BrowserMode mode,
            HashSet<string>? extensions = null, string? suggestedName = null, string? startDir = null)
        {
            fileBrowser.Initialize(
                startDir: startDir,
                extensions: extensions,
                mode: mode,
                suggestedFileName: suggestedName);
            fileBrowser.FileSelected -= FileBrowser_FileSelected;
            fileBrowser.Cancelled -= FileBrowser_Cancelled;
            fileBrowser.FileSelected += FileBrowser_FileSelected;
            fileBrowser.Cancelled += FileBrowser_Cancelled;
            fileBrowserOverlay.IsVisible = true;
        }

        private async void FileBrowser_FileSelected(object? sender, string? path)
        {
            try
            {
                fileBrowserOverlay.IsVisible = false;
                if (string.IsNullOrEmpty(path))
                {
                    Log("未选择路径");
                    return;
                }

                switch (_pendingFileAction)
                {
                    case PendingFileAction.Open:
                        Log($"打开文件: {path}");
                        await OpenFile(path);
                        break;
                    case PendingFileAction.Import:
                        Log($"导入选择: {path}");
                        await OnImportFileSelected(path);
                        break;
                    case PendingFileAction.ExportItem:
                        Log($"导出到: {path}");
                        await DoExportToPath(_pendingExportItem, path);
                        break;
                    case PendingFileAction.ExportAll:
                        Log($"批量导出到目录: {path}");
                        await DoExportAllToDir(path);
                        break;
                    case PendingFileAction.ImportAll:
                        Log($"批量导入到目录: {path}");
                        await DoImportAllToDir(path);
                        break;
                    case PendingFileAction.BatchImportRaw:
                        Log($"资产级批量导入目录: {path}");
                        ShowImportBatchView(path);
                        break;
                    case PendingFileAction.ImportRawAsset:
                        Log($"Raw 导入: {path}");
                        await DoImportRawAsset(_pendingImportRawItem, path);
                        break;
                    case PendingFileAction.ImportDumpDir:
                        Log($"Dump 导入目录: {path}");
                        ShowImportDumpView(path, _pendingDumpSelection, _pendingDumpFormat);
                        break;
                    case PendingFileAction.SaveBundle:
                        Log($"保存到: {path}");
                        await DoSaveBundleToPath(path);
                        break;
                    case PendingFileAction.CompressSave:
                        Log($"压缩保存到: {path}");
                        await DoCompressSaveToPath(path, _pendingCompType);
                        break;
                    case PendingFileAction.DumpSave:
                        Log($"Dump 保存到: {path}");
                        await DoDumpToPath(_pendingDumpItem, path);
                        break;
                    case PendingFileAction.ReplaceTextureOpen:
                        Log($"替换贴图源: {path}");
                        await DoReplaceTextureFromPath(path);
                        break;
                    case PendingFileAction.SaveTextFile:
                        Log($"保存文本到: {path}");
                        await DoSaveTextToPath(path);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log("文件操作异常: " + ex);
                statusText.Text = "操作失败: " + ex.Message;
            }
        }

        private void FileBrowser_Cancelled(object? sender, EventArgs e)
        {
            fileBrowserOverlay.IsVisible = false;
        }

        /// <summary>合并 Unity .split 文件(.split0/.split1/...)为一个临时文件</summary>
        private async Task<string?> MergeSplitFiles(string split0Path)
        {
            try
            {
                string baseName = Path.GetFileNameWithoutExtension(split0Path);
                if (baseName.EndsWith(".split0", StringComparison.OrdinalIgnoreCase))
                    baseName = baseName.Substring(0, baseName.Length - ".split0".Length);
                string dir = Path.GetDirectoryName(split0Path) ?? "";
                var splitFiles = Directory.GetFiles(dir, baseName + ".split*")
                    .Where(f =>
                    {
                        string name = Path.GetFileName(f);
                        if (!name.StartsWith(baseName + ".split")) return false;
                        string suffix = name.Substring((baseName + ".split").Length);
                        return int.TryParse(suffix, out _);
                    })
                    .OrderBy(f => int.Parse(Path.GetFileName(f).Substring((baseName + ".split").Length)))
                    .ToList();

                if (splitFiles.Count == 0) return split0Path;

                Log($"检测到 .split 文件,合并 {splitFiles.Count} 个分片...");
                string tempPath = Path.Combine(AppPaths.GetCacheDir(), baseName);
                using (var outStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                {
                    foreach (var split in splitFiles)
                    {
                        using (var inStream = File.OpenRead(split))
                        {
                            await inStream.CopyToAsync(outStream);
                        }
                    }
                }
                Log($"合并完成: {Path.GetFileName(tempPath)}");
                return tempPath;
            }
            catch (Exception ex)
            {
                Log("合并 .split 失败: " + ex.Message);
                return null;
            }
        }

        private async Task OpenFile(string path)
        {
            try
            {
                // .split 文件合并(Unity 把大 bundle 分成 .split0/.split1/...)
                if (path.EndsWith(".split0", StringComparison.OrdinalIgnoreCase))
                {
                    var merged = await MergeSplitFiles(path);
                    if (merged == null) { Log("合并 .split 失败"); return; }
                    path = merged;
                }

                var fileType = FileTypeDetector.DetectFileType(path);
                Log($"文件类型: {fileType}");

                await CloseAllFilesInternal();

                if (fileType == DetectedFileType.BundleFile)
                {
                    _bundleInst = _am.LoadBundleFile(path, false);
                    _workspace = new BundleWorkspace();
                    _workspace.Reset(_bundleInst);

                    RefreshAssetListFromBundle();
                    UpdateStatusText();
                    SetBundleControlsEnabled(true, true);
                    Log($"Bundle 加载成功, {_items.Count} 个资产");
                }
                else if (fileType == DetectedFileType.AssetsFile)
                {
                    _standaloneAssetsInst = _am.LoadAssetsFile(path, true);
                    string uVer = _standaloneAssetsInst.file.Metadata.UnityVersion;
                    if (uVer == "0.0.0") uVer = "2019.4";
                    _am.LoadClassDatabaseFromPackage(uVer);

                    _assetWorkspace = new AssetWorkspace(_am, fromBundle: false);
                    _assetWorkspace.LoadAssetsFile(_standaloneAssetsInst, true);

                    RefreshAssetListFromAssetsFile(_standaloneAssetsInst);
                    UpdateStatusText();
                    SetBundleControlsEnabled(true, false);
                    Log($".assets 加载成功, {_items.Count} 个资产");
                }
                else
                {
                    statusText.Text = "不支持的文件类型";
                    Log("未知文件类型");
                }
            }
            catch (Exception ex)
            {
                Log("加载异常: " + ex);
                statusText.Text = "加载失败: " + ex.Message;
            }
        }

        private void RefreshAssetListFromBundle()
        {
            _items.Clear();
            _allItems.Clear();
            if (_bundleInst == null) return;

            foreach (var dirInf in _bundleInst.file.BlockAndDirInfo.DirectoryInfos)
            {
                var item = new AssetListItem
                {
                    Name = dirInf.Name,
                    SizeText = $"Size: {dirInf.DecompressedSize} bytes",
                    DirInfo = dirInf
                };
                _items.Add(item);
                _allItems.Add(item);
            }
            ApplySearchFilter();
        }

        private void RefreshAssetListFromAssetsFile(AssetsFileInstance fileInst)
        {
            _items.Clear();
            _allItems.Clear();
            var cldb = _am.ClassDatabase;

            if (_assetWorkspace != null && _assetWorkspace.LoadedAssets.Count > 0)
            {
                foreach (var kv in _assetWorkspace.LoadedAssets)
                {
                    var cont = kv.Value;
                    int classId = cont.ClassId;
                    string typeName;
                    try
                    {
                        var type = cldb.FindAssetClassByID(classId);
                        typeName = type != null ? cldb.GetString(type.Name) : $"0x{classId:X}";
                    }
                    catch
                    {
                        typeName = $"0x{classId:X}";
                    }

                    _items.Add(new AssetListItem
                    {
                        Name = $"{typeName} #{cont.PathId}",
                        TypeName = typeName,
                        SizeText = $"{cont.Size} bytes",
                        PathId = cont.PathId,
                        IsAsset = true,
                        FileInstance = cont.FileInstance,
                        Container = cont
                    });
                }
            }
            else
            {
                foreach (var inf in fileInst.file.AssetInfos)
                {
                    int classId = inf.TypeId;
                    string typeName;
                    try
                    {
                        var type = cldb.FindAssetClassByID(classId);
                        typeName = type != null ? cldb.GetString(type.Name) : $"0x{classId:X}";
                    }
                    catch
                    {
                        typeName = $"0x{classId:X}";
                    }

                    _items.Add(new AssetListItem
                    {
                        Name = $"{typeName} #{inf.PathId}",
                        TypeName = typeName,
                        SizeText = $"{inf.ByteSize} bytes",
                        PathId = inf.PathId,
                        IsAsset = true,
                        AssetInfo = inf,
                        FileInstance = fileInst
                    });
                }
            }

            _allItems.Clear();
            foreach (var i in _items) _allItems.Add(i);
            ApplySearchFilter();
        }

        /// <summary>根据搜索框文本过滤资产列表</summary>
        private void ApplySearchFilter()
        {
            var keyword = searchBox.Text ?? "";
            _items.Clear();
            foreach (var item in _allItems)
            {
                if (keyword.Length > 0 && item.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (item.Container != null && _filteredOutClassIds.Contains(item.Container.ClassId))
                    continue;
                _items.Add(item);
            }
        }

        private void AssetList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            bool hasSel = assetList.SelectedItems.Count > 0;
            bool isBundle = _bundleInst != null;
            bool isAssets = _standaloneAssetsInst != null;
            // 仅当选中真正的资产（非 bundle 条目）时启用资产级操作
            var firstSel = assetList.SelectedItems.OfType<AssetListItem>().FirstOrDefault();
            bool selIsAsset = hasSel && firstSel != null && firstSel.Container != null;
            // 是否打开过 .assets 文件（独立或 bundle 内均可）
            bool hasAssetsFile = _standaloneAssetsInst != null || _assetWorkspace != null;

            btnPreview.IsEnabled = hasSel;
            btnInfo.IsEnabled = hasSel;
            btnExport.IsEnabled = hasSel;
            btnImport.IsEnabled = hasSel && isBundle;
            btnDump.IsEnabled = hasSel;
            btnRename.IsEnabled = hasSel && isBundle;
            btnRemove.IsEnabled = hasSel && isBundle;
            btnBatchImportRaw.IsEnabled = hasSel && isAssets;
            btnSearch.IsEnabled = hasSel && isAssets;
            btnContinueSearch.IsEnabled = isAssets && _navSearching;
            btnGoTo.IsEnabled = hasSel && isAssets;
            btnFilterType.IsEnabled = isAssets;

            // 新视图按钮
            btnViewData.IsEnabled = selIsAsset;
            btnPreview3D.IsEnabled = selIsAsset;
            btnEditData.IsEnabled = selIsAsset;
            btnImportRaw.IsEnabled = selIsAsset;
            btnImportDump.IsEnabled = selIsAsset;
            btnAddAsset.IsEnabled = hasAssetsFile;
            btnHierarchy.IsEnabled = hasAssetsFile;
        }

        /// <summary>设置 bundle 相关按钮的启用状态</summary>
        private void SetBundleControlsEnabled(bool enabled, bool isBundle)
        {
            btnExportAll.IsEnabled = enabled && isBundle;
            btnImportAll.IsEnabled = enabled && isBundle;
            btnClose.IsEnabled = enabled;
            btnSave.IsEnabled = false; // 保存只在有修改时启用
            btnCompress.IsEnabled = enabled && isBundle;
            AssetList_SelectionChanged(null, null!);
        }

        // ==================== 导出 ====================
        // 使用内置文件选择器（Save 模式）选择导出目标，避免系统选择器导致掉后台
        private void BtnExport_Click(object? sender, RoutedEventArgs e)
        {
            var item = assetList.SelectedItems.OfType<AssetListItem>().FirstOrDefault();
            if (item == null) return;

            _pendingFileAction = PendingFileAction.ExportItem;
            _pendingExportItem = item;
            ShowFileBrowser(
                mode: FileBrowserView.BrowserMode.Save,
                suggestedName: Path.GetFileName(item.Name));
        }

        /// <summary>实际执行单项导出到指定路径</summary>
        private async Task DoExportToPath(AssetListItem? item, string outPath)
        {
            if (item == null) { Log("导出项为空"); return; }
            try
            {
                using var ms = GetItemStream(item);
                if (ms == null) { Log("无法读取数据"); return; }
                ms.Position = 0;

                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                using var fs = File.Open(outPath, FileMode.Create);
                await ms.CopyToAsync(fs);

                Log($"已导出: {outPath}");
                statusText.Text = "导出成功: " + Path.GetFileName(outPath);
            }
            catch (Exception ex)
            {
                Log("导出异常: " + ex);
                statusText.Text = "导出失败: " + ex.Message;
            }
        }

        // 批量导出：使用内置文件选择器（Directory 模式）选择目标目录
        private void BtnExportAll_Click(object? sender, RoutedEventArgs e)
        {
            if (_bundleInst == null) return;
            _pendingFileAction = PendingFileAction.ExportAll;
            ShowFileBrowser(mode: FileBrowserView.BrowserMode.Directory);
        }

        /// <summary>实际执行批量导出到指定目录</summary>
        private async Task DoExportAllToDir(string dir)
        {
            if (_bundleInst == null) return;
            try
            {
                Directory.CreateDirectory(dir);
                int count = 0;
                foreach (var dirInf in _bundleInst.file.BlockAndDirInfo.DirectoryInfos)
                {
                    string outPath = Path.Combine(dir, dirInf.Name);
                    Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

                    using var fs = File.Open(outPath, FileMode.Create);
                    var reader = _bundleInst.file.DataReader;
                    reader.Position = dirInf.Offset;
                    reader.BaseStream.CopyToCompat(fs, dirInf.DecompressedSize);
                    count++;
                }

                Log($"批量导出 {count} 项到: {dir}");
                statusText.Text = $"已导出 {count} 个文件到 {dir}";
            }
            catch (Exception ex)
            {
                Log("批量导出异常: " + ex);
                statusText.Text = "批量导出失败: " + ex.Message;
            }
        }

        // ===== 批量导入(bundle 级)=====
        private void BtnImportAll_Click(object? sender, RoutedEventArgs e)
        {
            if (_bundleInst == null) return;
            _pendingFileAction = PendingFileAction.ImportAll;
            ShowFileBrowser(mode: FileBrowserView.BrowserMode.Directory);
        }

        private async Task DoImportAllToDir(string dir)
        {
            if (_bundleInst == null || _workspace == null) return;
            try
            {
                var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList();
                if (files.Count == 0)
                {
                    statusText.Text = "未找到文件";
                    Log("批量导入:目录为空");
                    return;
                }

                progressBar.IsIndeterminate = false;
                progressBar.Maximum = files.Count;
                progressBar.Value = 0;
                progressText.Text = $"导入中 0/{files.Count}";
                progressOverlay.IsVisible = true;

                int success = 0, fail = 0;
                await Task.Run(() =>
                {
                    for (int i = 0; i < files.Count; i++)
                    {
                        string filePath = files[i];
                        try
                        {
                            string relPath = Path.GetRelativePath(dir, filePath)
                                .Replace("\\", "/").TrimEnd('/');

                            BundleWorkspaceItem? existing = _workspace!.Files
                                .FirstOrDefault(f => f.Name == relPath);
                            bool isSerialized = existing != null
                                ? existing.IsSerialized
                                : FileTypeDetector.DetectFileType(filePath) == DetectedFileType.AssetsFile;

                            _workspace.AddOrReplaceFile(File.OpenRead(filePath), relPath, isSerialized);
                            success++;
                        }
                        catch (Exception ex)
                        {
                            fail++;
                            Dispatcher.UIThread.Post(() => Log($"跳过 {Path.GetFileName(filePath)}: {ex.Message}"));
                        }

                        int done = i + 1;
                        Dispatcher.UIThread.Post(() =>
                        {
                            progressBar.Value = done;
                            progressText.Text = $"导入中 {done}/{files.Count}";
                        });
                    }
                });

                progressOverlay.IsVisible = false;
                SetChanges(true);
                RefreshAssetListFromBundle();
                statusText.Text = $"已批量导入 {success}/{files.Count} 项" + (fail > 0 ? $"(失败 {fail})" : "");
                Log($"批量导入完成:成功 {success},失败 {fail},目录 {dir}");
            }
            catch (Exception ex)
            {
                progressOverlay.IsVisible = false;
                Log("批量导入异常: " + ex);
                statusText.Text = "批量导入失败: " + ex.Message;
            }
        }

        // ===== 批量导入(资产级 raw)=====
        private void BtnBatchImportRaw_Click(object? sender, RoutedEventArgs e)
        {
            if (_standaloneAssetsInst == null || _assetWorkspace == null) return;

            var selection = assetList.SelectedItems.OfType<AssetListItem>()
                .Where(i => i.Container != null)
                .Select(i => i.Container!)
                .ToList();
            if (selection.Count == 0)
            {
                statusText.Text = "请先选中至少一个资产";
                return;
            }

            _pendingFileAction = PendingFileAction.BatchImportRaw;
            ShowFileBrowser(mode: FileBrowserView.BrowserMode.Directory);
        }

        private void ShowImportBatchView(string dir)
        {
            if (_assetWorkspace == null) return;

            var selection = assetList.SelectedItems.OfType<AssetListItem>()
                .Where(i => i.Container != null)
                .Select(i => i.Container!)
                .ToList();

            if (selection.Count == 0)
            {
                statusText.Text = "无有效资产";
                return;
            }

            importBatchView.Initialize(_assetWorkspace, selection, dir);
            importBatchView.Confirmed -= ImportBatchView_Confirmed;
            importBatchView.Confirmed += ImportBatchView_Confirmed;
            importBatchOverlay.IsVisible = true;
        }

        private async void ImportBatchView_Confirmed(object? sender, List<ImportBatchInfo>? batchInfos)
        {
            importBatchOverlay.IsVisible = false;
            if (batchInfos == null || batchInfos.Count == 0)
            {
                statusText.Text = batchInfos == null ? "已取消" : "无导入项";
                return;
            }
            await DoBatchImportRaw(batchInfos);
        }

        private async Task DoBatchImportRaw(List<ImportBatchInfo> batchInfos)
        {
            if (_assetWorkspace == null) return;
            try
            {
                progressBar.IsIndeterminate = false;
                progressBar.Maximum = batchInfos.Count;
                progressBar.Value = 0;
                progressText.Text = $"导入中 0/{batchInfos.Count}";
                progressOverlay.IsVisible = true;

                int success = 0, fail = 0;
                await Task.Run(() =>
                {
                    for (int i = 0; i < batchInfos.Count; i++)
                    {
                        var bi = batchInfos[i];
                        try
                        {
                            using var fs = File.OpenRead(bi.importFile);
                            var importer = new AssetImportExport();
                            byte[] bytes = importer.ImportRawAsset(fs);

                            AssetsReplacer replacer = AssetImportExport.CreateAssetReplacer(bi.cont, bytes);
                            _assetWorkspace!.AddReplacer(bi.cont.FileInstance, replacer, new MemoryStream(bytes));
                            success++;
                        }
                        catch (Exception ex)
                        {
                            fail++;
                            Dispatcher.UIThread.Post(() => Log($"资产 {bi.assetName} 导入失败: {ex.Message}"));
                        }

                        int done = i + 1;
                        Dispatcher.UIThread.Post(() =>
                        {
                            progressBar.Value = done;
                            progressText.Text = $"导入中 {done}/{batchInfos.Count}";
                        });
                    }
                });

                progressOverlay.IsVisible = false;
                SetChanges(true);
                if (_standaloneAssetsInst != null) RefreshAssetListFromAssetsFile(_standaloneAssetsInst);
                statusText.Text = $"已批量导入 {success}/{batchInfos.Count} 个资产" + (fail > 0 ? $"(失败 {fail})" : "");
                Log($"资产级批量导入完成:成功 {success},失败 {fail}");
            }
            catch (Exception ex)
            {
                progressOverlay.IsVisible = false;
                Log("资产级批量导入异常: " + ex);
                statusText.Text = "资产级批量导入失败: " + ex.Message;
            }
        }

        // ===== 导航与搜索 =====
        private void NextNameSearch()
        {
            if (!_navSearching)
            {
                statusText.Text = "请先执行搜索";
                return;
            }

            bool found = false;
            int count = _items.Count;

            if (_navSearchDown)
            {
                for (int i = _navSearchStart; i < count; i++)
                {
                    if (SearchUtils.WildcardMatches(_items[i].Name, _navSearchText, _navSearchCaseSensitive))
                    {
                        assetList.SelectedIndex = i;
                        assetList.ScrollIntoView(_items[i]);
                        _navSearchStart = i + 1;
                        found = true;
                        break;
                    }
                }
            }
            else
            {
                for (int i = _navSearchStart; i >= 0; i--)
                {
                    if (SearchUtils.WildcardMatches(_items[i].Name, _navSearchText, _navSearchCaseSensitive))
                    {
                        assetList.SelectedIndex = i;
                        assetList.ScrollIntoView(_items[i]);
                        _navSearchStart = i - 1;
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                statusText.Text = "未找到匹配资产";
                _navSearchText = "";
                _navSearchStart = 0;
                _navSearchDown = true;
                _navSearching = false;
                AssetList_SelectionChanged(null, null!);
            }
            else
            {
                statusText.Text = $"匹配: {_items[assetList.SelectedIndex].Name}";
            }
        }

        private void SelectAsset(long targetPathId)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                if (item.Container != null && item.Container.PathId == targetPathId)
                {
                    assetList.SelectedIndex = i;
                    assetList.ScrollIntoView(item);
                    statusText.Text = $"已跳转: {item.Name}";
                    return;
                }
            }
            statusText.Text = $"未找到 PathID 为 {targetPathId} 的资产";
        }

        private void BtnSearch_Click(object? sender, RoutedEventArgs e)
        {
            searchView.Confirmed -= SearchView_Confirmed;
            searchView.Confirmed += SearchView_Confirmed;
            searchView.FocusKeyword();
            searchOverlay.IsVisible = true;
        }

        private void BtnContinueSearch_Click(object? sender, RoutedEventArgs e)
        {
            if (!_navSearching)
            {
                statusText.Text = "无上次搜索,请先搜索";
                return;
            }
            NextNameSearch();
        }

        private void SearchView_Confirmed(object? sender, SearchDialogResult? result)
        {
            searchOverlay.IsVisible = false;
            if (result == null || !result.ok) return;

            _navSearchText = result.text;
            _navSearchDown = result.isDown;
            _navSearchCaseSensitive = result.caseSensitive;
            _navSearching = true;

            int sel = assetList.SelectedIndex;
            _navSearchStart = sel >= 0 ? sel : 0;
            if (!_navSearchDown && _navSearchStart == 0 && _items.Count > 0)
                _navSearchStart = _items.Count - 1;

            AssetList_SelectionChanged(null, null!);
            NextNameSearch();
        }

        private void BtnGoTo_Click(object? sender, RoutedEventArgs e)
        {
            goToAssetView.Confirmed -= GoToAssetView_Confirmed;
            goToAssetView.Confirmed += GoToAssetView_Confirmed;
            goToAssetView.Reset();
            goToOverlay.IsVisible = true;
        }

        private void GoToAssetView_Confirmed(object? sender, long? pathId)
        {
            goToOverlay.IsVisible = false;
            if (pathId == null) return;
            SelectAsset(pathId.Value);
        }

        private void BtnFilterType_Click(object? sender, RoutedEventArgs e)
        {
            if (_assetWorkspace == null) return;
            var checkedClassIds = new HashSet<int>();
            foreach (var kv in _assetWorkspace.LoadedAssets)
            {
                int classId = kv.Value.ClassId;
                if (!_filteredOutClassIds.Contains(classId))
                    checkedClassIds.Add(classId);
            }
            filterTypeView.Initialize(_assetWorkspace, checkedClassIds, _am);
            filterTypeView.Confirmed -= FilterTypeView_Confirmed;
            filterTypeView.Confirmed += FilterTypeView_Confirmed;
            filterTypeOverlay.IsVisible = true;
        }

        private void FilterTypeView_Confirmed(object? sender, HashSet<int>? filteredOut)
        {
            filterTypeOverlay.IsVisible = false;
            if (filteredOut == null) return;
            _filteredOutClassIds = filteredOut;
            _navSearchStart = 0;
            ApplySearchFilter();
            statusText.Text = $"已应用类型过滤(隐藏 {filteredOut.Count} 个类型)";
        }

        // ==================== 导入（插件替换） ====================
        // 使用内置文件选择器（Open 模式）选择导入源文件，避免系统选择器导致掉后台
        private void BtnImport_Click(object? sender, RoutedEventArgs e)
        {
            var item = assetList.SelectedItems.OfType<AssetListItem>().FirstOrDefault();
            if (item == null || _workspace == null) return;

            _pendingFileAction = PendingFileAction.Import;
            _importOrigName = item.Name; // 记录要替换的目标项
            ShowFileBrowser(mode: FileBrowserView.BrowserMode.Open);
        }

        /// <summary>导入文件选择后的回调：显示导入类型确认 overlay</summary>
        private Task OnImportFileSelected(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) { Log("未选择文件"); return Task.CompletedTask; }
                _importTempPath = path;
                importFileName.Text = $"替换: {_importOrigName}\n导入: {Path.GetFileName(path)}";
                importOverlay.IsVisible = true;
            }
            catch (Exception ex)
            {
                Log("导入选择异常: " + ex);
                statusText.Text = "导入失败: " + ex.Message;
                CleanupImport();
            }
            return Task.CompletedTask;
        }

        /// <summary>执行导入：用新文件替换 bundle 内的文件</summary>
        private void DoImport(bool isSerialized)
        {
            try
            {
                importOverlay.IsVisible = false;
                if (string.IsNullOrEmpty(_importTempPath) || string.IsNullOrEmpty(_importOrigName) || _workspace == null)
                {
                    Log("导入参数缺失");
                    CleanupImport();
                    return;
                }

                Log($"导入: {_importTempPath} -> {_importOrigName} (serialized={isSerialized})");

                // 读取导入文件到内存流（避免文件锁）
                byte[] fileBytes = File.ReadAllBytes(_importTempPath);
                MemoryStream stream = new MemoryStream(fileBytes);

                // 检查是否为已存在的文件（替换）还是新文件
                bool exists = _workspace.FileLookup.ContainsKey(_importOrigName);
                if (exists)
                {
                    _workspace.AddOrReplaceFile(stream, _importOrigName, isSerialized);
                    Log($"已替换文件: {_importOrigName}");
                }
                else
                {
                    _workspace.AddOrReplaceFile(stream, _importOrigName, isSerialized);
                    Log($"已新增文件: {_importOrigName}");
                }

                SetChanges(true);
                statusText.Text = $"导入成功: {_importOrigName}";
            }
            catch (Exception ex)
            {
                Log("DoImport 异常: " + ex);
                statusText.Text = "导入失败: " + ex.Message;
            }
            finally
            {
                CleanupImport();
            }
        }

        private void CleanupImport()
        {
            _importTempPath = null;
            _importOrigName = null;
        }

        // ==================== 删除 ====================
        private void BtnRemove_Click(object? sender, RoutedEventArgs e)
        {
            var item = assetList.SelectedItems.OfType<AssetListItem>().FirstOrDefault();
            if (item == null || _workspace == null || _bundleInst == null) return;

            try
            {
                string name = item.Name;
                Log($"删除: {name}");

                // 从工作区移除
                if (_workspace.FileLookup.ContainsKey(name))
                {
                    var wsItem = _workspace.FileLookup[name];
                    wsItem.IsRemoved = true;
                    _workspace.RemovedFiles.Add(name);
                    _workspace.Files.Remove(wsItem);
                    _workspace.FileLookup.Remove(name);
                }

                // 从 UI 列表移除
                _items.Remove(item);

                SetChanges(true);
                statusText.Text = $"已删除: {name}";
                Log($"删除成功: {name}, 剩余 {_items.Count} 项");
            }
            catch (Exception ex)
            {
                Log("删除异常: " + ex);
                statusText.Text = "删除失败: " + ex.Message;
            }
        }

        // ==================== Info ====================
        // 打开 AssetInfoView 显示完整文件信息（替代原日志输出）
        private void BtnInfo_Click(object? sender, RoutedEventArgs e)
        {
            AssetsFileInstance? fileInst = _standaloneAssetsInst;
            if (fileInst == null)
            {
                // 优先用选中资产的 FileInstance（独立 .assets 模式或 bundle 内子 .assets）
                var item = assetList.SelectedItems.OfType<AssetListItem>().FirstOrDefault();
                if (item?.Container != null)
                    fileInst = item.Container.FileInstance;
                else if (item?.FileInstance != null)
                    fileInst = item.FileInstance;
            }
            if (fileInst == null) { Log("无文件信息可显示（请打开 .assets 文件或选中资产）"); return; }

            assetInfoView.Confirmed -= AssetInfoView_Confirmed;
            assetInfoView.Confirmed += AssetInfoView_Confirmed;
            assetInfoView.Initialize(fileInst, _assetWorkspace);
            infoOverlay.IsVisible = true;
        }

        private void AssetInfoView_Confirmed(object? sender, bool e)
        {
            infoOverlay.IsVisible = false;
        }

        // ==================== 新视图：查看数据 / 编辑数据 / Raw 导入 / Dump 导入 / 新增 / 层级 ====================
        // 4.1 查看数据：类型树浏览
        private void BtnViewData_Click(object? sender, RoutedEventArgs e)
        {
            var item = assetList.SelectedItems.OfType<AssetListItem>().FirstOrDefault();
            if (item?.Container == null) { Log("请先选中资产"); return; }
            dataView.Confirmed -= DataView_Confirmed;
            dataView.Confirmed += DataView_Confirmed;
            dataView.Initialize(_assetWorkspace, item.Container, _bundleInst, _workspace);
            dataOverlay.IsVisible = true;
        }

        private void DataView_Confirmed(object? sender, bool e)
        {
            dataOverlay.IsVisible = false;
        }

        // 4.2 编辑数据：dump 文本 -> 编辑 -> 导入回资产
        private void BtnEditData_Click(object? sender, RoutedEventArgs e)
        {
            var item = assetList.SelectedItems.OfType<AssetListItem>().FirstOrDefault();
            if (item?.Container == null || _assetWorkspace == null) { Log("请先选中资产"); return; }
            try
            {
                var cont = item.Container;
                // 取得反序列化后的 baseField（必要时从 file 重新读取）
                AssetContainer? resolved = cont.HasValueField
                    ? cont
                    : _assetWorkspace.GetAssetContainer(cont.FileInstance, 0, cont.PathId, false);
                AssetTypeValueField? baseField = resolved?.BaseValueField ?? _assetWorkspace.GetBaseField(cont);
                if (baseField == null)
                {
                    Log("无法读取资产 base field（可能类型树缺失）");
                    return;
                }

                // dump 为文本
                using var ms = new MemoryStream();
                using (var sw = new StreamWriter(ms, leaveOpen: true))
                {
                    var impexp = new AssetImportExport();
                    impexp.DumpTextAsset(sw, baseField);
                    sw.Flush();
                }
                ms.Position = 0;
                string dumpText = System.Text.Encoding.UTF8.GetString(ms.ToArray());

                _editDataTarget = cont;
                editDataView.Confirmed -= EditDataView_Confirmed;
                editDataView.Confirmed += EditDataView_Confirmed;
                editDataView.Initialize(dumpText);
                editDataOverlay.IsVisible = true;
            }
            catch (Exception ex) { Log("Dump 失败: " + ex.Message); }
        }

        private void EditDataView_Confirmed(object? sender, string? result)
        {
            editDataOverlay.IsVisible = false;
            if (result == null || _editDataTarget == null || _assetWorkspace == null)
            {
                _editDataTarget = null;
                return;
            }
            try
            {
                // 把编辑后的文本导入回资产
                var impexp = new AssetImportExport();
                byte[]? bytes;
                using (var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(result)))
                using (var sr = new StreamReader(ms))
                {
                    bytes = impexp.ImportTextAsset(sr, out string? exceptionMessage);
                    if (bytes == null)
                    {
                        Log("应用编辑失败: " + (exceptionMessage ?? "未知错误"));
                        return;
                    }
                }
                AssetsReplacer replacer = AssetImportExport.CreateAssetReplacer(_editDataTarget, bytes);
                _assetWorkspace.AddReplacer(_editDataTarget.FileInstance, replacer, new MemoryStream(bytes));
                SetChanges(true);
                Log("编辑已应用 (PathId=" + _editDataTarget.PathId + ")");
            }
            catch (Exception ex) { Log("应用编辑失败: " + ex.Message); }
            finally { _editDataTarget = null; }
        }

        // 4.3 单项 Raw 导入：选择文件后写回
        private void BtnImportRaw_Click(object? sender, RoutedEventArgs e)
        {
            var item = assetList.SelectedItems.OfType<AssetListItem>().FirstOrDefault();
            if (item?.Container == null) { Log("请先选中资产"); return; }
            _pendingImportRawItem = item;
            _pendingFileAction = PendingFileAction.ImportRawAsset;
            ShowFileBrowser(FileBrowserView.BrowserMode.Open, new HashSet<string> { "dat", "" });
        }

        /// <summary>单项 Raw 导入：将选中的字节文件作为新内容写回资产（参考 DoBatchImportRaw）</summary>
        private async Task DoImportRawAsset(AssetListItem? item, string path)
        {
            if (item?.Container == null || _assetWorkspace == null) { Log("Raw 导入：未选中资产"); return; }
            try
            {
                var cont = item.Container;
                byte[] bytes;
                using (var fs = File.OpenRead(path))
                {
                    var importer = new AssetImportExport();
                    bytes = importer.ImportRawAsset(fs);
                }
                AssetsReplacer replacer = AssetImportExport.CreateAssetReplacer(cont, bytes);
                _assetWorkspace.AddReplacer(cont.FileInstance, replacer, new MemoryStream(bytes));
                SetChanges(true);
                Log($"Raw 导入成功: {Path.GetFileName(path)} (PathId={cont.PathId})");
                statusText.Text = "Raw 导入成功: " + Path.GetFileName(path);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Log("Raw 导入失败: " + ex.Message);
                statusText.Text = "Raw 导入失败: " + ex.Message;
            }
        }

        // 4.4 Dump 批量导入：选目录后用 ImportDumpView 让用户确认匹配
        private void BtnImportDump_Click(object? sender, RoutedEventArgs e)
        {
            var selection = assetList.SelectedItems.OfType<AssetListItem>()
                .Where(i => i.Container != null)
                .Select(i => i.Container!)
                .ToList();
            if (selection.Count == 0) { Log("请先选中资产"); return; }
            _pendingDumpSelection = selection;
            _pendingDumpFormat = ImportDumpView.DumpFormat.Text;
            _pendingFileAction = PendingFileAction.ImportDumpDir;
            ShowFileBrowser(FileBrowserView.BrowserMode.Directory);
        }

        private void ShowImportDumpView(string dir, List<AssetContainer> selection, ImportDumpView.DumpFormat format)
        {
            if (_assetWorkspace == null) { Log("未加载资产文件"); return; }
            if (selection.Count == 0) { Log("无选中资产"); return; }
            importDumpView.Confirmed -= ImportDumpView_Confirmed;
            importDumpView.Confirmed += ImportDumpView_Confirmed;
            importDumpView.Initialize(_assetWorkspace, selection, dir, format);
            importDumpOverlay.IsVisible = true;
        }

        private async void ImportDumpView_Confirmed(object? sender, List<ImportDumpInfo>? result)
        {
            importDumpOverlay.IsVisible = false;
            if (result == null || _assetWorkspace == null) { Log(result == null ? "已取消 Dump 导入" : "无导入项"); return; }
            int ok = 0, fail = 0;
            await Task.Run(() =>
            {
                foreach (var info in result)
                {
                    try
                    {
                        using var fs = File.OpenRead(info.importFile);
                        using var sr = new StreamReader(fs);
                        var importer = new AssetImportExport();
                        byte[]? bytes;
                        if (info.importFile.EndsWith(".json"))
                        {
                            AssetTypeTemplateField tempField = _assetWorkspace!.GetTemplateField(info.cont);
                            bytes = importer.ImportJsonAsset(tempField, sr, out string? exceptionMessage);
                            if (bytes == null)
                                throw new Exception(exceptionMessage ?? "Json 解析失败");
                        }
                        else
                        {
                            bytes = importer.ImportTextAsset(sr, out string? exceptionMessage);
                            if (bytes == null)
                                throw new Exception(exceptionMessage ?? "Text 解析失败");
                        }
                        AssetsReplacer replacer = AssetImportExport.CreateAssetReplacer(info.cont, bytes);
                        _assetWorkspace!.AddReplacer(info.cont.FileInstance, replacer, new MemoryStream(bytes));
                        ok++;
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.UIThread.Post(() => Log($"导入 {info.assetName} 失败: {ex.Message}"));
                        fail++;
                    }
                }
            });
            SetChanges(true);
            Log($"Dump 导入完成: {ok} 成功, {fail} 失败");
            statusText.Text = $"Dump 导入完成: 成功 {ok}, 失败 {fail}";
            if (_standaloneAssetsInst != null) RefreshAssetListFromAssetsFile(_standaloneAssetsInst);
        }

        // 4.5 新增资产：用 AssetsReplacerFromMemory 创建空资产
        private void BtnAddAsset_Click(object? sender, RoutedEventArgs e)
        {
            if (_assetWorkspace == null) { Log("未加载资产文件"); return; }
            addAssetView.Reset();
            addAssetView.Initialize(_assetWorkspace);
            addAssetView.Confirmed -= AddAssetView_Confirmed;
            addAssetView.Confirmed += AddAssetView_Confirmed;
            addAssetOverlay.IsVisible = true;
        }

        private void AddAssetView_Confirmed(object? sender, AddAssetView.AddAssetInfo? info)
        {
            addAssetOverlay.IsVisible = false;
            if (info == null) return; // 用户取消
            if (_assetWorkspace == null || _standaloneAssetsInst == null)
            {
                Log("新增失败：未加载独立 .assets 文件");
                return;
            }
            try
            {
                long pathId = info.PathId;
                if (pathId == -1)
                    pathId = GetNextPathId(_standaloneAssetsInst);

                int typeId = info.ClassId;
                ushort monoId = info.MonoScriptId == 0 ? (ushort)0xffff : (ushort)info.MonoScriptId;

                // 简化实现：创建空字节数组资产（参考 AddAssetWindow 未勾选"创建空资产"时的行为）
                // 不读 typetree/cldb 构造默认值，避免依赖复杂的模板字段构造流程。
                byte[] assetBytes = new byte[0];
                var replacer = new AssetsReplacerFromMemory(pathId, typeId, monoId, assetBytes);
                _assetWorkspace.AddReplacer(_standaloneAssetsInst, replacer, new MemoryStream(assetBytes));

                SetChanges(true);
                RefreshAssetListFromAssetsFile(_standaloneAssetsInst);
                Log($"新增资产成功: ClassId={typeId}, PathId={pathId}, MonoId={monoId}");
                statusText.Text = $"已新增资产 PathId={pathId}";
            }
            catch (Exception ex)
            {
                Log("新增失败: " + ex.Message);
                statusText.Text = "新增失败: " + ex.Message;
            }
        }

        /// <summary>获取当前文件中可用的下一个 PathId（max + 1，避开新增资产占用的 id）</summary>
        private long GetNextPathId(AssetsFileInstance fileInst)
        {
            long max = 0;
            foreach (var inf in fileInst.file.AssetInfos)
                if (inf.PathId > max) max = inf.PathId;
            if (_assetWorkspace != null)
            {
                foreach (var kv in _assetWorkspace.NewAssets)
                    if (kv.Key.pathID > max) max = kv.Key.pathID;
                foreach (var kv in _assetWorkspace.LoadedAssets)
                    if (kv.Value != null && kv.Value.PathId > max) max = kv.Value.PathId;
            }
            return max + 1;
        }

        // 4.6 场景层级
        private void BtnHierarchy_Click(object? sender, RoutedEventArgs e)
        {
            AssetsFileInstance? fileInst = _standaloneAssetsInst;
            if (fileInst == null)
            {
                // bundle 或独立模式：若有选中项取其 FileInstance
                var item = assetList.SelectedItems.OfType<AssetListItem>().FirstOrDefault();
                if (item?.Container != null)
                    fileInst = item.Container.FileInstance;
                else if (item?.FileInstance != null)
                    fileInst = item.FileInstance;
            }
            if (fileInst == null) { Log("请先打开 .assets 文件"); return; }
            hierarchyView.Confirmed -= HierarchyView_Confirmed;
            hierarchyView.Confirmed += HierarchyView_Confirmed;
            hierarchyView.Initialize(fileInst, _assetWorkspace);
            hierarchyOverlay.IsVisible = true;
        }

        private void HierarchyView_Confirmed(object? sender, bool e)
        {
            hierarchyOverlay.IsVisible = false;
        }

        // 4.7 3D Mesh 预览(软件渲染,横屏宽幅 overlay)
        private void BtnPreview3D_Click(object? sender, RoutedEventArgs e)
        {
            var item = assetList.SelectedItems.OfType<AssetListItem>().FirstOrDefault();
            if (item?.Container == null) { Log("请先选中 Mesh 资产"); return; }
            meshPreviewView.Confirmed -= MeshPreviewView_Confirmed;
            meshPreviewView.Confirmed += MeshPreviewView_Confirmed;
            meshPreviewView.Initialize(_assetWorkspace, item.Container, _bundleInst, _workspace);
            meshPreviewOverlay.IsVisible = true;
        }

        private void MeshPreviewView_Confirmed(object? sender, bool e)
        {
            meshPreviewOverlay.IsVisible = false;
        }

        // ==================== Dump ====================
        // 使用内置文件选择器（Save 模式）选择 Dump 输出路径
        private void BtnDump_Click(object? sender, RoutedEventArgs e)
        {
            var item = assetList.SelectedItems.OfType<AssetListItem>().FirstOrDefault();
            if (item == null) return;

            _pendingFileAction = PendingFileAction.DumpSave;
            _pendingDumpItem = item;
            ShowFileBrowser(
                mode: FileBrowserView.BrowserMode.Save,
                suggestedName: Path.GetFileNameWithoutExtension(item.Name) + "_dump.txt");
        }

        /// <summary>实际执行 Dump 到指定路径</summary>
        private async Task DoDumpToPath(AssetListItem? item, string outPath)
        {
            if (item == null) { Log("Dump 项为空"); return; }
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("# UABEA Dump");
                sb.AppendLine($"# File: {item.Name}");
                sb.AppendLine($"# Time: {DateTime.Now}");
                sb.AppendLine();

                AssetsFileInstance? dumpFileInst = null;
                AssetFileInfo? dumpInf = null;

                if (item.IsAsset && item.FileInstance != null && item.AssetInfo != null)
                {
                    dumpFileInst = item.FileInstance;
                    dumpInf = item.AssetInfo;
                }
                else if (item.DirInfo != null && _bundleInst != null)
                {
                    using var ms = GetItemStream(item);
                    if (ms != null)
                    {
                        ms.Position = 0;
                        var fileType = FileTypeDetector.DetectFileType(new AssetsFileReader(ms), 0);
                        sb.AppendLine($"# FileType: {fileType}");
                        sb.AppendLine();

                        if (fileType == DetectedFileType.AssetsFile)
                        {
                            ms.Position = 0;
                            string assetMemPath = Path.Combine(_bundleInst.path, item.Name);
                            dumpFileInst = _am.LoadAssetsFile(ms, assetMemPath, true);
                            string uVer = dumpFileInst.file.Metadata.UnityVersion;
                            if (uVer == "0.0.0") uVer = _bundleInst.file.Header.EngineVersion;
                            if (uVer == "0.0.0") uVer = "2019.4";
                            _am.LoadClassDatabaseFromPackage(uVer);
                        }
                    }
                }

                if (dumpFileInst != null && dumpInf != null)
                {
                    var baseField = _am.GetBaseField(dumpFileInst, dumpInf);
                    if (baseField != null)
                    {
                        sb.AppendLine($"=== PathId: {dumpInf.PathId} TypeId: {dumpInf.TypeId} ===");
                        sb.AppendLine(baseField.ToString());
                    }
                    else
                    {
                        sb.AppendLine("(无法读取类型树)");
                    }
                }
                else if (dumpFileInst != null)
                {
                    foreach (var inf in dumpFileInst.file.AssetInfos)
                    {
                        var baseField = _am.GetBaseField(dumpFileInst, inf);
                        if (baseField != null)
                        {
                            sb.AppendLine($"=== PathId: {inf.PathId} TypeId: {inf.TypeId} ===");
                            sb.AppendLine(baseField.ToString());
                            sb.AppendLine();
                        }
                    }
                }
                else
                {
                    sb.AppendLine("(非 assets 文件，无法 Dump 类型树)");
                    if (item.DirInfo != null)
                        sb.AppendLine($"文件大小: {item.DirInfo.DecompressedSize} bytes");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                await File.WriteAllTextAsync(outPath, sb.ToString());
                Log($"Dump 完成: {outPath}");
                statusText.Text = "Dump 成功: " + Path.GetFileName(outPath);
            }
            catch (Exception ex)
            {
                Log("Dump 异常: " + ex);
                statusText.Text = "Dump 失败: " + ex.Message;
            }
        }

        // ==================== 重命名 ====================
        private void BtnRename_Click(object? sender, RoutedEventArgs e)
        {
            var item = assetList.SelectedItems.OfType<AssetListItem>().FirstOrDefault();
            if (item == null || _workspace == null) return;

            _renameTarget = item;
            renameBox.Text = item.Name;
            renameOverlay.IsVisible = true;
            renameBox.Focus();
        }

        private void RenameOk_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (_renameTarget != null && _workspace != null)
                {
                    string newName = renameBox.Text?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(newName) && newName != _renameTarget.Name)
                    {
                        _workspace.RenameFile(_renameTarget.Name, newName);
                        Log($"重命名: {_renameTarget.Name} -> {newName}");
                        _renameTarget.Name = newName;
                        RefreshAssetListFromBundle();
                        SetChanges(true);
                        statusText.Text = "已重命名为: " + newName;
                    }
                }
            }
            catch (Exception ex)
            {
                Log("重命名异常: " + ex);
                statusText.Text = "重命名失败: " + ex.Message;
            }
            finally
            {
                _renameTarget = null;
                renameOverlay.IsVisible = false;
            }
        }

        private void RenameCancel_Click(object? sender, RoutedEventArgs e)
        {
            _renameTarget = null;
            renameOverlay.IsVisible = false;
        }

        // ==================== 保存 ====================
        // 使用内置文件选择器（Save 模式）选择保存目标，避免系统选择器导致掉后台
        private void BtnSave_Click(object? sender, RoutedEventArgs e)
        {
            if (_bundleInst == null || _workspace == null || !_changesUnsaved) return;

            _pendingFileAction = PendingFileAction.SaveBundle;
            ShowFileBrowser(
                mode: FileBrowserView.BrowserMode.Save,
                suggestedName: Path.GetFileName(_bundleInst.path));
        }

        /// <summary>实际执行保存 Bundle 到指定路径</summary>
        private async Task DoSaveBundleToPath(string path)
        {
            if (_bundleInst == null || _workspace == null) return;
            try
            {
                ShowProgress("保存中...");
                await Task.Run(() => SaveBundleToPath(path));

                _changesUnsaved = false;
                UpdateStatusText();
                btnSave.IsEnabled = false;
                HideProgress();
                Log($"保存成功: {path}");
                statusText.Text = "保存成功: " + Path.GetFileName(path);
            }
            catch (Exception ex)
            {
                HideProgress();
                Log("保存异常: " + ex);
                statusText.Text = "保存失败: " + ex.Message;
            }
        }

        private void SaveBundleToPath(string path)
        {
            if (_bundleInst == null || _workspace == null) return;
            List<BundleReplacer> replacers = _workspace.GetReplacers();
            Log($"保存: {replacers.Count} 个 replacer -> {path}");
            using (var fs = File.Open(path, FileMode.Create))
            using (var w = new AssetsFileWriter(fs))
            {
                _bundleInst.file.Write(w, replacers);
            }
        }

        // ==================== 压缩 ====================
        private void BtnCompress_Click(object? sender, RoutedEventArgs e)
        {
            if (_bundleInst == null) return;
            compressOverlay.IsVisible = true;
        }

        // 用户选择压缩方式后，弹出内置文件选择器选择保存目标
        private void DoCompress(AssetBundleCompressionType compType)
        {
            compressOverlay.IsVisible = false;
            if (_bundleInst == null) return;

            _pendingFileAction = PendingFileAction.CompressSave;
            _pendingCompType = compType;
            ShowFileBrowser(
                mode: FileBrowserView.BrowserMode.Save,
                suggestedName: Path.GetFileNameWithoutExtension(_bundleInst.name) + "_compressed");
        }

        /// <summary>实际执行压缩保存到指定路径</summary>
        private async Task DoCompressSaveToPath(string path, AssetBundleCompressionType compType)
        {
            if (_bundleInst == null) return;
            try
            {
                ShowProgress($"压缩中 ({compType})...");

                string finalPath = path;
                string bundlePath = _bundleInst.path;
                AssetBundleFile bundleFile = _bundleInst.file;
                AssetsFileReader bundleReader = bundleFile.Reader;

                await Task.Run(() =>
                {
                    using var fs = File.Open(finalPath, FileMode.Create);
                    using var w = new AssetsFileWriter(fs);
                    bundleFile.Pack(bundleReader, w, compType, true, null);
                });

                HideProgress();
                Log($"压缩完成 ({compType}): {path}");
                statusText.Text = $"压缩成功: {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                HideProgress();
                Log("压缩异常: " + ex);
                statusText.Text = "压缩失败: " + ex.Message;
            }
        }

        // ==================== 进度遮罩 ====================
        private void ShowProgress(string text)
        {
            progressText.Text = text;
            progressOverlay.IsVisible = true;
        }

        private void HideProgress()
        {
            progressOverlay.IsVisible = false;
        }

        // ==================== 工具方法 ====================
        private Stream? GetItemStream(AssetListItem item)
        {
            if (item.DirInfo != null && _bundleInst != null)
            {
                var reader = _bundleInst.file.DataReader;
                var ms = new MemoryStream();
                reader.Position = item.DirInfo.Offset;
                reader.BaseStream.CopyToCompat(ms, item.DirInfo.DecompressedSize);
                return ms;
            }
            else if (item.IsAsset && item.FileInstance != null && item.AssetInfo != null)
            {
                var fileInst = item.FileInstance;
                var inf = item.AssetInfo;
                var reader = fileInst.file.Reader;
                reader.Position = inf.AbsoluteByteStart;
                var ms = new MemoryStream();
                reader.BaseStream.CopyToCompat(ms, inf.ByteSize);
                return ms;
            }
            return null;
        }

        // ==================== 预览 ====================
        private async void BtnPreview_Click(object? sender, RoutedEventArgs e)
        {
            var item = assetList.SelectedItems.OfType<AssetListItem>().FirstOrDefault();
            if (item == null) return;

            try
            {
                if (item.IsAsset && item.FileInstance != null && item.AssetInfo != null)
                {
                    // 独立 .assets 文件：直接预览选中的资产
                    int classId = item.AssetInfo.TypeId;
                    _previewFileInst = item.FileInstance;
                    _previewAssetInf = item.AssetInfo;
                    _previewIsFromBundle = false;
                    _previewBundleEntryName = null;

                    if (classId == (int)AssetClassID.Texture2D)
                    {
                        await PreviewTextureAsync();
                    }
                    else if (classId == (int)AssetClassID.TextAsset)
                    {
                        PreviewText();
                    }
                    else
                    {
                        Log($"不支持预览类型: ClassID={classId}");
                        statusText.Text = "不支持预览此类型";
                    }
                }
                else if (item.DirInfo != null && _bundleInst != null)
                {
                    // bundle 条目：加载子 .assets，查找贴图/文字
                    await PreviewFromBundleEntryAsync(item);
                }
            }
            catch (Exception ex)
            {
                Log("预览异常: " + ex);
                statusText.Text = "预览失败: " + ex.Message;
            }
        }

        private async Task PreviewFromBundleEntryAsync(AssetListItem item)
        {
            ShowProgress("加载中...");

            AssetsFileInstance? subInst = null;
            AssetFileInfo? texInf = null;
            AssetFileInfo? textInf = null;

            await Task.Run(() =>
            {
                using var ms = GetItemStream(item);
                if (ms == null) return;
                ms.Position = 0;
                var fileType = FileTypeDetector.DetectFileType(new AssetsFileReader(ms), 0);
                if (fileType != DetectedFileType.AssetsFile) return;

                ms.Position = 0;
                string memPath = _bundleInst!.path + "#" + item.Name;
                subInst = _am.LoadAssetsFile(ms, memPath, true);
                string uVer = subInst.file.Metadata.UnityVersion;
                if (uVer == "0.0.0") uVer = _bundleInst.file.Header.EngineVersion;
                if (uVer == "0.0.0") uVer = "2019.4";
                _am.LoadClassDatabaseFromPackage(uVer);

                foreach (var inf in subInst.file.AssetInfos)
                {
                    if (inf.TypeId == (int)AssetClassID.Texture2D && texInf == null)
                        texInf = inf;
                    else if (inf.TypeId == (int)AssetClassID.TextAsset && textInf == null)
                        textInf = inf;
                }
            });

            HideProgress();

            if (subInst == null)
            {
                Log("无法加载子 .assets 文件");
                statusText.Text = "无法预览";
                return;
            }

            _previewFileInst = subInst;
            _previewIsFromBundle = true;
            _previewBundleEntryName = item.Name;

            if (texInf != null)
            {
                _previewAssetInf = texInf;
                await PreviewTextureAsync();
            }
            else if (textInf != null)
            {
                _previewAssetInf = textInf;
                PreviewText();
            }
            else
            {
                Log("未找到可预览的 Texture2D 或 TextAsset");
                statusText.Text = "无贴图/文字资产";
            }
        }

        private async Task PreviewTextureAsync()
        {
            if (_previewFileInst == null || _previewAssetInf == null) return;

            ShowProgress("解码贴图...");

            try
            {
                byte[]? bgraData = null;
                int width = 0, height = 0;
                TextureFormat format = 0;
                string texName = "";
                int mipCount = 0;

                await Task.Run(() =>
                {
                    AssetTypeValueField baseField = GetTextureBaseField(_previewFileInst!, _previewAssetInf!);
                    var texFile = TextureFile.ReadTextureFile(baseField);
                    texName = texFile.m_Name;
                    width = texFile.m_Width;
                    height = texFile.m_Height;
                    format = (TextureFormat)texFile.m_TextureFormat;
                    mipCount = texFile.m_MipCount;

                    byte[]? rawBytes = GetRawTextureBytes(texFile, _previewFileInst!, _bundleInst);
                    if (rawBytes == null || rawBytes.Length == 0)
                    {
                        bgraData = null;
                        return;
                    }

                    try
                    {
                        // TextureFile.DecodeManaged 返回 BGRA 字节（适用于 Avalonia Bgra8888）
                        bgraData = TextureFile.DecodeManaged(rawBytes, format, width, height);
                    }
                    catch (Exception ex)
                    {
                        Log("DecodeManaged 失败: " + ex.Message);
                        bgraData = null;
                    }
                });

                _previewTexFormat = format;
                _previewTexWidth = width;
                _previewTexHeight = height;

                // 设置预览 UI
                previewTitle.Text = "贴图预览";
                previewInfo.Text = $"名称: {texName}\n格式: {format}\n尺寸: {width}x{height}\nMipCount: {mipCount}\n数据大小: {bgraData?.Length ?? 0} bytes";
                previewTextBox.IsVisible = false;
                btnSaveText.IsVisible = false;

                if (bgraData != null && bgraData.Length >= width * height * 4)
                {
                    // 创建 Avalonia Bitmap
                    var bitmap = new WriteableBitmap(
                        new PixelSize(width, height),
                        new Vector(96, 96),
                        PixelFormat.Bgra8888,
                        AlphaFormat.Unpremul);

                    using (var lockData = bitmap.Lock())
                    {
                        Marshal.Copy(bgraData, 0, lockData.Address, Math.Min(bgraData.Length, width * height * 4));
                    }

                    previewImage.Source = bitmap;
                    previewImage.IsVisible = true;
                    btnReplaceTexture.IsVisible = true;
                    Log($"贴图预览成功: {texName} {width}x{height} {format}");
                    statusText.Text = $"预览贴图: {texName}";
                }
                else
                {
                    previewImage.Source = null;
                    previewImage.IsVisible = false;
                    btnReplaceTexture.IsVisible = false;
                    previewInfo.Text += "\n\n(无法解码此格式，但可尝试替换)";
                    Log($"贴图解码失败: {format}，可能不支持");
                }

                previewOverlay.IsVisible = true;
            }
            catch (Exception ex)
            {
                Log("贴图预览异常: " + ex);
                statusText.Text = "贴图预览失败: " + ex.Message;
            }
            finally
            {
                HideProgress();
            }
        }

        private void PreviewText()
        {
            if (_previewFileInst == null || _previewAssetInf == null) return;

            try
            {
                var baseField = _am.GetBaseField(_previewFileInst, _previewAssetInf);
                if (baseField == null)
                {
                    Log("无法读取 TextAsset base field");
                    return;
                }

                string name = baseField["m_Name"].AsString;
                byte[]? scriptBytes = baseField["m_Script"].AsByteArray;

                string text = "";
                if (scriptBytes != null && scriptBytes.Length > 0)
                {
                    // 尝试 UTF-8 解码
                    try
                    {
                        text = System.Text.Encoding.UTF8.GetString(scriptBytes);
                    }
                    catch
                    {
                        text = System.Text.Encoding.Latin1.GetString(scriptBytes);
                    }
                }

                // 限制显示大小
                if (text.Length > 50000)
                    text = text.Substring(0, 50000) + "\n\n... (已截断，共 " + scriptBytes?.Length + " bytes)";

                previewTitle.Text = "文本预览";
                previewInfo.Text = $"名称: {name}\n大小: {scriptBytes?.Length ?? 0} bytes";
                previewImage.IsVisible = false;
                btnReplaceTexture.IsVisible = false;

                previewTextBox.Text = text;
                previewTextBox.IsVisible = true;
                btnSaveText.IsVisible = true;

                previewOverlay.IsVisible = true;
                Log($"文本预览: {name} ({scriptBytes?.Length ?? 0} bytes)");
                statusText.Text = $"预览文本: {name}";
            }
            catch (Exception ex)
            {
                Log("文本预览异常: " + ex);
                statusText.Text = "文本预览失败: " + ex.Message;
            }
        }

        // ==================== 替换贴图 ====================
        // 使用内置文件选择器（Open 模式）选择替换图片，避免系统选择器导致掉后台
        private void BtnReplaceTexture_Click(object? sender, RoutedEventArgs e)
        {
            if (_previewFileInst == null || _previewAssetInf == null) return;

            _pendingFileAction = PendingFileAction.ReplaceTextureOpen;
            ShowFileBrowser(
                mode: FileBrowserView.BrowserMode.Open,
                extensions: new HashSet<string> { "png", "jpg", "jpeg", "bmp", "tga", "" });
        }

        /// <summary>选择替换图片后的处理：编码并替换</summary>
        private async Task DoReplaceTextureFromPath(string imagePath)
        {
            if (_previewFileInst == null || _previewAssetInf == null) return;
            if (string.IsNullOrEmpty(imagePath)) { Log("未选择图片"); return; }

            try
            {
                ShowProgress("编码贴图...");

                bool success = false;
                string errorMsg = "";
                byte[]? savedAssetsData = null;

                await Task.Run(() =>
                {
                    try
                    {
                        // 加载图片
                        using var image = Image.Load<Rgba32>(imagePath);
                        int imgWidth = image.Width;
                        int imgHeight = image.Height;

                        // 翻转 Y 轴（Unity 纹理原点在底部）
                        image.Mutate(i => i.Flip(FlipMode.Vertical));

                        // 提取 RGBA 字节
                        byte[] rgbaData = new byte[imgWidth * imgHeight * 4];
                        image.ProcessPixelRows(accessor =>
                        {
                            for (int y = 0; y < accessor.Height; y++)
                            {
                                Span<Rgba32> row = accessor.GetRowSpan(y);
                                MemoryMarshal.AsBytes(row).CopyTo(rgbaData.AsSpan(y * accessor.Width * 4));
                            }
                        });

                        // 编码到原始纹理格式
                        byte[]? encData = EncodeTextureFromRGBA(rgbaData, imgWidth, imgHeight, _previewTexFormat);
                        if (encData == null)
                        {
                            errorMsg = $"不支持编码格式: {_previewTexFormat}（仅支持 RGBA32/BGRA32/ARGB32/RGB24/Alpha8/R8）";
                            return;
                        }

                        // 获取 base field 并修改
                        AssetTypeValueField baseField = GetTextureBaseField(_previewFileInst!, _previewAssetInf!);

                        // 清除流数据（改为内联）
                        var streamData = baseField["m_StreamData"];
                        if (!streamData.IsDummy)
                        {
                            streamData["offset"].AsInt = 0;
                            streamData["size"].AsInt = 0;
                            streamData["path"].AsString = "";
                        }

                        // 更新尺寸和格式
                        if (!baseField["m_Width"].IsDummy)
                            baseField["m_Width"].AsInt = imgWidth;
                        if (!baseField["m_Height"].IsDummy)
                            baseField["m_Height"].AsInt = imgHeight;
                        if (!baseField["m_TextureFormat"].IsDummy)
                            baseField["m_TextureFormat"].AsInt = (int)_previewTexFormat;
                        if (!baseField["m_CompleteImageSize"].IsDummy)
                            baseField["m_CompleteImageSize"].AsInt = encData.Length;
                        if (!baseField["m_MipCount"].IsDummy)
                            baseField["m_MipCount"].AsInt = 1;

                        // 设置 image data
                        var imageData = baseField["image data"];
                        imageData.Value.ValueType = AssetValueType.ByteArray;
                        imageData.TemplateField.ValueType = AssetValueType.ByteArray;
                        imageData.AsByteArray = encData;

                        // 序列化
                        byte[] savedAsset = baseField.WriteToByteArray();
                        var replacer = new AssetsReplacerFromMemory(
                            _previewAssetInf!.PathId, _previewAssetInf.TypeId,
                            _previewAssetInf.ScriptTypeIndex, savedAsset);

                        var replacers = new List<AssetsReplacer> { replacer };

                        // 写入修改后的 .assets 到内存
                        using var ms = new MemoryStream();
                        using var w = new AssetsFileWriter(ms);
                        _previewFileInst!.file.Write(w, 0, replacers);
                        savedAssetsData = ms.ToArray();

                        success = true;
                    }
                    catch (Exception ex)
                    {
                        errorMsg = ex.ToString();
                    }
                });

                HideProgress();

                if (success && savedAssetsData != null)
                {
                    if (_previewIsFromBundle && _workspace != null && _previewBundleEntryName != null)
                    {
                        // 替换 bundle 中的条目
                        _workspace.AddOrReplaceFile(new MemoryStream(savedAssetsData), _previewBundleEntryName, true);
                        SetChanges(true);
                        Log($"贴图已替换: {_previewBundleEntryName} (PathId={_previewAssetInf.PathId})");
                        statusText.Text = "贴图替换成功（请保存 Bundle）";
                    }
                    else
                    {
                        // 独立 .assets：保存到原文件同目录（避免再次弹出选择器）
                        string dir = Path.GetDirectoryName(_previewFileInst!.path) ?? AppPaths.GetCacheDir();
                        string baseName = Path.GetFileNameWithoutExtension(_previewFileInst.path);
                        string ext = Path.GetExtension(_previewFileInst.path);
                        string savePath = Path.Combine(dir, $"{baseName}_modified{ext}");
                        File.WriteAllBytes(savePath, savedAssetsData);
                        Log($"贴图已保存: {savePath}");
                        statusText.Text = "贴图保存成功: " + Path.GetFileName(savePath);
                    }
                    previewOverlay.IsVisible = false;
                    Log($"贴图替换成功: {_previewTexFormat} {_previewTexWidth}x{_previewTexHeight}");
                }
                else
                {
                    Log("贴图替换失败: " + errorMsg);
                    statusText.Text = "贴图替换失败: " + errorMsg.Substring(0, Math.Min(100, errorMsg.Length));
                }
            }
            catch (Exception ex)
            {
                HideProgress();
                Log("替换贴图异常: " + ex);
                statusText.Text = "替换失败: " + ex.Message;
            }
        }

        // ==================== 保存文本 ====================
        // Bundle 模式直接替换工作区；独立 .assets 保存到原文件同目录（避免系统选择器掉后台）
        private async void BtnSaveText_Click(object? sender, RoutedEventArgs e)
        {
            if (_previewFileInst == null || _previewAssetInf == null) return;

            try
            {
                var baseField = _am.GetBaseField(_previewFileInst, _previewAssetInf);
                if (baseField == null) { Log("无法读取 base field"); return; }

                byte[] textBytes = System.Text.Encoding.UTF8.GetBytes(previewTextBox.Text ?? "");
                baseField["m_Script"].AsByteArray = textBytes;

                byte[] savedAsset = baseField.WriteToByteArray();
                var replacer = new AssetsReplacerFromMemory(
                    _previewAssetInf.PathId, _previewAssetInf.TypeId,
                    _previewAssetInf.ScriptTypeIndex, savedAsset);

                var replacers = new List<AssetsReplacer> { replacer };

                if (_previewIsFromBundle && _workspace != null && _previewBundleEntryName != null)
                {
                    // 写入修改后的 .assets 到内存，再替换 bundle 中的条目
                    using var ms = new MemoryStream();
                    using var w = new AssetsFileWriter(ms);
                    _previewFileInst.file.Write(w, 0, replacers);
                    _workspace.AddOrReplaceFile(new MemoryStream(ms.ToArray()), _previewBundleEntryName, true);
                    SetChanges(true);
                    Log($"文本已替换: {_previewBundleEntryName} (PathId={_previewAssetInf.PathId})");
                    statusText.Text = "文本替换成功（请保存 Bundle）";
                }
                else
                {
                    // 独立 .assets：保存到原文件同目录（避免系统选择器掉后台）
                    string dir = Path.GetDirectoryName(_previewFileInst.path) ?? AppPaths.GetCacheDir();
                    string baseName = Path.GetFileNameWithoutExtension(_previewFileInst.path);
                    string ext = Path.GetExtension(_previewFileInst.path);
                    string savePath = Path.Combine(dir, $"{baseName}_modified{ext}");

                    using (var fs = File.Open(savePath, FileMode.Create))
                    using (var w = new AssetsFileWriter(fs))
                    {
                        _previewFileInst.file.Write(w, 0, replacers);
                    }

                    Log($"文本已保存: {savePath}");
                    statusText.Text = "文本保存成功: " + Path.GetFileName(savePath);
                }

                previewOverlay.IsVisible = false;
            }
            catch (Exception ex)
            {
                Log("保存文本异常: " + ex);
                statusText.Text = "保存失败: " + ex.Message;
            }
        }

        /// <summary>保存文本到指定路径（占位实现，当前 SaveText 已内联保存）</summary>
        private Task DoSaveTextToPath(string path)
        {
            // 当前 BtnSaveText_Click 已直接内联保存逻辑，此方法作为 FileBrowser 调度占位
            return Task.CompletedTask;
        }

        // ==================== 贴图工具方法 ====================

        /// <summary>获取 Texture2D 的 base field，将 "image data" 修补为 ByteArray 类型</summary>
        private AssetTypeValueField GetTextureBaseField(AssetsFileInstance fileInst, AssetFileInfo assetInf)
        {
            AssetTypeTemplateField template = _am.GetTemplateBaseField(
                fileInst, fileInst.file.Reader, assetInf.AbsoluteByteStart,
                assetInf.TypeId, assetInf.ScriptTypeIndex, AssetReadFlags.None);

            // 修补 "image data" 为 ByteArray
            var imageData = template.Children.FirstOrDefault(f => f.Name == "image data");
            if (imageData != null)
                imageData.ValueType = AssetValueType.ByteArray;

            // 修补 m_PlatformBlob 为 ByteArray
            var platformBlob = template.Children.FirstOrDefault(f => f.Name == "m_PlatformBlob");
            if (platformBlob != null && platformBlob.Children.Count > 0)
                platformBlob.Children[0].ValueType = AssetValueType.ByteArray;

            return template.MakeValue(fileInst.file.Reader, assetInf.AbsoluteByteStart);
        }

        /// <summary>获取原始纹理字节（内联或从 resS 流数据）</summary>
        private byte[]? GetRawTextureBytes(TextureFile texFile, AssetsFileInstance inst, BundleFileInstance? bundleInst)
        {
            if (texFile.m_StreamData.size != 0 && texFile.m_StreamData.path != string.Empty)
            {
                string streamPath = texFile.m_StreamData.path;

                // 尝试在 bundle 中查找
                if (bundleInst != null)
                {
                    string searchPath = streamPath;
                    if (searchPath.StartsWith("archive:/"))
                        searchPath = searchPath.Substring(9);
                    searchPath = Path.GetFileName(searchPath);

                    var bundle = bundleInst.file;
                    var reader = bundle.DataReader;
                    foreach (var dirInf in bundle.BlockAndDirInfo.DirectoryInfos)
                    {
                        if (dirInf.Name == searchPath)
                        {
                            reader.Position = dirInf.Offset + (long)texFile.m_StreamData.offset;
                            texFile.pictureData = reader.ReadBytes((int)texFile.m_StreamData.size);
                            return texFile.pictureData;
                        }
                    }
                }

                // 尝试作为外部文件查找
                string rootPath = Path.GetDirectoryName(inst.path);
                string fixedPath = streamPath;
                if (fixedPath.StartsWith("archive:/"))
                    fixedPath = Path.GetFileName(fixedPath);
                if (!Path.IsPathRooted(fixedPath) && rootPath != null)
                    fixedPath = Path.Combine(rootPath, fixedPath);
                if (File.Exists(fixedPath))
                {
                    using var fs = File.OpenRead(fixedPath);
                    fs.Position = (long)texFile.m_StreamData.offset;
                    texFile.pictureData = new byte[texFile.m_StreamData.size];
                    fs.Read(texFile.pictureData, 0, (int)texFile.m_StreamData.size);
                    return texFile.pictureData;
                }

                Log($"纹理流数据未找到: {streamPath}");
                return null;
            }

            return texFile.pictureData;
        }

        /// <summary>将 RGBA 字节编码为指定纹理格式（托管，仅支持简单格式）</summary>
        private byte[]? EncodeTextureFromRGBA(byte[] rgbaData, int width, int height, TextureFormat format)
        {
            int pixelCount = width * height;
            switch (format)
            {
                case TextureFormat.RGBA32:
                    return rgbaData;

                case TextureFormat.BGRA32:
                    {
                        byte[] bgra = new byte[pixelCount * 4];
                        for (int i = 0; i < pixelCount; i++)
                        {
                            bgra[i * 4 + 0] = rgbaData[i * 4 + 2]; // B
                            bgra[i * 4 + 1] = rgbaData[i * 4 + 1]; // G
                            bgra[i * 4 + 2] = rgbaData[i * 4 + 0]; // R
                            bgra[i * 4 + 3] = rgbaData[i * 4 + 3]; // A
                        }
                        return bgra;
                    }

                case TextureFormat.ARGB32:
                    {
                        byte[] argb = new byte[pixelCount * 4];
                        for (int i = 0; i < pixelCount; i++)
                        {
                            argb[i * 4 + 0] = rgbaData[i * 4 + 3]; // A
                            argb[i * 4 + 1] = rgbaData[i * 4 + 0]; // R
                            argb[i * 4 + 2] = rgbaData[i * 4 + 1]; // G
                            argb[i * 4 + 3] = rgbaData[i * 4 + 2]; // B
                        }
                        return argb;
                    }

                case TextureFormat.RGB24:
                    {
                        byte[] rgb = new byte[pixelCount * 3];
                        for (int i = 0; i < pixelCount; i++)
                        {
                            rgb[i * 3 + 0] = rgbaData[i * 4 + 0]; // R
                            rgb[i * 3 + 1] = rgbaData[i * 4 + 1]; // G
                            rgb[i * 3 + 2] = rgbaData[i * 4 + 2]; // B
                        }
                        return rgb;
                    }

                case TextureFormat.Alpha8:
                    {
                        byte[] a8 = new byte[pixelCount];
                        for (int i = 0; i < pixelCount; i++)
                            a8[i] = rgbaData[i * 4 + 3];
                        return a8;
                    }

                case TextureFormat.R8:
                    {
                        byte[] r8 = new byte[pixelCount];
                        for (int i = 0; i < pixelCount; i++)
                            r8[i] = rgbaData[i * 4 + 0];
                        return r8;
                    }

                default:
                    return null; // 不支持的格式
            }
        }

        // ==================== 关闭 ====================
        private async void BtnClose_Click(object? sender, RoutedEventArgs e)
        {
            if (_changesUnsaved && _bundleInst != null)
            {
                Log("有未保存的修改，关闭将丢弃");
            }
            await CloseAllFilesInternal();
            statusText.Text = "已关闭文件";
            Log("文件已关闭");
        }

        private Task CloseAllFilesInternal()
        {
            try
            {
                _am.UnloadAllAssetsFiles(true);
                _am.UnloadAllBundleFiles();
                _bundleInst = null;
                _workspace = null;
                _standaloneAssetsInst = null;
                _assetWorkspace = null;
                _items.Clear();
                _changesUnsaved = false;
                _changesMade = false;
                SetBundleControlsEnabled(false, false);
                statusText.Text = "就绪";
            }
            catch (Exception ex)
            {
                Log("关闭异常: " + ex);
            }
            return Task.CompletedTask;
        }
    }

    public class AssetListItem
    {
        public string Name { get; set; } = "";
        public string SizeText { get; set; } = "";
        public string TypeName { get; set; } = "";
        public long PathId { get; set; }
        public AssetBundleDirectoryInfo? DirInfo { get; set; }
        public AssetFileInfo? AssetInfo { get; set; }
        public AssetsFileInstance? FileInstance { get; set; }
        public AssetContainer? Container { get; set; }
        public bool IsAsset { get; set; }
    }
}
