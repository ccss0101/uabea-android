using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Dock.Model.Mvvm.Controls;
using System.Collections.ObjectModel;

namespace UABEAvalonia.ViewModels.Tools;

/// <summary>
/// Inspector 工具窗。监听 AssetsSelectedMessage，显示当前选中资产的简要信息。
/// 后续阶段会扩展为完整的属性编辑面板。
/// </summary>
public partial class InspectorToolViewModel : Tool
{
    private const string ToolTitle = "Inspector";

    /// <summary>
    /// 当前选中的资产集合，绑定到 ItemsControl 显示。
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<AssetContainer> _activeAssets = new();

    public InspectorToolViewModel()
    {
        Id = ToolTitle.Replace(" ", "");
        Title = ToolTitle;

        // 监听资产选中消息（来自资产文档）
        WeakReferenceMessenger.Default.Register<AssetsSelectedMessage>(this, OnAssetsSelected);
        // 监听工作区关闭消息，清空列表
        WeakReferenceMessenger.Default.Register<WorkspaceClosingMessage>(this, OnWorkspaceClosing);
    }

    private void OnAssetsSelected(object recipient, AssetsSelectedMessage message)
    {
        ActiveAssets.Clear();
        foreach (AssetContainer asset in message.Value)
        {
            ActiveAssets.Add(asset);
        }
    }

    private void OnWorkspaceClosing(object recipient, WorkspaceClosingMessage message)
    {
        ActiveAssets.Clear();
    }
}
