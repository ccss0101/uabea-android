using Avalonia;
using Avalonia.Controls;
using UABEAvalonia.ViewModels;

namespace UABEAvalonia;

/// <summary>
/// Dock 主窗口的 code-behind。
/// 与现有 Forms/MainWindow.axaml 并存，作为新版多文档界面入口。
/// 通过 App.axaml.cs 在启动时选择创建 MainWindow 或 DockMainWindow。
/// </summary>
public partial class DockMainWindow : Window
{
    public DockMainWindow()
    {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
    }

    /// <summary>
    /// 注入 MainViewModel。供 App.axaml.cs 在创建窗口后调用。
    /// </summary>
    public void SetViewModel(MainViewModel viewModel)
    {
        DataContext = viewModel;
    }
}
