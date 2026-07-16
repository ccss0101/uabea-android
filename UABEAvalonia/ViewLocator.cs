using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Dock.Model.Core;
using System;

namespace UABEAvalonia
{
    /// <summary>
    /// ViewModel -> View 自动映射。
    /// 按命名约定 XxxViewModel 映射到 XxxView（位于同一程序集 UABEAvalonia）。
    /// 匹配规则：
    ///   1. 继承 ViewModelBase 的 ViewModel（旧规则）
    ///   2. 实现 Dock.Model.Core.IDockable 的 Dock dockable（P1 引入）
    ///      包括 Tool / Document / ToolDock / DocumentDock 等 Dock.Model.Mvvm.Controls 子类。
    /// </summary>
    public class ViewLocator : IDataTemplate
    {
        public bool SupportsRecycling => false;

        public Control? Build(object? data)
        {
            if (data == null)
            {
                return null;
            }

            // 按命名约定：XxxViewModel -> XxxView。
            // 注意 Replace("ViewModel", "View") 会把命名空间里的 "ViewModels"
            // 一并替换为 "Views"，因此 View 需放在对应的 Views.{子命名空间} 下。
            string name = data.GetType().FullName!.Replace("ViewModel", "View");
            Type? type = Type.GetType(name);

            if (type != null)
            {
                return (Control?)Activator.CreateInstance(type);
            }

            return new TextBlock { Text = "Not Found: " + name };
        }

        public bool Match(object? data)
        {
            // 同时匹配旧版 ViewModelBase 与 Dock 的 IDockable
            return data is ViewModelBase || data is IDockable;
        }
    }
}
