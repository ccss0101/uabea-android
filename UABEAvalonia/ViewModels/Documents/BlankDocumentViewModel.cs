using Dock.Model.Mvvm.Controls;

namespace UABEAvalonia.ViewModels.Documents;

/// <summary>
/// 空白文档占位，作为 DocumentDock 的初始内容。
/// 基类 Dock.Model.Mvvm.Controls.Document 已继承 ObservableObject，可直接使用 [ObservableProperty]。
/// </summary>
public partial class BlankDocumentViewModel : Document
{
    private const string DocumentTitle = "New Tab";

    public BlankDocumentViewModel()
    {
        Id = "Blank";
        Title = DocumentTitle;
        // 占位文档不可关闭、不可浮动，保证文档区始终至少有一个标签
        CanClose = false;
        CanFloat = false;
    }
}
