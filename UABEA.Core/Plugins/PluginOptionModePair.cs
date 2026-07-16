namespace UABEAvalonia.Plugins
{
    /// <summary>
    /// 持有 <see cref="IUavPluginOption"/> 与其支持的某个 <see cref="UavPluginMode"/>，
    /// 用于 <see cref="PluginLoader.GetOptionsThatSupport"/> 返回结果。
    /// 参考自 UABEANext4 的 PluginOptionModePair。
    /// </summary>
    public class PluginOptionModePair
    {
        /// <summary>插件选项实例。</summary>
        public IUavPluginOption Option { get; }

        /// <summary>本条目对应的模式（已拆分为单独的 flag）。</summary>
        public UavPluginMode Mode { get; }

        public PluginOptionModePair(IUavPluginOption option, UavPluginMode mode)
        {
            Option = option;
            Mode = mode;
        }

        public override string ToString() => Option.Name;
    }
}
