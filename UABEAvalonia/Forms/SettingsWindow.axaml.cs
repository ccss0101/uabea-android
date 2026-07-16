using Avalonia;
using Avalonia.Controls;
using UABEAvalonia.ViewModels;

namespace UABEAvalonia.Forms;

/// <summary>
/// Settings 窗口的 code-behind。
/// 由 MainViewModel.FileSettingsCommand 或 MainWindow 菜单打开。
/// 创建时自动构造 SettingsViewModel 作为 DataContext。
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
        DataContext = new SettingsViewModel();
    }
}
