using CommunityToolkit.Mvvm.ComponentModel;

namespace UABEAvalonia
{
    /// <summary>
    /// 所有 ViewModel 的基类，提供 INotifyPropertyChanged 支持。
    /// 位于平台无关的 UABEA.Core，供主项目和 Android 共用。
    /// </summary>
    public abstract class ViewModelBase : ObservableObject
    {
    }
}
