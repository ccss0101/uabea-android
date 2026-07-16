using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Dock.Model.Controls;
using System;
using System.IO;
using System.Threading.Tasks;
using UABEAvalonia.Plugins;
using UABEAvalonia.ViewModels.Documents;

namespace UABEAvalonia.ViewModels;

/// <summary>
/// Dock 主窗口的根 ViewModel。持有 Workspace、DockFactory 与 Layout(IRootDock)。
/// 参考 UABEANext4 MainViewModel 的设计：
///   - 构造时调用 factory.CreateLayout + factory.InitLayout 完成 Dock 树初始化
///   - 监听 SelectedWorkspaceItemChangedMessage，自动打开对应资产文档
///   - 提供 FileOpen / FileCloseAll 等命令供菜单绑定
/// 注意：本类继承 ViewModelBase（已位于 UABEA.Core，包含 ObservableObject），
/// 因此可直接使用 [ObservableProperty] / [RelayCommand]。
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private const string DefaultWindowTitle = "UABEA - Dock";

    /// <summary>多 bundle 工作区</summary>
    public Workspace Workspace { get; }

    /// <summary>Dock 工厂，外部需要时可用于动态操作 dockable</summary>
    public MainDockFactory Factory { get; }

    /// <summary>Dock 根布局，绑定到 DockControl.Layout</summary>
    [ObservableProperty]
    private IRootDock? _layout;

    /// <summary>窗口标题，加载文件后会附加版本信息</summary>
    [ObservableProperty]
    private string _windowTitle = DefaultWindowTitle;

    public MainViewModel()
        : this(null)
    {
    }

    /// <summary>
    /// 注入新版插件加载器（由 App 启动时加载 plugins 目录填充）。
    /// 经 <see cref="MainDockFactory"/> 转发到 PreviewerToolViewModel。
    /// pluginLoader 为 null 时回退到内置预览器列表（向后兼容）。
    /// </summary>
    public MainViewModel(PluginLoader? pluginLoader)
    {
        Workspace = new Workspace();
        Factory = new MainDockFactory(Workspace, pluginLoader);
        Layout = Factory.CreateLayout();
        if (Layout is not null)
        {
            Factory.InitLayout(Layout);
        }

        // 监听工作区选中项变化（来自 WorkspaceExplorer），自动打开文档
        WeakReferenceMessenger.Default.Register<SelectedWorkspaceItemChangedMessage>(this, OnSelectedItemChanged);
    }

    /// <summary>
    /// File > Open 菜单命令：弹出文件选择对话框，加载到工作区。
    /// 通过 IDialogService 抽象，便于 Android 端替换实现。
    /// </summary>
    [RelayCommand]
    private async Task FileOpen()
    {
        var dialogService = App.Services.GetService(typeof(IDialogService)) as IDialogService;
        if (dialogService is null)
        {
            return;
        }

        var files = await dialogService.ShowOpenFileDialog(
            title: "Open bundle or assets file",
            multiSelect: true);

        if (files is null || files.Count == 0)
        {
            return;
        }

        foreach (string path in files)
        {
            if (!File.Exists(path))
            {
                continue;
            }
            try
            {
                Workspace.LoadAnyFile(path);
            }
            catch (Exception ex)
            {
                await dialogService.ShowMessageDialog("Load error", $"{Path.GetFileName(path)}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// File > Close All 菜单命令：清空工作区并关闭所有文档。
    /// </summary>
    [RelayCommand]
    private void FileCloseAll()
    {
        // 通知各组件（Inspector、Asset 文档等）清空状态
        WeakReferenceMessenger.Default.Send(new WorkspaceClosingMessage(true));

        // 注意：当前 P1 阶段不实现完整的 RemoveFile 循环（依赖保存逻辑），
        // 仅清空 RootItems。后续阶段接入 Workspace.CloseAll 后再补全。
        var rootCopy = Workspace.RootItems.ToArray();
        foreach (var item in rootCopy)
        {
            Workspace.RemoveFile(item.Name);
        }

        WindowTitle = DefaultWindowTitle;
    }

    /// <summary>
    /// File > Settings 菜单命令：打开 Settings 窗口。
    /// SettingsViewModel 内部直接读写 ConfigurationManager.Settings，
    /// 关闭时会触发即时保存。
    /// </summary>
    [RelayCommand]
    private void FileSettings()
    {
        var window = new Forms.SettingsWindow();
        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            window.ShowDialog(desktop.MainWindow);
        }
        else
        {
            window.Show();
        }
    }

    /// <summary>
    /// File > Exit 菜单命令：关闭主窗口。
    /// </summary>
    [RelayCommand]
    private void FileExit()
    {
        WeakReferenceMessenger.Default.Send(new WorkspaceClosingMessage(true));
        // 关闭窗口由 DockMainWindow 处理（通过 Closing 事件或主菜单命令绑定）
    }

    /// <summary>
    /// 收到 WorkspaceExplorer 选中项变化消息时尝试打开资产文档。
    /// 仅处理根级 AssetsFile；其它类型（Bundle / Resource）暂不打开文档。
    /// </summary>
    private void OnSelectedItemChanged(object recipient, SelectedWorkspaceItemChangedMessage message)
    {
        _ = OpenAssetDocumentFor(message.Value);
    }

    private Task OpenAssetDocumentFor(object? selectedItem)
    {
        if (Layout is null || Factory is null)
        {
            return Task.CompletedTask;
        }

        if (selectedItem is not WorkspaceItem wsItem)
        {
            return Task.CompletedTask;
        }

        // 仅 AssetsFile 类型的根级项才有意义打开资产文档
        if (wsItem.ItemType != WorkspaceItemType.AssetsFile)
        {
            return Task.CompletedTask;
        }

        var files = Factory.GetDockable<IDocumentDock>("Files");
        if (files is null)
        {
            return Task.CompletedTask;
        }

        var document = new AssetDocumentViewModel(wsItem);
        Factory.AddDockable(files, document);
        Factory.SetActiveDockable(document);
        Factory.SetFocusedDockable(files, document);

        return Task.CompletedTask;
    }
}
