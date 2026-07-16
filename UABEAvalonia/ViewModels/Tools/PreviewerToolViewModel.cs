using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Dock.Model.Mvvm.Controls;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UABEAvalonia.Mesh;
using UABEAvalonia.Plugins;

namespace UABEAvalonia.ViewModels.Tools
{
    /// <summary>
    /// 统一 Previewer 面板。参考自 UABEANext4 的 PreviewerToolViewModel。
    /// 监听 <see cref="AssetsSelectedMessage"/>，通过注入的 <see cref="PluginLoader"/>
    /// 查找首个支持当前选择项的 previewer，按类型（Text / Image / Mesh）渲染。
    ///
    /// PluginLoader 由 App 启动时加载 plugins 目录填充，经 MainViewModel ->
    /// MainDockFactory 注入到本类。_pluginLoader 为 null 时回退到内置 _previewers
    /// 列表（当前为空），此时选中资产会显示 "No preview available."。
    /// </summary>
    public partial class PreviewerToolViewModel : Tool
    {
        private const string ToolTitle = "Previewer";

        /// <summary>多 bundle 工作区引用，供预览器查询使用。</summary>
        public Workspace Workspace { get; }

        /// <summary>当前预览类型，视图据此切换显示面板。</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsTextPreview))]
        [NotifyPropertyChangedFor(nameof(IsImagePreview))]
        [NotifyPropertyChangedFor(nameof(IsMeshPreview))]
        [NotifyPropertyChangedFor(nameof(IsNonePreview))]
        private UavPluginPreviewerType _previewType = UavPluginPreviewerType.None;

        /// <summary>文本预览内容（PreviewType == Text 时显示）。</summary>
        [ObservableProperty]
        private string _previewText = "No preview available.";

        /// <summary>图像预览位图（PreviewType == Image 时显示）。</summary>
        [ObservableProperty]
        private Bitmap? _previewImage;

        /// <summary>Mesh 3D 预览数据（PreviewType == Mesh 时由 MeshPreviewerControl 渲染）。</summary>
        [ObservableProperty]
        private MeshObj? _activeMesh;

        /// <summary>图像预览子 VM，承载缩放 / 背景 / 尺寸信息。</summary>
        [ObservableProperty]
        private ImagePreviewViewModel _imagePreview = new();

        // 当前已注册的内置预览器列表。新版插件系统通过 _pluginLoader 提供，
        // 此列表仅作为回退（_pluginLoader 为 null 时使用），当前为空。
        private readonly List<IUavPluginPreviewer> _previewers = new();

        // 新版插件加载器（P2 接入）。为 null 时回退到内置 _previewers 列表。
        // 由 MainViewModel -> MainDockFactory -> 本构造函数 注入。
        private readonly PluginLoader? _pluginLoader;

        /// <summary>当前是否为文本预览。</summary>
        public bool IsTextPreview => PreviewType == UavPluginPreviewerType.Text;

        /// <summary>当前是否为图像预览。</summary>
        public bool IsImagePreview => PreviewType == UavPluginPreviewerType.Image;

        /// <summary>当前是否为 Mesh 3D 预览。</summary>
        public bool IsMeshPreview => PreviewType == UavPluginPreviewerType.Mesh;

        /// <summary>当前是否无可显示预览（显示提示文本）。</summary>
        public bool IsNonePreview => PreviewType == UavPluginPreviewerType.None;

        public PreviewerToolViewModel(Workspace workspace)
            : this(workspace, null)
        {
        }

        /// <summary>
        /// 注入新版插件加载器。_pluginLoader 不为 null 时优先通过
        /// <see cref="PluginLoader.GetPreviewersThatSupport"/> 查找预览器；
        /// 为 null 时回退到内置 <c>_previewers</c> 列表。
        /// </summary>
        public PreviewerToolViewModel(Workspace workspace, PluginLoader? pluginLoader)
        {
            Workspace = workspace;
            _pluginLoader = pluginLoader;

            Id = ToolTitle;
            Title = ToolTitle;

            // 监听资产选中消息（来自资产文档）
            WeakReferenceMessenger.Default.Register<AssetsSelectedMessage>(this, OnAssetsSelected);
            // 监听工作区关闭消息，清空预览
            WeakReferenceMessenger.Default.Register<WorkspaceClosingMessage>(this, OnWorkspaceClosing);
        }

        private void OnAssetsSelected(object recipient, AssetsSelectedMessage message)
        {
            HandleAssetPreview(message.Value);
        }

        private void OnWorkspaceClosing(object recipient, WorkspaceClosingMessage message)
        {
            HandleAssetPreview(null);
        }

        /// <summary>
        /// 处理资产预览：查找支持当前选择项的预览器并按类型渲染。
        /// </summary>
        private void HandleAssetPreview(List<AssetContainer>? assets)
        {
            if (assets == null || assets.Count == 0)
            {
                SetEmpty();
                return;
            }

            IUavPluginPreviewer? previewer = null;
            UavPluginPreviewerType previewType = UavPluginPreviewerType.None;

            // 新版插件系统：通过 PluginLoader 查找支持当前选择项的预览器。
            // _pluginLoader 为 null（未注入）时回退到内置 _previewers 列表。
            // 用 try-catch 包裹查找逻辑，确保插件系统未就绪 / 插件异常时不崩溃。
            try
            {
                if (_pluginLoader != null)
                {
                    var pairs = _pluginLoader.GetPreviewersThatSupport(Workspace, assets);
                    if (pairs.Count > 0)
                    {
                        previewer = pairs[0].Previewer;
                        previewType = pairs[0].PreviewType;
                    }
                }
                else
                {
                    foreach (var p in _previewers)
                    {
                        var type = p.SupportsPreview(Workspace, assets);
                        if (type != UavPluginPreviewerType.None)
                        {
                            previewer = p;
                            previewType = type;
                            break;
                        }
                    }
                }
            }
            catch
            {
                // 插件系统尚未就绪或抛出异常，忽略并显示空预览
            }

            if (previewer == null)
            {
                SetDisplayText("No preview available.");
                return;
            }

            try
            {
                switch (previewType)
                {
                    case UavPluginPreviewerType.Text:
                        PreviewType = UavPluginPreviewerType.Text;
                        ClearImage();
                        PreviewText = previewer.ExecuteText(Workspace, assets) ?? string.Empty;
                        break;

                    case UavPluginPreviewerType.Image:
                        {
                            PreviewType = UavPluginPreviewerType.Image;
                            byte[] bgra = previewer.ExecuteImage(Workspace, assets, out int width, out int height);
                            WriteableBitmap? bitmap = CreateBitmapFromBgra(bgra, width, height);
                            if (bitmap != null)
                            {
                                ImagePreview.UpdateImage(bitmap);
                                PreviewImage = bitmap;
                            }
                            else
                            {
                                SetDisplayText("Failed to decode image.");
                            }
                            break;
                        }

                    case UavPluginPreviewerType.Mesh:
                        {
                            PreviewType = UavPluginPreviewerType.Mesh;
                            ClearImage();
                            MeshObj? meshObj = previewer.ExecuteMesh(Workspace, assets);
                            if (meshObj != null)
                            {
                                ActiveMesh = meshObj;
                            }
                            else
                            {
                                SetDisplayText("No preview available (mesh couldn't be loaded).");
                            }
                            break;
                        }

                    default:
                        SetDisplayText($"Preview type {previewType} not supported.");
                        break;
                }
            }
            catch (Exception ex)
            {
                SetDisplayText($"Preview error: {ex.Message}");
            }
        }

        /// <summary>切换到文本预览并显示指定文本。</summary>
        private void SetDisplayText(string text)
        {
            ClearImage();
            PreviewType = UavPluginPreviewerType.Text;
            PreviewText = text;
        }

        /// <summary>清空预览（回到 None 状态）。</summary>
        private void SetEmpty()
        {
            ClearImage();
            PreviewType = UavPluginPreviewerType.None;
            PreviewText = string.Empty;
        }

        /// <summary>释放图像预览资源并清空位图引用。</summary>
        private void ClearImage()
        {
            ImagePreview.UpdateImage(null);
            PreviewImage = null;
            ActiveMesh = null;
        }

        /// <summary>
        /// 将 BGRA 像素数据转换为 Avalonia <see cref="WriteableBitmap"/>。
        /// 参考任务约束：ExecuteImage 返回 BGRA 字节，宿主负责转成位图。
        /// </summary>
        private static WriteableBitmap? CreateBitmapFromBgra(byte[] bgra, int width, int height)
        {
            if (bgra == null || bgra.Length == 0 || width <= 0 || height <= 0)
            {
                return null;
            }

            var bitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Unpremul);

            using (var framebuffer = bitmap.Lock())
            {
                int sourceStride = width * 4;
                int targetStride = framebuffer.RowBytes;
                if (targetStride == sourceStride)
                {
                    // stride 一致，整块复制
                    Marshal.Copy(bgra, 0, framebuffer.Address, bgra.Length);
                }
                else
                {
                    // stride 不一致，逐行复制
                    for (int y = 0; y < height; y++)
                    {
                        Marshal.Copy(bgra, y * sourceStride, IntPtr.Add(framebuffer.Address, y * targetStride), sourceStride);
                    }
                }
            }

            return bitmap;
        }
    }
}
