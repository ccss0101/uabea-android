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

        /// <summary>内置文件选择器的挂起操作类型</summary>
        private enum PendingFileAction
        {
            Open,        // 打开 bundle/assets 文件
            Import,      // 导入替换文件
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
            btnInfo.Click += BtnInfo_Click;
            btnExport.Click += BtnExport_Click;
            btnImport.Click += BtnImport_Click;
            btnDump.Click += BtnDump_Click;
            btnRename.Click += BtnRename_Click;
            btnRemove.Click += BtnRemove_Click;
            btnPreview.Click += BtnPreview_Click;

            btnReplaceTexture.Click += BtnReplaceTexture_Click;
            btnSaveText.Click += BtnSaveText_Click;
            btnClosePreview.Click += (s, e) => { previewOverlay.IsVisible = false; };

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

        private async Task OpenFile(string path)
        {
            try
            {
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
                    SizeText = $"Size: {inf.ByteSize} bytes",
                    PathId = inf.PathId,
                    IsAsset = true,
                    AssetInfo = inf,
                    FileInstance = fileInst
                });
            }
            // 同步 _allItems
            _allItems.Clear();
            foreach (var i in _items) _allItems.Add(i);
            ApplySearchFilter();
        }

        /// <summary>根据搜索框文本过滤资产列表</summary>
        private void ApplySearchFilter()
        {
            var keyword = searchBox?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(keyword))
            {
                // 无关键字：显示全部
                if (_items.Count != _allItems.Count)
                {
                    _items.Clear();
                    foreach (var i in _allItems) _items.Add(i);
                }
                return;
            }

            _items.Clear();
            foreach (var item in _allItems)
            {
                if (item.Name != null && item.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    _items.Add(item);
            }
        }

        private void AssetList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            bool hasSel = assetList.SelectedItem != null;
            bool isBundle = _bundleInst != null;
            btnPreview.IsEnabled = hasSel;
            btnInfo.IsEnabled = hasSel;
            btnExport.IsEnabled = hasSel;
            btnImport.IsEnabled = hasSel && isBundle;
            btnDump.IsEnabled = hasSel;
            btnRename.IsEnabled = hasSel && isBundle;
            btnRemove.IsEnabled = hasSel && isBundle;
        }

        /// <summary>设置 bundle 相关按钮的启用状态</summary>
        private void SetBundleControlsEnabled(bool enabled, bool isBundle)
        {
            btnExportAll.IsEnabled = enabled && isBundle;
            btnClose.IsEnabled = enabled;
            btnSave.IsEnabled = false; // 保存只在有修改时启用
            btnCompress.IsEnabled = enabled && isBundle;
            AssetList_SelectionChanged(null, null!);
        }

        // ==================== 导出 ====================
        // 使用内置文件选择器（Save 模式）选择导出目标，避免系统选择器导致掉后台
        private void BtnExport_Click(object? sender, RoutedEventArgs e)
        {
            var item = assetList.SelectedItem as AssetListItem;
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

        // ==================== 导入（插件替换） ====================
        // 使用内置文件选择器（Open 模式）选择导入源文件，避免系统选择器导致掉后台
        private void BtnImport_Click(object? sender, RoutedEventArgs e)
        {
            var item = assetList.SelectedItem as AssetListItem;
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
            var item = assetList.SelectedItem as AssetListItem;
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
        private void BtnInfo_Click(object? sender, RoutedEventArgs e)
        {
            var item = assetList.SelectedItem as AssetListItem;
            if (item == null) return;

            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("=== Asset Info ===");
                sb.AppendLine($"Name: {item.Name}");
                sb.AppendLine($"SizeText: {item.SizeText}");

                if (item.IsAsset && item.FileInstance != null && item.AssetInfo != null)
                {
                    var fileInst = item.FileInstance;
                    var inf = item.AssetInfo;
                    sb.AppendLine($"Unity Version: {fileInst.file.Metadata.UnityVersion}");
                    sb.AppendLine($"Target Platform: {fileInst.file.Metadata.TargetPlatform}");
                    sb.AppendLine($"PathId: {inf.PathId}");
                    sb.AppendLine($"TypeId: {inf.TypeId}");
                    sb.AppendLine($"ByteSize: {inf.ByteSize}");

                    var cldb = _am.ClassDatabase;
                    var type = cldb.FindAssetClassByID(inf.TypeId);
                    if (type != null)
                    {
                        sb.AppendLine($"TypeName: {cldb.GetString(type.Name)}");
                    }
                }
                else if (item.DirInfo != null && _bundleInst != null)
                {
                    using var ms = GetItemStream(item);
                    if (ms != null)
                    {
                        ms.Position = 0;
                        var fileType = FileTypeDetector.DetectFileType(new AssetsFileReader(ms), 0);
                        sb.AppendLine($"Detected Type: {fileType}");
                        sb.AppendLine($"Offset: {item.DirInfo.Offset}");
                        sb.AppendLine($"DecompressedSize: {item.DirInfo.DecompressedSize}");

                        if (fileType == DetectedFileType.AssetsFile)
                        {
                            ms.Position = 0;
                            string assetMemPath = Path.Combine(_bundleInst.path, item.Name);
                            var subInst = _am.LoadAssetsFile(ms, assetMemPath, true);
                            sb.AppendLine($"Unity Version: {subInst.file.Metadata.UnityVersion}");
                            sb.AppendLine($"Target Platform: {subInst.file.Metadata.TargetPlatform}");
                            sb.AppendLine($"Assets: {subInst.file.AssetInfos.Count}");
                            sb.AppendLine();
                            sb.AppendLine("=== Asset List ===");
                            var subCldb = _am.ClassDatabase;
                            foreach (var subInf in subInst.file.AssetInfos)
                            {
                                var subType = subCldb.FindAssetClassByID(subInf.TypeId);
                                string subTypeName = subType != null ? subCldb.GetString(subType.Name) : $"0x{subInf.TypeId:X}";
                                sb.AppendLine($"  [{subTypeName}] PathId={subInf.PathId} Size={subInf.ByteSize}");
                            }
                        }
                    }
                }

                Log(sb.ToString());
                statusText.Text = $"Info: {item.Name}";
            }
            catch (Exception ex)
            {
                Log("Info 异常: " + ex);
                statusText.Text = "Info 失败: " + ex.Message;
            }
        }

        // ==================== Dump ====================
        // 使用内置文件选择器（Save 模式）选择 Dump 输出路径
        private void BtnDump_Click(object? sender, RoutedEventArgs e)
        {
            var item = assetList.SelectedItem as AssetListItem;
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
            var item = assetList.SelectedItem as AssetListItem;
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
            var item = assetList.SelectedItem as AssetListItem;
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
        public long PathId { get; set; }
        public AssetBundleDirectoryInfo? DirInfo { get; set; }
        public AssetFileInfo? AssetInfo { get; set; }
        public AssetsFileInstance? FileInstance { get; set; }
        public bool IsAsset { get; set; }
    }
}
