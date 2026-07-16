namespace UABEAvalonia.Plugins
{
    /// <summary>
    /// 包装插件信息：插件名 + 选项 + 模式。用于菜单 / 列表展示。
    /// 参考自 UABEANext4 的 PluginItemInfo，但去掉对 AssetDocumentViewModel 的依赖
    /// （执行逻辑由调用方自行处理），保持平台无关。
    /// </summary>
    public class PluginItemInfo
    {
        /// <summary>插件名（来自选项实例类型所在程序集 / 命名空间，调用方设置）。</summary>
        public string PluginName { get; }

        /// <summary>插件选项实例，可能为 null（仅用于展示分组时）。</summary>
        public IUavPluginOption? Option { get; }

        /// <summary>本条目对应的操作模式。</summary>
        public UavPluginMode Mode { get; }

        public PluginItemInfo(string pluginName, IUavPluginOption? option, UavPluginMode mode)
        {
            PluginName = pluginName;
            Option = option;
            Mode = mode;
        }

        /// <summary>显示名，菜单 / 列表绑定用。优先返回选项名，回退到插件名。</summary>
        public string DisplayName => Option?.Name ?? PluginName;

        public override string ToString() => DisplayName;
    }
}
