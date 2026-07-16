using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Dock.Model.Mvvm.Controls;
using System.Collections.ObjectModel;

namespace UABEAvalonia.ViewModels.Tools;

/// <summary>
/// 工作区浏览器工具窗。显示 Workspace.RootItems 的树形结构，
/// 选中节点后通过 SelectedWorkspaceItemChangedMessage 通知 MainViewModel 打开文档。
/// 基类 Dock.Model.Mvvm.Controls.Tool 已继承 ObservableObject，可直接使用 [ObservableProperty]。
/// </summary>
public partial class WorkspaceExplorerToolViewModel : Tool
{
    private const string ToolTitle = "Workspace Explorer";

    /// <summary>
    /// 绑定到 TreeView 的根级工作区项集合。
    /// 直接引用 Workspace.RootItems，避免数据重复。
    /// </summary>
    public ObservableCollection<WorkspaceItem> RootItems { get; }

    /// <summary>
    /// 当前选中的工作区项。设置后通过 Messenger 通知主视图模型。
    /// </summary>
    [ObservableProperty]
    private WorkspaceItem? _selectedItem;

    public WorkspaceExplorerToolViewModel(Workspace workspace)
    {
        RootItems = new ObservableCollection<WorkspaceItem>(workspace.RootItems);

        Id = ToolTitle.Replace(" ", "");
        Title = ToolTitle;

        // 监听 workspace 项变更事件，自动同步 RootItems 集合
        workspace.ItemAdded += OnWorkspaceItemAdded;
        workspace.ItemRemoved += OnWorkspaceItemRemoved;
    }

    partial void OnSelectedItemChanged(WorkspaceItem? value)
    {
        // 选中项变化时通知 MainViewModel 打开对应文档
        WeakReferenceMessenger.Default.Send(new SelectedWorkspaceItemChangedMessage(value));
    }

    private void OnWorkspaceItemAdded(WorkspaceItem item)
    {
        // 仅根级项（Parent == null）需要加到列表中
        if (item.Parent == null)
        {
            RootItems.Add(item);
        }
    }

    private void OnWorkspaceItemRemoved(WorkspaceItem item)
    {
        if (item.Parent == null)
        {
            RootItems.Remove(item);
        }
    }
}
