using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace UABEAvalonia.ViewModels;

/// <summary>
/// 设置面板 ViewModel：通过 <see cref="ConfigurationManager.GetConfigurationItems"/>
/// 反射获取所有带 [ConfigTitle] 的配置项，绑定到 SettingsView 上动态渲染。
/// 配置项的 Value 直接通过反射读写 ConfigurationManager.Settings，
/// 写入会自动触发 ConfigurationManager 的 500ms 防抖保存。
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    /// <summary>所有可配置项（布尔/整数/枚举）</summary>
    public ObservableCollection<ConfigurationItemBase> Items { get; }

    public SettingsViewModel()
    {
        Items = new ObservableCollection<ConfigurationItemBase>(ConfigurationManager.GetConfigurationItems());
    }

    /// <summary>
    /// 关闭设置窗口命令。设置项的变更已经实时写入 Settings 并防抖保存，
    /// 此处仅触发窗口关闭（由 view 通过 CloseBehavior 处理）。
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        // 显式触发一次保存，避免用户在 500ms 内关闭导致丢失最后一项变更
        ConfigurationManager.SaveConfig();
        CloseRequested?.Invoke(this, System.EventArgs.Empty);
    }

    /// <summary>通知 view 关闭窗口</summary>
    public event System.EventHandler? CloseRequested;
}
