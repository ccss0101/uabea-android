using Avalonia.Controls;
using UABEAvalonia.ViewModels;

namespace UABEAvalonia.Views;

/// <summary>
/// Dock 主视图（UserControl）的 code-behind。
/// 仅做 InitializeComponent，所有逻辑都在 MainViewModel 中。
/// </summary>
public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }
}
