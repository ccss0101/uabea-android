using CommunityToolkit.Mvvm.Input;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using System;
using System.Collections.Generic;
using UABEAvalonia.Plugins;
using UABEAvalonia.ViewModels.Documents;
using UABEAvalonia.ViewModels.Tools;

namespace UABEAvalonia.ViewModels;

/// <summary>
/// Dock 布局工厂。参考 UABEANext4 MainDockFactory 的设计：
///   - CreateLayout() 构建三栏布局树（左 WorkspaceExplorer + 中 DocumentDock + 右 Inspector）
///   - InitLayout() 注册 ContextLocator / DockableLocator / HostWindowLocator
/// 注意：本项目使用 Dock.Avalonia 11.0.0.7（兼容 Avalonia 11.0.1），
/// 工厂基类为 Dock.Model.Mvvm.Factory，IRootDock 位于 Dock.Model.Controls。
/// </summary>
public class MainDockFactory : Factory
{
    /// <summary>
    /// 主 ProportionalDock，外部需要时可用于动态添加 dockable。
    /// </summary>
    public ProportionalDock? MainPane { get; private set; }

    private IRootDock? _rootDock;
    private IDocumentDock? _fileDocumentDock;
    private WorkspaceExplorerToolViewModel? _workspaceExplorerTool;
    private HierarchyToolViewModel? _hierarchyTool;
    private InspectorToolViewModel? _inspectorTool;
    private PreviewerToolViewModel? _previewerTool;

    private readonly Workspace _workspace;
    private readonly PluginLoader? _pluginLoader;

    public MainDockFactory(Workspace workspace)
        : this(workspace, null)
    {
    }

    /// <summary>
    /// 注入新版插件加载器，由本工厂转发给 PreviewerToolViewModel。
    /// 为 null 时 PreviewerTool 回退到内置预览器列表。
    /// </summary>
    public MainDockFactory(Workspace workspace, PluginLoader? pluginLoader)
    {
        _workspace = workspace;
        _pluginLoader = pluginLoader;
    }

    public override IRootDock CreateLayout()
    {
        // 创建工具窗 ViewModel
        _workspaceExplorerTool = new WorkspaceExplorerToolViewModel(_workspace);
        _hierarchyTool = new HierarchyToolViewModel(_workspace);
        _inspectorTool = new InspectorToolViewModel();
        _previewerTool = new PreviewerToolViewModel(_workspace, _pluginLoader);

        // DocumentDock 初始内容为一个不可关闭的占位文档
        var blankDocument = new BlankDocumentViewModel();
        _fileDocumentDock = new DocumentDock
        {
            Id = "Files",
            Title = "Files",
            ActiveDockable = blankDocument,
            VisibleDockables = CreateList<IDockable>(blankDocument),
            CanCreateDocument = true,
            CreateDocument = new RelayCommand(AddNewBlankDocument),
            IsCollapsable = false,
            Proportion = double.NaN
        };

        // 左侧工具窗（Workspace Explorer）
        var explorerDock = new ToolDock
        {
            Id = "ExplorerDock",
            ActiveDockable = _workspaceExplorerTool,
            VisibleDockables = CreateList<IDockable>(_workspaceExplorerTool),
            Alignment = Alignment.Left,
            GripMode = GripMode.Visible,
            Proportion = 0.5
        };

        // 左侧工具窗（Hierarchy，Unity 场景层级浏览器）
        var hierarchyDock = new ToolDock
        {
            Id = "HierarchyDock",
            ActiveDockable = _hierarchyTool,
            VisibleDockables = CreateList<IDockable>(_hierarchyTool),
            Alignment = Alignment.Left,
            GripMode = GripMode.Visible,
            Proportion = 0.5
        };

        // 左侧工具区：Workspace Explorer 在上、Hierarchy 在下，垂直堆叠
        var leftToolsPane = new ProportionalDock
        {
            Id = "LeftToolsPane",
            Orientation = Orientation.Vertical,
            IsCollapsable = false,
            VisibleDockables = CreateList<IDockable>(
                explorerDock,
                new ProportionalDockSplitter(),
                hierarchyDock)
        };

        // 右侧工具窗（Inspector）
        var inspectorDock = new ToolDock
        {
            Id = "InspectorDock",
            ActiveDockable = _inspectorTool,
            VisibleDockables = CreateList<IDockable>(_inspectorTool),
            Alignment = Alignment.Right,
            GripMode = GripMode.Visible,
            Proportion = 0.5
        };

        // 右侧工具窗（Previewer）
        var previewerDock = new ToolDock
        {
            Id = "PreviewerDock",
            ActiveDockable = _previewerTool,
            VisibleDockables = CreateList<IDockable>(_previewerTool),
            Alignment = Alignment.Right,
            GripMode = GripMode.Visible,
            Proportion = 0.5
        };

        // 右侧工具区：Inspector 在上、Previewer 在下，垂直堆叠
        var rightToolsPane = new ProportionalDock
        {
            Id = "RightToolsPane",
            Orientation = Orientation.Vertical,
            IsCollapsable = false,
            VisibleDockables = CreateList<IDockable>(
                inspectorDock,
                new ProportionalDockSplitter(),
                previewerDock)
        };

        // 主横向比例布局：[LeftTools(Explorer/Hierarchy)] | [Files] | [RightTools(Inspector/Previewer)]
        MainPane = new ProportionalDock
        {
            Id = "MainPane",
            Orientation = Orientation.Horizontal,
            IsCollapsable = false,
            VisibleDockables = CreateList<IDockable>(
                leftToolsPane,
                new ProportionalDockSplitter(),
                _fileDocumentDock,
                new ProportionalDockSplitter(),
                rightToolsPane)
        };

        _rootDock = CreateRootDock();
        _rootDock.Id = "Root";
        _rootDock.Title = "Default";
        _rootDock.IsCollapsable = false;
        _rootDock.VisibleDockables = CreateList<IDockable>(MainPane);
        _rootDock.ActiveDockable = MainPane;
        _rootDock.DefaultDockable = MainPane;

        return _rootDock;
    }

    /// <summary>
    /// DocumentDock "+" 按钮回调：新建一个空白文档。
    /// </summary>
    private void AddNewBlankDocument()
    {
        if (_fileDocumentDock is null)
        {
            return;
        }

        var newDoc = new BlankDocumentViewModel();
        AddDockable(_fileDocumentDock, newDoc);
        SetActiveDockable(newDoc);
    }

    public override void InitLayout(IDockable layout)
    {
        DockableLocator = new Dictionary<string, Func<IDockable?>>
        {
            ["Root"] = () => _rootDock,
            ["Files"] = () => _fileDocumentDock,
            ["WorkspaceExplorer"] = () => _workspaceExplorerTool,
            ["Hierarchy"] = () => _hierarchyTool,
            ["Inspector"] = () => _inspectorTool,
            ["Previewer"] = () => _previewerTool,
        };

        // HostWindow 用于支持 dockable 浮动到独立窗口
        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = () => new HostWindow()
        };

        base.InitLayout(layout);
    }
}
