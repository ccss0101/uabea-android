using AssetsTools.NET.Extra;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Dock.Model.Mvvm.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UABEAvalonia.Logic.Hierarchy;

namespace UABEAvalonia.ViewModels.Tools;

/// <summary>
/// Hierarchy 工具窗。以异步增量方式加载选中 assets 文件的 GameObject 层级树。
/// 加载在后台线程进行，DispatcherTimer 每秒批量把待添加节点刷到 RootItems，
/// 避免大文件卡 UI；切换文件时通过 CancellationTokenSource 取消上一次加载。
/// 选中 GameObject 时，收集其所有 component 一并发送 AssetsSelectedMessage。
/// 基类 Dock.Model.Mvvm.Controls.Tool 已继承 ObservableObject，可直接使用 [ObservableProperty]。
/// </summary>
public partial class HierarchyToolViewModel : Tool
{
    private const string ToolTitle = "Hierarchy";

    /// <summary>多 bundle 工作区，用于解析 GameObject / component。</summary>
    public Workspace Workspace { get; }

    /// <summary>TreeView 根级节点集合。</summary>
    [ObservableProperty]
    private ObservableCollection<HierarchyItem> _rootItems = new();

    /// <summary>当前选中节点，双向绑定到 TreeView.SelectedItem。</summary>
    [ObservableProperty]
    private HierarchyItem? _selectedItem;

    /// <summary>是否按字母排序根节点。</summary>
    [ObservableProperty]
    private bool _sortAlphabetically;

    /// <summary>搜索文本，变化时递归查找并选中第一个名称匹配的节点。</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>是否正在加载（用于禁用排序复选框等）。</summary>
    [ObservableProperty]
    private bool _isLoadingNewItems;

    // 切换文件时取消上一次加载
    private CancellationTokenSource? _loadCts;
    // 每秒批量把 _pendingRootItems 移入 RootItems
    private DispatcherTimer? _loadTimer;
    // 待添加节点缓冲区（后台线程写入，UI 线程定时读取，访问需加锁）
    private readonly List<HierarchyItem> _pendingRootItems = new();

    /// <summary>设计器用构造函数。</summary>
    public HierarchyToolViewModel()
    {
        Workspace = new Workspace();
        Id = ToolTitle;
        Title = ToolTitle;
    }

    public HierarchyToolViewModel(Workspace workspace)
    {
        Workspace = workspace;
        Id = ToolTitle;
        Title = ToolTitle;

        // 选中工作区文件时加载其 GameObject 层级
        WeakReferenceMessenger.Default.Register<SelectedWorkspaceItemChangedMessage>(this, OnSelectedWorkspaceItemChanged);
        // 请求跳转到指定 GameObject（来自资产文档 / 预览器等）
        WeakReferenceMessenger.Default.Register<RequestSceneViewMessage>(this, OnRequestSceneView);
        // 工作区关闭时清空
        WeakReferenceMessenger.Default.Register<WorkspaceClosingMessage>(this, OnWorkspaceClosing);
    }

    // 选中工作区文件时加载其 GameObject 层级（仅处理 AssetsFile 类型）
    private void OnSelectedWorkspaceItemChanged(object recipient, SelectedWorkspaceItemChangedMessage message)
    {
        if (message.Value is not WorkspaceItem wsItem)
            return;
        if (wsItem.ItemType != WorkspaceItemType.AssetsFile || wsItem.AssetsFileInstance == null)
            return;
        LoadFile(wsItem.AssetsFileInstance);
    }

    private void OnRequestSceneView(object recipient, RequestSceneViewMessage message)
    {
        SelectItem(message.Value);
    }

    private void OnWorkspaceClosing(object recipient, WorkspaceClosingMessage message)
    {
        _loadCts?.Cancel();
        _loadTimer?.Stop();
        IsLoadingNewItems = false;
        lock (_pendingRootItems)
        {
            _pendingRootItems.Clear();
        }
        RootItems.Clear();
        SelectedItem = null;
    }

    /// <summary>
    /// 异步增量加载指定 assets 文件的 GameObject 层级树。
    /// 取消上一次加载，后台线程构建树并分批写入缓冲区，
    /// DispatcherTimer 每秒把缓冲区批量刷到 RootItems。
    /// </summary>
    public async void LoadFile(AssetsFileInstance fileInst)
    {
        // 取消上一次加载并停止其定时器
        if (_loadCts != null)
        {
            _loadCts.Cancel();
        }
        _loadTimer?.Stop();

        _loadCts = new CancellationTokenSource();
        CancellationToken ct = _loadCts.Token;

        RootItems.Clear();
        SelectedItem = null;
        lock (_pendingRootItems)
        {
            _pendingRootItems.Clear();
        }
        IsLoadingNewItems = true;

        // 启动定时器：每秒把待添加节点批量移入 RootItems（UI 线程）
        _loadTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            (s, e) => AddPendingRootItems());
        _loadTimer.Start();

        // 后台线程解析 GameObject 树，分批加入缓冲区
        var loadTask = Task.Run(() =>
        {
            List<HierarchyItem> items = HierarchyItem.CreateRootItems(fileInst, Workspace, ct);
            // 分批加入待添加缓冲区，配合 DispatcherTimer 实现增量刷新
            const int batchSize = 100;
            for (int i = 0; i < items.Count; i += batchSize)
            {
                ct.ThrowIfCancellationRequested();
                int end = Math.Min(i + batchSize, items.Count);
                lock (_pendingRootItems)
                {
                    for (int j = i; j < end; j++)
                        _pendingRootItems.Add(items[j]);
                }
            }
        }, ct);

        try
        {
            await loadTask;
        }
        catch (OperationCanceledException)
        {
            // 切换文件时取消，忽略
        }

        // 仅当本次加载未被更晚的 LoadFile 取消时才收尾
        if (!ct.IsCancellationRequested)
        {
            AddPendingRootItems();
            _loadTimer?.Stop();
            if (SortAlphabetically)
                SortRootItems();
            IsLoadingNewItems = false;
        }
    }

    // 把缓冲区中的待添加节点批量移入 RootItems（UI 线程调用）
    private void AddPendingRootItems()
    {
        lock (_pendingRootItems)
        {
            if (_pendingRootItems.Count == 0)
                return;
            foreach (var item in _pendingRootItems)
                RootItems.Add(item);
            _pendingRootItems.Clear();
        }
    }

    // 排序开关变化时，对已加载的根节点重新排序
    partial void OnSortAlphabeticallyChanged(bool value)
    {
        SortRootItems();
    }

    private void SortRootItems()
    {
        if (!SortAlphabetically || RootItems.Count == 0)
            return;
        var sorted = RootItems.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
        RootItems.Clear();
        foreach (var item in sorted)
            RootItems.Add(item);
    }

    // 选中节点变化时，收集 GameObject + 其所有 component，发送 AssetsSelectedMessage
    partial void OnSelectedItemChanged(HierarchyItem? value)
    {
        if (value?.AssetContainer == null)
            return;
        NotifySelectionChanged(value.AssetContainer);
    }

    /// <summary>
    /// 收集指定 GameObject 及其所有 component（通过 m_Component.Array），
    /// 作为 List&lt;AssetContainer&gt; 发送 AssetsSelectedMessage，供 Inspector / Previewer 显示。
    /// </summary>
    public void NotifySelectionChanged(AssetContainer gameObjectCont)
    {
        var allAssets = new List<AssetContainer> { gameObjectCont };

        var goBf = Workspace.GetBaseField(gameObjectCont);
        if (goBf != null)
        {
            var components = goBf["m_Component"]["Array"];
            foreach (var data in components)
            {
                // 每个 component 条目的最后一个子字段是 PPtr<Component>
                if (data.Children.Count == 0)
                    continue;
                var component = data[data.Children.Count - 1];
                var componentCont = Workspace.GetAssetContainer(gameObjectCont.FileInstance, component);
                if (componentCont != null)
                    allAssets.Add(componentCont);
            }
        }

        WeakReferenceMessenger.Default.Send(new AssetsSelectedMessage(allAssets));
    }

    // 请求跳转到指定 GameObject：在 RootItems 中递归搜索并展开选中
    private void SelectItem(AssetContainer asset)
    {
        foreach (var root in RootItems)
        {
            if (SearchForItemAndSelectRecursive(asset, root))
            {
                root.IsExpanded = true;
                break;
            }
        }
    }

    private bool SearchForItemAndSelectRecursive(AssetContainer asset, HierarchyItem item)
    {
        if (item.AssetContainer != null &&
            item.AssetContainer.FileInstance == asset.FileInstance &&
            item.AssetContainer.PathId == asset.PathId)
        {
            SelectedItem = item;
            return true;
        }

        foreach (var child in item.Children)
        {
            if (SearchForItemAndSelectRecursive(asset, child))
            {
                item.IsExpanded = true;
                return true;
            }
        }
        return false;
    }

    // 搜索文本变化时，递归查找第一个名称匹配的节点并展开选中
    partial void OnSearchTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        SearchByText(value);
    }

    private void SearchByText(string searchText)
    {
        foreach (var root in RootItems)
        {
            if (SearchByTextRecursive(searchText, root))
            {
                root.IsExpanded = true;
                break;
            }
        }
    }

    private bool SearchByTextRecursive(string searchText, HierarchyItem item)
    {
        if (!string.IsNullOrEmpty(item.Name) &&
            item.Name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            SelectedItem = item;
            return true;
        }
        foreach (var child in item.Children)
        {
            if (SearchByTextRecursive(searchText, child))
            {
                item.IsExpanded = true;
                return true;
            }
        }
        return false;
    }
}
