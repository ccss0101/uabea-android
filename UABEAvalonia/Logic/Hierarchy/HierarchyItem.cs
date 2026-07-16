using AssetsTools.NET;
using AssetsTools.NET.Extra;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;

namespace UABEAvalonia.Logic.Hierarchy;

/// <summary>
/// Hierarchy 树节点，对应一个 Unity GameObject。
/// 通过关联的 Transform 资产推导父子关系，AssetContainer 指向 GameObject 本身。
/// 继承 ObservableObject 以支持 IsExpanded / IsSelected 双向绑定。
/// </summary>
public partial class HierarchyItem : ObservableObject
{
    /// <summary>节点显示名称（GameObject 的 m_Name）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>关联的 GameObject 资产容器；根包装节点可能为 null。</summary>
    public AssetContainer? AssetContainer { get; set; }

    /// <summary>子节点集合，绑定到 TreeView 的 ItemTemplate.ItemsSource。</summary>
    public ObservableCollection<HierarchyItem> Children { get; } = new();

    /// <summary>父节点；根节点为 null。</summary>
    public HierarchyItem? Parent { get; set; }

    /// <summary>是否展开，双向绑定到 TreeViewItem.IsExpanded。</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>是否选中，双向绑定到 TreeViewItem.IsSelected。</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// 为指定 assets 文件构建 GameObject 层级树。
    /// 在后台线程调用，通过 <paramref name="ct"/> 支持切换文件时取消。
    /// 解析逻辑参考 GameObjectViewWindow：遍历 Transform/RectTransform 资产，
    /// 通过 m_Father 判定根节点，通过 m_GameObject PPtr 关联 GameObject。
    /// </summary>
    public static List<HierarchyItem> CreateRootItems(
        AssetsFileInstance fileInst, Workspace workspace, CancellationToken ct)
    {
        // 第一遍：收集所有 Transform/RectTransform，建立 PathId -> 节点信息
        var transformInfos = new Dictionary<long, TransformInfo>();

        foreach (AssetFileInfo info in fileInst.file.AssetInfos)
        {
            ct.ThrowIfCancellationRequested();

            // 仅处理 Transform(4) 与 RectTransform(21)
            bool isTransform = info.TypeId == (uint)AssetClassID.Transform
                            || info.TypeId == (uint)AssetClassID.RectTransform;
            if (!isTransform)
                continue;

            var cont = new AssetContainer(info, fileInst);
            var tfmBf = workspace.GetBaseField(cont);
            if (tfmBf == null)
                continue;

            long pathId = info.PathId;
            // m_Father 为 PPtr，m_PathID == 0 表示根 Transform
            long fatherPathId = tfmBf["m_Father"]["m_PathID"].AsLong;

            // 通过 m_GameObject PPtr 解析关联的 GameObject 容器
            AssetContainer? goCont = workspace.GetAssetContainer(fileInst, tfmBf["m_GameObject"]);
            string name = "[missing gameobject]";
            if (goCont != null)
            {
                var goBf = workspace.GetBaseField(goCont);
                if (goBf != null)
                    name = goBf["m_Name"].AsString;
            }

            transformInfos[pathId] = new TransformInfo
            {
                PathId = pathId,
                FatherPathId = fatherPathId,
                GameObjectContainer = goCont,
                Name = name
            };
        }

        // 第二遍：创建 HierarchyItem 并按 m_Father 建立父子关系
        var pathIdToItem = new Dictionary<long, HierarchyItem>();
        foreach (var kv in transformInfos)
        {
            ct.ThrowIfCancellationRequested();
            var inf = kv.Value;
            pathIdToItem[kv.Key] = new HierarchyItem
            {
                Name = inf.Name,
                AssetContainer = inf.GameObjectContainer
            };
        }

        var rootItems = new List<HierarchyItem>();
        foreach (var kv in transformInfos)
        {
            ct.ThrowIfCancellationRequested();
            var inf = kv.Value;
            var item = pathIdToItem[kv.Key];

            if (inf.FatherPathId != 0 &&
                pathIdToItem.TryGetValue(inf.FatherPathId, out HierarchyItem? parentItem))
            {
                // 挂到父 Transform 节点下
                parentItem.Children.Add(item);
                item.Parent = parentItem;
            }
            else
            {
                // m_Father.m_PathID == 0 为根；父 Transform 不在本文件中亦视为根
                rootItems.Add(item);
            }
        }

        return rootItems;
    }

    // Transform 资产的解析中间结构
    private class TransformInfo
    {
        public long PathId { get; set; }
        public long FatherPathId { get; set; }
        public AssetContainer? GameObjectContainer { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
