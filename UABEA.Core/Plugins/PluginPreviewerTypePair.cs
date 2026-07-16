namespace UABEAvalonia.Plugins
{
    /// <summary>
    /// 持有 <see cref="IUavPluginPreviewer"/> 与其返回的 <see cref="UavPluginPreviewerType"/>，
    /// 用于 <see cref="PluginLoader.GetPreviewersThatSupport"/> 返回结果。
    /// 参考自 UABEANext4 的 PluginPreviewerTypePair。
    /// </summary>
    public class PluginPreviewerTypePair
    {
        /// <summary>插件预览器实例。</summary>
        public IUavPluginPreviewer Previewer { get; }

        /// <summary>该预览器对当前选择项支持的预览类型。</summary>
        public UavPluginPreviewerType PreviewType { get; }

        public PluginPreviewerTypePair(IUavPluginPreviewer previewer, UavPluginPreviewerType previewType)
        {
            Previewer = previewer;
            PreviewType = previewType;
        }

        public override string ToString() => Previewer.Name;
    }
}
