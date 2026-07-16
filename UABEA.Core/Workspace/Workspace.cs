using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.Collections.Generic;
using System.IO;

namespace UABEAvalonia
{
    // 多 bundle 工作区。与旧的 BundleWorkspace / AssetWorkspace 并存，作为过渡方案。
    //
    // 设计参考 UABEANext4 的 AssetWorkspace.Workspace，但做了如下简化：
    //   - 不实现保存逻辑（留给后续 P1）
    //   - 不实现并行加载（保持串行）
    //   - 不依赖 AssetsManager.FileLookup / GetFileLookupKey
    //     （当前引用的 AssetsTools.NET 版本尚未提供这些 API），
    //     去重改用本类自己的 ItemLookup 字典。
    //
    // 工作区以树形结构组织：RootItems 保存所有根级文件；bundle 内部的子文件
    // 作为该 bundle 项的 Children，并设置 Parent 指向父 bundle。
    public class Workspace
    {
        // 资产管理器，负责加载 bundle / assets 文件并维护依赖关系
        public AssetsManager am { get; }

        // 所有根级文件（独立的 bundle / assets / resource）
        public List<WorkspaceItem> RootItems { get; }

        // 按路径快速查找工作区项（键为规范化后的路径）
        public Dictionary<string, WorkspaceItem> ItemLookup { get; }

        // 文件项变更事件，供 UI 层订阅
        public delegate void WorkspaceItemEvent(WorkspaceItem item);
        public event WorkspaceItemEvent? ItemAdded;
        public event WorkspaceItemEvent? ItemRemoved;
        public event WorkspaceItemEvent? ItemUpdated;

        // 通知订阅者某个工作区项已更新（例如编辑后）。供 UI / 后续保存逻辑调用。
        public void NotifyItemUpdated(WorkspaceItem item)
        {
            ItemUpdated?.Invoke(item);
        }

        public Workspace()
        {
            am = new AssetsManager();
            RootItems = new List<WorkspaceItem>();
            ItemLookup = new Dictionary<string, WorkspaceItem>();

            // 加载 classdata.tpk（如果存在），以便后续按 Unity 版本加载类数据库
            string classDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "classdata.tpk");
            if (File.Exists(classDataPath))
            {
                am.LoadClassPackage(classDataPath);
            }
        }

        // 规范化路径作为 ItemLookup 的键：统一分隔符并小写
        private static string NormalizeKey(string path)
        {
            return path.Replace('\\', '/').ToLowerInvariant();
        }

        // 检查指定路径的文件是否已经加载（用于去重）
        public bool HasFile(string path)
        {
            string key = NormalizeKey(path);
            return ItemLookup.ContainsKey(key);
        }

        // 根据文件类型自动判定并加载任意文件，返回创建的 WorkspaceItem。
        // 无法识别的文件返回 null。
        public WorkspaceItem? LoadAnyFile(string path)
        {
            DetectedFileType detectedType = FileTypeDetector.DetectFileType(path);
            if (detectedType == DetectedFileType.BundleFile)
            {
                return LoadBundle(path);
            }
            else if (detectedType == DetectedFileType.AssetsFile)
            {
                return LoadAssets(path);
            }
            else if (path.EndsWith(".resS", StringComparison.OrdinalIgnoreCase) ||
                     path.EndsWith(".resource", StringComparison.OrdinalIgnoreCase))
            {
                return LoadResource(path);
            }

            return null;
        }

        // 加载一个 bundle 文件，并遍历其内部目录信息为每个子文件创建 WorkspaceItem。
        // bundle 内的被序列化子文件会注册到 am，便于后续读取资产。
        public WorkspaceItem LoadBundle(string path)
        {
            string key = NormalizeKey(path);
            if (ItemLookup.ContainsKey(key))
            {
                throw new DuplicateWorkspaceFileException(path);
            }

            // 解包到内存，保证内部子文件可以直接通过 SegmentStream 读取
            BundleFileInstance bunInst = am.LoadBundleFile(path, true);
            TryLoadClassDatabase(bunInst.file);

            WorkspaceItem item = new WorkspaceItem(bunInst);

            // 遍历 bundle 内部目录信息，为每个子文件创建 WorkspaceItem
            var dirInfs = bunInst.file.BlockAndDirInfo.DirectoryInfos;
            foreach (var dirInf in dirInfs)
            {
                string childName = dirInf.Name;
                string childKey = NormalizeKey(Path.Combine(bunInst.path, childName));
                if (ItemLookup.ContainsKey(childKey))
                {
                    throw new DuplicateWorkspaceFileException(childName, bunInst.path);
                }

                // 用 SegmentStream 包装 bundle 数据中该子文件的区间，
                // 参考现有 BundleWorkspace.PopulateFilesList 的做法
                long startAddress = dirInf.Offset;
                long length = dirInf.DecompressedSize;
                SegmentStream segStream = new SegmentStream(
                    bunInst.file.DataReader.BaseStream, startAddress, length);

                WorkspaceItem child;
                bool isSerialized = (dirInf.Flags & 0x04) != 0;
                if (isSerialized)
                {
                    // 注册到 am，便于后续读取资产；父 bundle 设为当前 bundle
                    string childVirtualPath = Path.Combine(bunInst.path, childName);
                    AssetsFileInstance fileInst = am.LoadAssetsFile(
                        segStream, childVirtualPath, false, bunInst);
                    TryLoadClassDatabase(fileInst.file);

                    child = new WorkspaceItem(childName, fileInst);
                }
                else
                {
                    WorkspaceItemType type = IsResourceName(childName)
                        ? WorkspaceItemType.ResourceFile
                        : WorkspaceItemType.OtherFile;
                    child = new WorkspaceItem(childName, segStream, type);
                }

                child.Parent = item;
                item.Children.Add(child);
                ItemLookup[childKey] = child;
            }

            RootItems.Add(item);
            ItemLookup[key] = item;
            ItemAdded?.Invoke(item);
            return item;
        }

        // 加载一个独立的 .assets 文件
        public WorkspaceItem LoadAssets(string path)
        {
            string key = NormalizeKey(path);
            if (ItemLookup.ContainsKey(key))
            {
                throw new DuplicateWorkspaceFileException(path);
            }

            AssetsFileInstance fileInst = am.LoadAssetsFile(path, true);
            TryLoadClassDatabase(fileInst.file);

            WorkspaceItem item = new WorkspaceItem(fileInst);
            RootItems.Add(item);
            ItemLookup[key] = item;
            ItemAdded?.Invoke(item);
            return item;
        }

        // 加载一个 resource 文件（.resS / .resource 等）
        public WorkspaceItem LoadResource(string path)
        {
            string key = NormalizeKey(path);
            if (ItemLookup.ContainsKey(key))
            {
                throw new DuplicateWorkspaceFileException(path);
            }

            string name = Path.GetFileName(path);
            FileStream fs = File.OpenRead(path);
            WorkspaceItem item = new WorkspaceItem(name, fs, WorkspaceItemType.ResourceFile);
            RootItems.Add(item);
            ItemLookup[key] = item;
            ItemAdded?.Invoke(item);
            return item;
        }

        // 移除指定路径的文件。如果是 bundle，会一并移除其所有子文件。
        public void RemoveFile(string path)
        {
            string key = NormalizeKey(path);
            if (!ItemLookup.TryGetValue(key, out WorkspaceItem? item))
            {
                return;
            }

            // 先处理子文件（bundle 内部）
            if (item.Children.Count > 0)
            {
                string basePath = item.BundleFileInstance != null
                    ? item.BundleFileInstance.path
                    : item.Name;
                List<WorkspaceItem> childrenCopy = new List<WorkspaceItem>(item.Children);
                foreach (WorkspaceItem child in childrenCopy)
                {
                    string childKey = NormalizeKey(Path.Combine(basePath, child.Name));
                    ItemLookup.Remove(childKey);
                    ItemRemoved?.Invoke(child);
                }
                item.Children.Clear();
            }

            // 卸载根级文件持有的资源
            if (item.ItemType == WorkspaceItemType.BundleFile && item.BundleFileInstance != null)
            {
                am.UnloadBundleFile(item.BundleFileInstance);
            }
            else if (item.ItemType == WorkspaceItemType.AssetsFile &&
                     item.AssetsFileInstance != null && item.Parent == null)
            {
                am.UnloadAssetsFile(item.AssetsFileInstance);
            }
            else if ((item.ItemType == WorkspaceItemType.ResourceFile ||
                      item.ItemType == WorkspaceItemType.OtherFile) && item.Stream != null)
            {
                item.Stream.Close();
            }

            // 从父项或根列表中移除
            if (item.Parent != null)
            {
                item.Parent.Children.Remove(item);
                item.Parent = null;
            }
            else
            {
                RootItems.Remove(item);
            }

            ItemLookup.Remove(key);
            ItemRemoved?.Invoke(item);
        }

        // 获取工作区中所有已加载的 AssetsFileInstance 列表
        // （包含根级 assets 文件和 bundle 内的被序列化子文件）
        public List<AssetsFileInstance> GetAssetsFileInstances()
        {
            List<AssetsFileInstance> result = new List<AssetsFileInstance>();
            foreach (WorkspaceItem item in RootItems)
            {
                if (item.ItemType == WorkspaceItemType.AssetsFile && item.AssetsFileInstance != null)
                {
                    result.Add(item.AssetsFileInstance);
                }
                else if (item.ItemType == WorkspaceItemType.BundleFile)
                {
                    foreach (WorkspaceItem child in item.Children)
                    {
                        if (child.ItemType == WorkspaceItemType.AssetsFile && child.AssetsFileInstance != null)
                        {
                            result.Add(child.AssetsFileInstance);
                        }
                    }
                }
            }
            return result;
        }

        // 如果尚未加载类数据库，则根据 assets 文件的 Unity 版本从 classdata.tpk 加载
        private void TryLoadClassDatabase(AssetsFile file)
        {
            if (am.ClassDatabase == null)
            {
                string version = file.Metadata.UnityVersion;
                if (!string.IsNullOrEmpty(version) && version != "0.0.0")
                {
                    am.LoadClassDatabaseFromPackage(version);
                }
            }
        }

        // 如果尚未加载类数据库，则根据 bundle 文件的引擎版本从 classdata.tpk 加载
        private void TryLoadClassDatabase(AssetBundleFile file)
        {
            if (am.ClassDatabase == null)
            {
                string version = file.Header.EngineVersion;
                if (!string.IsNullOrEmpty(version) && version != "0.0.0")
                {
                    am.LoadClassDatabaseFromPackage(version);
                }
            }
        }

        // 判断文件名是否属于 resource 文件
        private static bool IsResourceName(string name)
        {
            return name.EndsWith(".resS", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith(".resource", StringComparison.OrdinalIgnoreCase);
        }

        // ============ 资产字段读取 / PPtr 解析辅助方法 ============
        // 供插件预览器（如 MeshPlugin）按需加载资产基础字段、解析组件引用。
        // 设计参考旧 AssetWorkspace 的同名方法，但去掉 LoadedAssets 缓存，
        // 每次按需构造 AssetContainer，保持新工作区无状态查询语义。

        /// <summary>
        /// 获取资产的基础值字段。若容器尚未加载字段，则用 <see cref="am"/> 按需解码。
        /// 非 MonoBehaviour 资产无需 RefTypeManager。
        /// </summary>
        public AssetTypeValueField? GetBaseField(AssetContainer cont)
        {
            if (cont == null)
                return null;

            if (cont.HasValueField)
                return cont.BaseValueField;

            try
            {
                AssetTypeTemplateField tempField = am.GetTemplateBaseField(
                    cont.FileInstance, cont.FileReader, cont.FilePosition,
                    cont.ClassId, cont.MonoId, AssetReadFlags.None);
                return tempField.MakeValue(cont.FileReader, cont.FilePosition);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 按 PPtr（m_FileID + m_PathID）解析目标资产容器。
        /// m_FileID 为 0 表示同一文件，否则为 1-based 依赖索引。
        /// 返回的容器仅含信息（BaseValueField 为空），需再调用 <see cref="GetBaseField"/> 解码。
        /// </summary>
        public AssetContainer? GetAssetContainer(AssetsFileInstance fileInst, AssetTypeValueField pptrField)
        {
            int fileId = pptrField["m_FileID"].AsInt;
            long pathId = pptrField["m_PathID"].AsLong;

            AssetsFileInstance targetInst = fileInst;
            if (fileId != 0)
            {
                targetInst = fileInst.GetDependency(am, fileId - 1);
                if (targetInst == null)
                    return null;
            }

            AssetFileInfo info = targetInst.file.GetAssetInfo(pathId);
            if (info == null)
                return null;

            return new AssetContainer(info, targetInst);
        }
    }
}
