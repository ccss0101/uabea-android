using Dock.Model.Mvvm.Controls;

namespace UABEAvalonia.ViewModels.Documents;

/// <summary>
/// 资产文档，对应一个被打开的 WorkspaceItem（assets / bundle）。
/// P1 阶段仅持有 WorkspaceItem 引用并提供占位 Title，
/// 真正的资产列表展示（DataGrid）由后续阶段接入。
/// 基类 Dock.Model.Mvvm.Controls.Document 已继承 ObservableObject。
/// </summary>
public partial class AssetDocumentViewModel : Document
{
    /// <summary>
    /// 此文档对应的工作区项（根级 assets / bundle）。
    /// 后续阶段会用它来读取 AssetContainer 列表。
    /// </summary>
    public WorkspaceItem WorkspaceItem { get; }

    public AssetDocumentViewModel(WorkspaceItem wsItem)
    {
        WorkspaceItem = wsItem;

        // Id 需要保持唯一，便于 DockableLocator 查找；
        // 不同 bundle 内可能有同名子文件，故 Id 带上 Parent.Name 前缀以避免冲突。
        string parentId = wsItem.Parent?.Name ?? "root";
        Id = $"{parentId}/{wsItem.Name}";
        Title = wsItem.ToString();
    }
}
