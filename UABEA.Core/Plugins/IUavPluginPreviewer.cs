using System.Collections.Generic;
using UABEAvalonia.Mesh;

namespace UABEAvalonia.Plugins
{
    /// <summary>
    /// 插件预览器接口。插件实现此接口以向统一 Previewer 面板提供文本 / 图像预览。
    /// 参考自 UABEANext4 的 IUavPluginPreviewer，但做了平台无关化改造：
    ///   - 不引用 Avalonia（Bitmap 等类型由宿主在 UI 层转换）
    ///   - 选择项使用 <see cref="List{AssetContainer}"/> 而非单个 AssetInst，
    ///     以支持多选预览扩展
    ///   - 图像以 BGRA 原始像素字节返回，由宿主转成 WriteableBitmap
    /// </summary>
    public interface IUavPluginPreviewer
    {
        /// <summary>预览器名称（显示用）。</summary>
        string Name { get; }

        /// <summary>预览器描述。</summary>
        string Description { get; }

        /// <summary>
        /// 判断当前选择是否可被本预览器处理，返回支持的预览类型；
        /// 返回 <see cref="UavPluginPreviewerType.None"/> 表示不支持。
        /// </summary>
        UavPluginPreviewerType SupportsPreview(Workspace workspace, List<AssetContainer> selection);

        /// <summary>
        /// 执行文本预览，返回预览文本内容。
        /// 仅在 <see cref="SupportsPreview"/> 返回 <see cref="UavPluginPreviewerType.Text"/> 时调用。
        /// </summary>
        string ExecuteText(Workspace workspace, List<AssetContainer> selection);

        /// <summary>
        /// 执行图像预览，返回 BGRA 像素数据，并通过 out 参数返回宽高。
        /// 仅在 <see cref="SupportsPreview"/> 返回 <see cref="UavPluginPreviewerType.Image"/> 时调用。
        /// 宿主负责将返回的字节数组转换为平台位图（如 Avalonia WriteableBitmap）。
        /// </summary>
        byte[] ExecuteImage(Workspace workspace, List<AssetContainer> selection, out int width, out int height);

        /// <summary>
        /// 执行 Mesh 3D 预览，返回解析后的 <see cref="MeshObj"/>（含顶点 / 法线 / 索引）。
        /// 仅在 <see cref="SupportsPreview"/> 返回 <see cref="UavPluginPreviewerType.Mesh"/> 时调用。
        /// 宿主将其绑定到 OpenGL 渲染控件（如 MeshPreviewerControl）。
        /// </summary>
        MeshObj? ExecuteMesh(Workspace workspace, List<AssetContainer> selection);

        /// <summary>
        /// 工作区关闭 / 重置时调用，释放预览器持有的资源。
        /// </summary>
        void Cleanup();
    }
}
