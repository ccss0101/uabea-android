namespace UABEAvalonia.Plugins
{
    /// <summary>
    /// 插件预览器支持的预览类型。参考 UABEANext4 的 UavPluginPreviewerType。
    /// 位于平台无关的 UABEA.Core，供主项目与 Android 端共用。
    /// </summary>
    public enum UavPluginPreviewerType
    {
        /// <summary>不支持预览。</summary>
        None,
        /// <summary>文本预览（只读文本）。</summary>
        Text,
        /// <summary>图像预览（BGRA 像素数据）。</summary>
        Image,
        /// <summary>网格预览（留给 P1-C 实现）。</summary>
        Mesh
    }
}
