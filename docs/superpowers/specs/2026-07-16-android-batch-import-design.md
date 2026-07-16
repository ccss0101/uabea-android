# Android 批量导入功能设计

- **日期**:2026-07-16
- **状态**:已批准(待 spec 审查)
- **子项目**:#1 批量导入(「将桌面版全部适配到手机」路线图的第一个子项目)
- **方案**:B(分两阶段 + 资产级仅 raw)

## 1. 背景与目标

UABEA Android fork(`ccss0101/uabea-android`,v1.3.0)当前只支持 bundle 级**单项**导入(`btnImport` → `importOverlay` → `DoImport`),缺少批量导入能力。桌面版 UABEA 有两套批量导入机制:

1. **bundle 级**(`MainWindow.BtnImportAll_Click`):选目录 → 递归遍历 → 每个文件作为 bundle 条目导入。
2. **资产级**(`InfoWindow.BatchImportRaw` + `ImportBatch` 对话框):往 .assets 里塞多个资产的 raw 数据,按文件名模式 `-{assetFile}-{PathID}.dat` 匹配。

本子项目把这两套批量导入适配到 Android,实现「一下子能导入一个文件目录」。

## 2. 范围

### 本子项目包含
- **阶段 1**:bundle 级批量导入(照搬桌面 `BtnImportAll_Click`),复用现有 `BundleWorkspace`。
- **阶段 2**:资产级 raw 批量导入,需先接入 `AssetWorkspace`(当前 Android 未用),仅支持**独立 .assets 文件**。
- 进度反馈、错误处理、手动测试矩阵。

### 本子项目不含(归入其他子项目)
- 资产级 **dump** 批量导入(`BatchImportDump`,依赖 TypeTree 反序列化)→ 归入 **#3 资产级编辑核心**(与 TypeTree 查看同批做,共享 TypeTree 反序列化代码)。
- **bundle 内 .assets 的资产级入口**(双击条目进资产视图)→ 归入 **#3**(需完整 InfoWindow 等价物)。
- #0 硬障碍验证(native lib / Silk.NET)、#2 导航搜索、#4 Scene Hierarchy、#5 ModMaker、#6 插件系统、#7 杂项。

### 平台约束
- Android 单 UserControl 架构(`MainView`),无多窗口 → 对话框适配为 overlay。
- 系统文件选择器会触发独立 Activity 导致 UABEA 掉后台被回收 → 复用内置 `FileBrowserView`(`System.IO` 遍历)。
- 耗时操作不得阻塞 UI 线程 → 用 `Task.Run` + `Dispatcher.Post` 更新进度。

## 3. 架构概览

### 不动的部分
- 桌面端代码(`UABEAvalonia/`、`UABEA.Core/`)不修改。
- `FileBrowserView` 的 `Directory` 模式(已被批量导出 `BtnExportAll_Click` 使用)直接复用,不增强。

### 改动的部分(全部在 `UABEA.Android/UABEA.Android/`)
- `MainView.axaml`:新增「导入全部」按钮(Row 3)、新增「批量导入」按钮(Row 5)、`assetList` 改多选、新增 `ImportBatchView` overlay 容器、复用 `progressOverlay`。
- `MainView.axaml.cs`:`PendingFileAction` 新增 `ImportAll`、`BatchImportRaw`;新增 `DoImportAllToDir`、`DoBatchImportRaw`;接入 `AssetWorkspace`;`RefreshAssetListFromAssetsFile` 改从 `LoadedAssets` 取 `AssetContainer`;单项操作适配多选(取首个)。
- 新增 `ImportBatchView.axaml(.cs)`:资产级匹配 overlay。

### 复用的 UABEA.Core API(已就绪,无需改)
- `BundleWorkspace.AddOrReplaceFile(stream, name, isSerialized)` — bundle 级导入。
- `FileTypeDetector.DetectFileType(path)` — 判断是否序列化。
- `AssetWorkspace(am, fromBundle)` + `LoadAssetsFile(inst)` — 加载 .assets + 依赖。
- `AssetWorkspace.LoadedAssets`(`Dictionary<AssetID, AssetContainer>`) — 资产容器。
- `AssetImportExport.ImportRawAsset(fs)` → `byte[]`。
- `AssetImportExport.CreateAssetReplacer(cont, bytes)` → `AssetsReplacer`。
- `AssetWorkspace.AddReplacer(inst, replacer, previewStream)`。

## 4. 阶段 1:bundle 级批量导入

### 4.1 UI 入口
- [MainView.axaml](../../UABEA.Android/UABEA.Android/MainView.axaml) Row 3(`导出全部`旁边)新增 `btnImportAll`「导入全部」按钮,`Height="36" MinWidth="80"`,`IsEnabled="False"`。
- 启用条件:`_bundleInst != null`(仅 bundle 模式;独立 .assets 不支持 bundle 级导入)。

### 4.2 流程
1. `btnImportAll.Click` → `_pendingFileAction = PendingFileAction.ImportAll` → `ShowFileBrowser(FileBrowserView.BrowserMode.Directory)`。
2. `FileBrowser_FileSelected` 新增 `ImportAll` 分支 → `DoImportAllToDir(dir)`。

### 4.3 `DoImportAllToDir` 行为(对齐桌面 [MainWindow.axaml.cs:443-479](../../UABEAvalonia/Forms/MainWindow.axaml.cs#L443-L479))
1. `var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList()` — 递归收集(先 ToList 拿总数供进度)。
2. 显示 `progressOverlay`:`progressBar.IsIndeterminate = false`、`Maximum = files.Count`、`Value = 0`、`progressText = "导入中 0/{count}"`。
3. 整个循环放 `await Task.Run(() => { ... })`,内部每个文件后用 `Dispatcher.Post(() => { progressBar.Value++; progressText.Text = $"导入中 {i}/{n}"; })`。
4. 循环体(每个文件):
   - `relPath = Path.GetRelativePath(dir, filePath).Replace("\\", "/").TrimEnd('/')`。
   - `var existing = _workspace!.Files.FirstOrDefault(f => f.Name == relPath);`
   - `bool isSerialized = existing != null ? existing.IsSerialized : FileTypeDetector.DetectFileType(filePath) == DetectedFileType.AssetsFile;`
   - `_workspace.AddOrReplaceFile(File.OpenRead(filePath), relPath, isSerialized);`
   - 单文件 `try/catch`:失败记日志,`continue`(不中断整批)。
5. 结束(`finally`):隐藏 `progressOverlay`;`SetChanges(true)`;`RefreshAssetListFromBundle()`;`statusText = $"已批量导入 {successCount}/{files.Count} 项"`;日志汇总。

### 4.4 冲突策略
直接替换(`AddOrReplaceFile` 本身是 add-or-replace 语义),与桌面一致;不做跳过/询问,保持简单。已存在条目沿用其 `IsSerialized`,新条目用 `FileTypeDetector` 判断。

### 4.5 异常
- 单文件失败:catch → 日志「跳过 {file}: {ex.Message}」,继续。
- 整批外层 try/catch:日志 + statusText 提示,确保 `progressOverlay` 隐藏。

## 5. 阶段 2:资产级 raw 批量导入

### 5.1 接入 AssetWorkspace
- [MainView.axaml.cs:320](../../UABEA.Android/UABEA.Android/MainView.axaml.cs#L320) 附近(打开独立 .assets 分支):
  - 现有:`_am.LoadAssetsFile(path, true)` → 加载 class database → `RefreshAssetListFromAssetsFile()`。
  - 新增:构造 `AssetWorkspace _assetWorkspace = new AssetWorkspace(_am, fromBundle: false);` 并 `_assetWorkspace.LoadAssetsFile(_standaloneAssetsInst, true);`(加载依赖)。
  - 保留 `_standaloneAssetsInst` 供现有预览/Info 逻辑用;`_assetWorkspace` 供资产级写操作。
  - 关闭文件时置空 `_assetWorkspace`。
- 新增字段:`private AssetWorkspace? _assetWorkspace;`

### 5.2 RefreshAssetListFromAssetsFile 改造
- 从 `_assetWorkspace.LoadedAssets.Values` 取 `AssetContainer`(而非裸 `fileInst.file.AssetInfos`)。
- 每个 `AssetContainer cont` 构造 `AssetListItem`:`Name = $"{typeName} #{cont.PathId}"`、`PathId = cont.PathId`、`IsAsset = true`、新增 `Container = cont` 字段(`AssetContainer.PathId` 为 `long`)。
- `typeName` 仍通过 `cldb.FindAssetClassByID(cont.ClassId)` 取(与现有逻辑一致;`AssetContainer.ClassId` 为 `int`)。

### 5.3 AssetListItem 扩展
- 新增字段 `public AssetContainer? Container;`(bundle 模式为 null,资产模式非 null)。
- bundle 级单项操作对 `Container == null` 做兼容(不影响现有 bundle 行为)。

### 5.4 assetList 多选
- [MainView.axaml:54](../../UABEA.Android/UABEA.Android/MainView.axaml#L54) `assetList` 加 `SelectionMode="Multiple"`。
- 单项操作(预览/导出/导入/Dump/重命名/删除)取首个:`var item = assetList.SelectedItems.OfType<AssetListItem>().FirstOrDefault();`(替换原 `assetList.SelectedItem as AssetListItem`)。
- 批量导入取全集:`var selection = assetList.SelectedItems.OfType<AssetListItem>().Where(i => i.Container != null).Select(i => i.Container!).ToList();`

### 5.5 UI 入口
- Row 5(`预览/Info/导出/导入/Dump/重命名/删除`所在 WrapPanel)新增 `btnBatchImportRaw`「批量导入」按钮,`Height="46" MinWidth="64"`,`IsEnabled="False"`。
- 启用条件:`_standaloneAssetsInst != null`(仅独立 .assets 模式;bundle 模式禁用)。
- 点击校验:`selection.Count == 0` 时提示「请先选中至少一个资产」并返回。

### 5.6 流程
1. `btnBatchImportRaw.Click` → 校验选中 → `_pendingFileAction = PendingFileAction.BatchImportRaw` → `ShowFileBrowser(Directory)`。
2. `FileBrowser_FileSelected` 新增 `BatchImportRaw` 分支 → `ShowImportBatchView(dir)`。

### 5.7 ImportBatchView overlay(适配桌面 [ImportBatch.axaml.cs](../../UABEAvalonia/Forms/ImportBatch.axaml.cs))
- 新增文件:`UABEA.Android/UABEA.Android/ImportBatchView.axaml` + `.axaml.cs`。
- `UserControl`,挂到 `MainView.axaml` 新增的 `importBatchOverlay` Border 上。
- 构造:`ImportBatchView(AssetWorkspace workspace, List<AssetContainer> selection, string dir, List<string> extensions)`。
- 匹配逻辑(照搬桌面 [ImportBatch.axaml.cs:35-83](../../UABEAvalonia/Forms/ImportBatch.axaml.cs#L35-L83)):
  - `extensions = ["dat"]`(raw 固定扩展名)。
  - `filesInDir = FileUtils.GetFilesInDirectory(dir, extensions)`(`FileUtils` 在 UABEA.Core,Android 可直接用;**不递归**顶层枚举)。
  - 对每个 `cont`:`AssetNameUtils.GetDisplayNameFast(_assetWorkspace, cont, true, out string assetName, out _)` 取显示名(`AssetNameUtils` 在 UABEAvalonia/Utils,未被 Android csproj 排除,已 Link 可用)。
  - `matchName = GetMatchName("dat")` 生成模式 `-{File}-{PathID}.dat`(其中 `File = Path.GetFileName(cont.FileInstance.path)`、`PathID = cont.PathId`,见桌面 [ImportBatch.axaml.cs:153-159](../../UABEAvalonia/Forms/ImportBatch.axaml.cs#L153-L159))。
  - 候选 = `filesInDir.Where(f => f.EndsWith(matchName)).Select(Path.GetFileName).ToList()`(**后缀匹配 `EndsWith`**,与桌面一致,允许文件名前有自定义前缀)。
- UI:
  - 顶部:标题「批量导入(raw)」+ 文件计数。
  - 主体 `ScrollViewer` + `ItemsControl`:每行一个资产,显示「资产名」+ 匹配状态下拉(`ComboBox`,候选为匹配文件名,默认选第一个);无匹配时显示红色「无匹配文件」并禁用该行下拉。
  - 底部:「确认导入」/「取消」按钮。
- 确认:收集所有「有匹配且用户未禁用」的行 → 返回 `List<ImportBatchInfo>{ cont, importFile }`(通过事件回调,非 `ShowDialog`,因 Android 无窗口对话框)。

### 5.8 执行 raw 批量导入(照搬桌面 [InfoWindow.axaml.cs:817-831](../../UABEAvalonia/Forms/InfoWindow.axaml.cs#L817-L831))
1. `ImportBatchView` 确认回调返回 `List<ImportBatchInfo> batchInfos`。
2. 显示 `progressOverlay`(确定性进度,`Maximum = batchInfos.Count`)。
3. `await Task.Run(() => { foreach (var bi in batchInfos) { ... } })`,每个:
   - `using var fs = File.OpenRead(bi.importFile);`
   - `var importer = new AssetImportExport();`
   - `byte[] bytes = importer.ImportRawAsset(fs);`
   - `AssetsReplacer replacer = AssetImportExport.CreateAssetReplacer(bi.cont, bytes);`
   - `_assetWorkspace!.AddReplacer(bi.cont.FileInstance, replacer, new MemoryStream(bytes));`
   - 单项 `try/catch`:失败记日志,继续。
   - `Dispatcher.Post` 更新进度。
4. 结束:隐藏 `progressOverlay`;`SetChanges(true)`;`RefreshAssetListFromAssetsFile()`;`statusText = $"已批量导入 {successCount}/{batchInfos.Count} 个资产"`。

## 6. 错误处理汇总

| 场景 | 处理 |
|---|---|
| bundle 级:目录不可访问 | catch 外层,提示并返回,不报错 |
| bundle 级:单文件失败 | 记日志,继续下一文件 |
| bundle 级:空目录 | 进度 0/0,提示「未找到文件」,不报错 |
| 资产级:未选中资产 | 提示「请先选中至少一个资产」,不打开浏览器 |
| 资产级:无匹配文件 | ImportBatchView 标红,该项跳过,不阻塞其他项 |
| 资产级:.dat 损坏(`ImportRawAsset` 异常) | 该项记失败日志,继续下一项 |
| 资产级:目录为空 | ImportBatchView 全部标红,确认后导入 0 项 |
| 进度期间用户操作 | overlay 遮罩天然阻挡其他交互 |
| AssetWorkspace 加载依赖失败 | 日志警告,继续(部分资产可能缺类型信息) |

## 7. 测试策略

Android 端无 headless Avalonia 测试设施,采用**手动测试矩阵**:

### 7.1 bundle 级
- 空目录:提示「未找到文件」,无异常。
- 单文件:导入 1 项,relPath 为文件名。
- 多文件(顶层):全部导入。
- 嵌套子目录:验证 `relPath` 保留 `subdir/file` 结构,且与 bundle 内已有条目名匹配时替换。
- 同名替换:bundle 已有 `CAB-xxx.assets` 条目,目录里有同名文件 → 沿用原 `IsSerialized` 替换。
- 全新文件:bundle 无该条目 → `FileTypeDetector` 判断 `.assets` 为序列化、其他为非序列化 → 新增。
- 混合文件:目录含 `.assets`/`.png`/`.txt` 混合,全部作为条目导入(不按扩展名过滤)。
- 单文件损坏:`AddOrReplaceFile` 异常 → 记日志跳过,其余继续。

### 7.2 资产级
- 单选 + 批量导入:1 个资产,1 个匹配 .dat。
- 多选 + 批量导入:N 个资产,各自匹配。
- 无匹配文件:目录里没有对应 `-{file}-{PathID}.dat` → 标红,确认后导入 0 项。
- 部分匹配:N 个资产中部分有匹配 → 只导入有匹配的。
- 全部匹配。
- .dat 损坏:`ImportRawAsset` 抛异常 → 该项失败,其余继续。
- 多选后取消:不导入,列表不变。

### 7.3 回归
- assetList 改多选后,单项操作(预览/导出/导入/Dump/重命名/删除)取首个选中项,行为与改动前一致。
- bundle 模式下 `btnBatchImportRaw` 禁用;独立 .assets 模式下 `btnImportAll` 禁用。
- 打开/保存/压缩/关闭不受影响。

## 8. 文件改动清单

### 新增
- `UABEA.Android/UABEA.Android/ImportBatchView.axaml`
- `UABEA.Android/UABEA.Android/ImportBatchView.axaml.cs`
- (无需新增 `ImportBatchInfo.cs`:`UABEAvalonia.ImportBatchInfo` 已在 [ImportBatch.axaml.cs:131-138](../../UABEAvalonia/Forms/ImportBatch.axaml.cs#L131-L138) 定义且被 Android csproj Link 进来,直接复用其字段 `cont` / `importFile` / `assetName` / `assetFile` / `pathId`。)

### 修改
- `UABEA.Android/UABEA.Android/MainView.axaml`:新增 2 按钮、`assetList` 改 `SelectionMode="Multiple"`、新增 `importBatchOverlay` 容器。
- `UABEA.Android/UABEA.Android/MainView.axaml.cs`:新增枚举值 `ImportAll`/`BatchImportRaw`、`_assetWorkspace` 字段、`DoImportAllToDir`、`ShowImportBatchView`、`DoBatchImportRaw`、接入 `AssetWorkspace`、`RefreshAssetListFromAssetsFile` 改造、单项操作适配多选(取 `SelectedItems` 首个)。
- `AssetListItem`(定义在 [MainView.axaml.cs:1643-1652](../../UABEA.Android/UABEA.Android/MainView.axaml.cs#L1643-L1652)):新增 `public AssetContainer? Container;` 字段。

### 不改
- `UABEA.Core/`、`UABEAvalonia/`、各插件、`FileBrowserView`。

## 9. 开放问题
无。所有设计决策已与用户确认:
- 范围:bundle 级 + 资产级 raw(方案 B,分两阶段)。
- bundle 级行为:递归、自动判断序列化、同名直接替换(照搬桌面)。
- 资产级边界:仅独立 .assets;bundle 内 .assets 入口 + dump 归 #3。
- UI:overlay 模式、assetList 多选、复用 FileBrowserView Directory 模式与 progressOverlay。
