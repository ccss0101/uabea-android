using Avalonia.Controls;
using Avalonia.Input;
using UABEAvalonia.Logic.Hierarchy;
using UABEAvalonia.ViewModels.Tools;

namespace UABEAvalonia.Views.Tools;

/// <summary>
/// Hierarchy 工具窗视图。双击节点切换展开/折叠。
/// </summary>
public partial class HierarchyToolView : UserControl
{
    public HierarchyToolView()
    {
        InitializeComponent();
    }

    // 双击切换选中节点的展开/折叠状态
    private void HierarchyTreeView_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is HierarchyToolViewModel vm && vm.SelectedItem is HierarchyItem item)
        {
            item.IsExpanded = !item.IsExpanded;
        }
    }
}
