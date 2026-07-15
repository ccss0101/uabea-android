using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
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

        // 修改状态跟踪
        private bool _changesUnsaved;
        private bool _changesMade;

        // 弹出层相关
        private AssetListItem? _renameTarget;
        private string? _importTempPath;       // 导入文件复制到缓存的路径
        private string? _importOrigName;       // 要替换的原文件名（如果有）

        // 预览/替换相关
        private AssetsFileInstance? _previewFileInst;
        private AssetFileInfo? _previewAssetInf;
        private bool _previewIsFromBundle;
        private string? _previewBundleEntryName;
        private TextureFormat _previewTexFormat;
        private int _previewTexWidth;
        private int _previewTexHeight;

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

            renameOk.Click += RenameOk_Click;
            renameCancel.Click += RenameCancel_Click;

            importYes.Click += (s, e) => DoImport(true);
            importNo.Click += (s, e) => DoImport(false);
            importCancel.Click += (s, e) => { importOverlay.IsVisible = false; CleanupImport(); };

            compLz4.Click += (s, e) => DoCompress(AssetBundleCompressionType.LZ4);
            compLzma.Click += (s, e) => DoCompress(AssetBundleCompressionType.LZMA);
            compCancel.Click += (s, e) => { compressOverlay.IsVisible = false; };
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
        private async void BtnOpen_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (_changesUnsaved && _bundleInst != null)
                {
                    // 简单提示，不做对话框
                    Log("有未保存的修改，继续打开将丢弃");
                }

                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) { Log("无法获取 TopLevel"); return; }

                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
                {
                    Title = "选择 AssetBundle 或 .assets 文件",
                    FileTypeFilter = new List<FilePickerFileType>
                    {
                        new FilePickerFileType("All files") { Patterns = new List<string> { "*" } }
                    }
                });

                if (files == null || files.Count == 0) return;

                var storageFile = files[0];
                string? path = storageFile.TryGetLocalPath();

                if (string.IsNullOrEmpty(path))
                {
                    Log("TryGetLocalPath 返回 null，使用流复制到缓存...");
                    path = await CopyStorageFileToCache(storageFile);
                }

                if (string.IsNullOrEmpty(path)) { Log("无法获取文件路径"); return; }

                Log($"打开文件: {path}");
                await OpenFile(path);
            }
            catch (Exception ex)
            {
                Log("打开文件异常: " + ex);
                statusText.Text = "打开失败: " + ex.Message;
            }
        }

        /// <summary>把 IStorageFile 的内容复制到缓存目录，返回临时文件路径。解决 Android content:// URI 无法直接访问的问题。</summary>
        private async Task<string?> CopyStorageFileToCache(IStorageFile storageFile)
        {
            try
            {
                var cacheDir = AppPaths.GetCacheDir();
                Directory.CreateDirectory(cacheDir);
                var name = storageFile.Name;
                if (string.IsNullOrEmpty(name)) name = "uabea_input";
                foreach (var c in Path.GetInvalidFileNameChars())
                    name = name.Replace(c, '_');
                var tempPath = Path.Combine(cacheDir, $"{DateTime.Now:yyyyMMdd_HHmmss}_{name}");

                await using var src = await storageFile.OpenReadAsync();
                using var dst = File.Create(tempPath);
                await src.CopyToAsync(dst);

                Log($"已复制到缓存: {tempPath} ({new FileInfo(tempPath).Length} bytes)");
                return tempPath;
            }
            catch (Exception ex)
            {
                Log("CopyStorageFileToCache 异常: " + ex);
                return null;
            }
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
            if (_bundleInst == null) return;

            foreach (var dirInf in _bundleInst.file.BlockAndDirInfo.DirectoryInfos)
            {
                _items.Add(new AssetListItem
                {
                    Name = dirInf.Name,
                    SizeText = $"Size: {dirInf.DecompressedSize} bytes",
                    DirInfo = dirInf
                });
            }
        }

        private void RefreshAssetListFromAssetsFile(AssetsFileInstance fileInst)
        {
            _items.Clear();
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
        private async void BtnExport_Click(object? sender, RoutedEventArgs e)
        {
            var item = assetList.SelectedItem as AssetListItem;
            if (item == null) return;

            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "导出为...",
                    SuggestedFileName = Path.GetFileName(item.Name)
                });

                if (file == null) return;

                using var ms = GetItemStream(item);
                if (ms == null) { Log("无法读取数据"); return; }
                ms.Position = 0;

                await using var fs = await file.OpenWriteAsync();
                await ms.CopyToAsync(fs);

                Log($"已导出: {file.Name}");
                statusText.Text = "导出成功: " + file.Name;
            }
            catch (Exception ex)
            {
                Log("导出异常: " + ex);
                statusText.Text = "导出失败: " + ex.Message;
            }
        }

        private async void BtnExportAll_Click(object? sender, RoutedEventArgs e)
        {
            if (_bundleInst == null) return;

            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "选择导出目录"
                });

                if (folders == null || folders.Count == 0) return;

                var dir = folders[0].TryGetLocalPath();
                if (string.IsNullOrEmpty(dir))
                {
                    Log("导出目录路径为 null（Android 安全存储限制），改用缓存目录");
                    dir = Path.Combine(AppPaths.GetCacheDir(), "uabea_export");
                    Directory.CreateDirectory(dir);
                }

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
        private async void BtnImport_Click(object? sender, RoutedEventArgs e)
        {
            var item = assetList.SelectedItem as AssetListItem;
            if (item == null || _workspace == null) return;

            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
                {
                    Title = "选择要导入的文件",
                    FileTypeFilter = new List<FilePickerFileType>
                    {
                        new FilePickerFileType("All files") { Patterns = new List<string> { "*" } }
                    }
                });

                if (files == null || files.Count == 0) return;

                var storageFile = files[0];
                string? path = storageFile.TryGetLocalPath();
                if (string.IsNullOrEmpty(path))
                {
                    Log("导入文件复制到缓存...");
                    path = await CopyStorageFileToCache(storageFile);
                }
                if (string.IsNullOrEmpty(path)) { Log("无法获取导入文件路径"); return; }

                _importTempPath = path;
                _importOrigName = item.Name; // 替换当前选中项
                importFileName.Text = $"替换: {item.Name}\n导入: {storageFile.Name}";
                importOverlay.IsVisible = true;
            }
            catch (Exception ex)
            {
                Log("导入异常: " + ex);
                statusText.Text = "导入失败: " + ex.Message;
                CleanupImport();
            }
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
        private async void BtnDump_Click(object? sender, RoutedEventArgs e)
        {
            var item = assetList.SelectedItem as AssetListItem;
            if (item == null) return;

            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Dump 到...",
                    SuggestedFileName = Path.GetFileNameWithoutExtension(item.Name) + "_dump.txt"
                });
                if (file == null) return;

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

                await using (var fs = await file.OpenWriteAsync())
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
                    await fs.WriteAsync(bytes, 0, bytes.Length);
                }
                Log($"Dump 完成: {file.Name}");
                statusText.Text = "Dump 成功: " + file.Name;
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
        private async void BtnSave_Click(object? sender, RoutedEventArgs e)
        {
            if (_bundleInst == null || _workspace == null || !_changesUnsaved) return;

            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                // 用 SaveFilePicker 选择保存位置（SaveAs 模式，避免覆盖原文件导致流冲突）
                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "保存 Bundle 为...",
                    SuggestedFileName = Path.GetFileName(_bundleInst.path)
                });

                if (file == null) return;

                ShowProgress("保存中...");

                string? path = file.TryGetLocalPath();
                if (string.IsNullOrEmpty(path))
                {
                    // Android: 先写到缓存，再复制到 StorageProvider 流
                    path = Path.Combine(AppPaths.GetCacheDir(), $"save_{DateTime.Now:HHmmss}_{file.Name}");
                    SaveBundleToPath(path);
                    await using var dst = await file.OpenWriteAsync();
                    using var src = File.OpenRead(path);
                    await src.CopyToAsync(dst);
                    try { File.Delete(path); } catch { }
                }
                else
                {
                    SaveBundleToPath(path);
                }

                _changesUnsaved = false;
                UpdateStatusText();
                btnSave.IsEnabled = false;
                HideProgress();
                Log($"保存成功: {file.Name}");
                statusText.Text = "保存成功: " + file.Name;
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

        private async void DoCompress(AssetBundleCompressionType compType)
        {
            compressOverlay.IsVisible = false;
            if (_bundleInst == null) return;

            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "压缩保存为...",
                    SuggestedFileName = Path.GetFileNameWithoutExtension(_bundleInst.name) + "_compressed"
                });
                if (file == null) return;

                ShowProgress($"压缩中 ({compType})...");

                // 压缩在后台线程执行
                string? path = file.TryGetLocalPath();
                bool usedCache = false;
                if (string.IsNullOrEmpty(path))
                {
                    path = Path.Combine(AppPaths.GetCacheDir(), $"comp_{DateTime.Now:HHmmss}_{file.Name}");
                    usedCache = true;
                }

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

                if (usedCache)
                {
                    await using var dst = await file.OpenWriteAsync();
                    using var src = File.OpenRead(finalPath);
                    await src.CopyToAsync(dst);
                    try { File.Delete(finalPath); } catch { }
                }

                HideProgress();
                Log($"压缩完成 ({compType}): {file.Name}");
                statusText.Text = $"压缩成功: {file.Name}";
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
        private async void BtnReplaceTexture_Click(object? sender, RoutedEventArgs e)
        {
            if (_previewFileInst == null || _previewAssetInf == null) return;

            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
                {
                    Title = "选择替换图片 (PNG/JPG)",
                    FileTypeFilter = new List<FilePickerFileType>
                    {
                        new FilePickerFileType("图片") { Patterns = new List<string> { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.tga" } },
                        new FilePickerFileType("All files") { Patterns = new List<string> { "*" } }
                    }
                });

                if (files == null || files.Count == 0) return;

                var storageFile = files[0];
                string? path = storageFile.TryGetLocalPath();
                if (string.IsNullOrEmpty(path))
                {
                    path = await CopyStorageFileToCache(storageFile);
                }
                if (string.IsNullOrEmpty(path)) { Log("无法获取图片路径"); return; }

                ShowProgress("编码贴图...");

                bool success = false;
                string errorMsg = "";
                byte[]? savedAssetsData = null;

                await Task.Run(() =>
                {
                    try
                    {
                        // 加载图片
                        using var image = Image.Load<Rgba32>(path);
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
                        // 独立 .assets：保存到文件
                        var tl = TopLevel.GetTopLevel(this);
                        if (tl != null)
                        {
                            var file = await tl.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                            {
                                Title = "保存 .assets 为...",
                                SuggestedFileName = Path.GetFileName(_previewFileInst!.path)
                            });
                            if (file != null)
                            {
                                string? savePath = file.TryGetLocalPath();
                                if (string.IsNullOrEmpty(savePath))
                                {
                                    savePath = Path.Combine(AppPaths.GetCacheDir(), $"save_{DateTime.Now:HHmmss}_{file.Name}");
                                }
                                File.WriteAllBytes(savePath, savedAssetsData);
                                Log($"贴图已保存: {file.Name}");
                                statusText.Text = "贴图保存成功: " + file.Name;
                            }
                        }
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
                    // 独立 .assets：保存到文件
                    var topLevel = TopLevel.GetTopLevel(this);
                    if (topLevel == null) return;

                    var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                    {
                        Title = "保存 .assets 为...",
                        SuggestedFileName = Path.GetFileName(_previewFileInst.path)
                    });
                    if (file == null) return;

                    string? path = file.TryGetLocalPath();
                    if (string.IsNullOrEmpty(path))
                    {
                        path = Path.Combine(AppPaths.GetCacheDir(), $"save_{DateTime.Now:HHmmss}_{file.Name}");
                    }

                    using (var fs = File.Open(path, FileMode.Create))
                    using (var w = new AssetsFileWriter(fs))
                    {
                        _previewFileInst.file.Write(w, 0, replacers);
                    }

                    Log($"文本已保存: {file.Name}");
                    statusText.Text = "文本保存成功: " + file.Name;
                }

                previewOverlay.IsVisible = false;
            }
            catch (Exception ex)
            {
                Log("保存文本异常: " + ex);
                statusText.Text = "保存失败: " + ex.Message;
            }
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
