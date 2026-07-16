using Avalonia.Controls;
using UABEAvalonia.ViewModels;

namespace UABEAvalonia.Views;

/// <summary>
/// Settings 面板的 code-behind。
/// 当 SettingsViewModel.CloseRequested 触发时关闭所在窗口。
/// </summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is SettingsViewModel svm)
        {
            svm.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, System.EventArgs e)
    {
        // 找到所在 Window 并关闭
        if (this.VisualRoot is Window window)
        {
            window.Close();
        }
    }
}
