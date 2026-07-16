using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace UABEAvalonia.ViewModels.Tools
{
    /// <summary>
    /// 图像预览子 ViewModel。负责缩放、背景切换、尺寸 / 格式信息显示。
    /// 参考自 UABEANext4 的 ImagePreviewViewModel，由 PreviewerToolViewModel 持有。
    /// </summary>
    public partial class ImagePreviewViewModel : ViewModelBase
    {
        /// <summary>当前预览的位图（由宿主将 BGRA 像素转换得到）。</summary>
        [ObservableProperty]
        private Bitmap? _image;

        /// <summary>尺寸信息，例如 "256 x 256 px"。</summary>
        [ObservableProperty]
        private string _imageInfo = "No image";

        /// <summary>缩放等级，1.0 为原始尺寸。</summary>
        [ObservableProperty]
        private double _zoomLevel = 1.0;

        /// <summary>是否自适应可用区域大小（保留给后续视图像加载时计算）。</summary>
        [ObservableProperty]
        private bool _fitToSize = true;

        /// <summary>纹理格式描述。</summary>
        [ObservableProperty]
        private string _textureFormat = string.Empty;

        /// <summary>当前背景索引（0-4）。</summary>
        [ObservableProperty]
        private int _backgroundIndex = 0;

        /// <summary>当前背景画刷。</summary>
        [ObservableProperty]
        private IBrush _previewBackground = Brushes.Transparent;

        // 5 种背景：透明 / 棋盘格 / 黑 / 白 / 灰
        private readonly IBrush[] _backgrounds =
        {
            Brushes.Transparent,
            CreateCheckerboardBrush(),
            Brushes.Black,
            Brushes.White,
            Brushes.Gray
        };

        // 背景标签，用于按钮显示当前背景类型
        private readonly string[] _bgLabels = { "A", "C", "B", "W", "G" };

        /// <summary>当前背景标签（透明/棋盘格/黑/白/灰）。</summary>
        public string CurrentBgLabel => _bgIndex < _bgLabels.Length ? _bgLabels[_bgIndex] : "?";

        // 内部索引用于切换（与 BackgroundIndex 同步）
        private int _bgIndex = 0;

        // 渲染时 Y 轴翻转标记，配合视图 ScaleTransform 使用
        public double RenderScaleY => -1.0;

        /// <summary>实际显示宽度 = 像素宽 * 缩放。</summary>
        public double DisplayWidth => Image != null ? Image.PixelSize.Width * ZoomLevel : 0;

        /// <summary>实际显示高度 = 像素高 * 缩放。</summary>
        public double DisplayHeight => Image != null ? Image.PixelSize.Height * ZoomLevel : 0;

        public ImagePreviewViewModel()
        {
            // 同步初始背景
            _previewBackground = _backgrounds[0];
        }

        /// <summary>
        /// 更新预览图像及附加信息。
        /// </summary>
        /// <param name="bitmap">新的位图（可能为 null 表示无图像）。</param>
        /// <param name="textureFormat">纹理格式描述，可为空。</param>
        public void UpdateImage(Bitmap? bitmap, string? textureFormat = null)
        {
            if (Image is IDisposable disposable)
            {
                disposable.Dispose();
            }

            Image = bitmap;
            if (bitmap != null)
            {
                ImageInfo = $"{bitmap.PixelSize.Width} x {bitmap.PixelSize.Height} px";
                TextureFormat = string.IsNullOrEmpty(textureFormat) ? "Unknown Format" : $"Format: {textureFormat}";
            }
            else
            {
                ImageInfo = "No image";
                TextureFormat = string.Empty;
                ZoomLevel = 1.0;
                OnPropertyChanged(nameof(DisplayWidth));
                OnPropertyChanged(nameof(DisplayHeight));
            }
        }

        /// <summary>按可用区域自适应缩放（不超过原始尺寸）。</summary>
        public void FitToAvailableSize(double availableWidth, double availableHeight)
        {
            if (Image == null || availableWidth <= 0 || availableHeight <= 0)
            {
                return;
            }

            double ratioX = availableWidth / Image.PixelSize.Width;
            double ratioY = availableHeight / Image.PixelSize.Height;

            ZoomLevel = Math.Min(ratioX, ratioY) * 0.95;
            if (ZoomLevel > 1.0)
            {
                ZoomLevel = 1.0;
            }
        }

        /// <summary>循环切换 5 种背景（透明/棋盘格/黑/白/灰）。</summary>
        [RelayCommand]
        private void CycleBackground()
        {
            _bgIndex = (_bgIndex + 1) % _backgrounds.Length;
            BackgroundIndex = _bgIndex;
            PreviewBackground = _backgrounds[_bgIndex];
            OnPropertyChanged(nameof(CurrentBgLabel));
        }

        /// <summary>放大 / 缩小（increase=true 放大）。</summary>
        public void AdjustZoom(bool increase)
        {
            double step = 1.1;
            if (increase)
            {
                ZoomLevel *= step;
            }
            else
            {
                ZoomLevel /= step;
            }

            ZoomLevel = Math.Clamp(ZoomLevel, 0.05, 20.0);
        }

        // 缩放变化时刷新派生尺寸属性
        partial void OnZoomLevelChanged(double value)
        {
            OnPropertyChanged(nameof(DisplayWidth));
            OnPropertyChanged(nameof(DisplayHeight));
        }

        // 构造棋盘格画刷
        private static IBrush CreateCheckerboardBrush()
        {
            return new DrawingBrush
            {
                TileMode = TileMode.Tile,
                SourceRect = new RelativeRect(0, 0, 20, 20, RelativeUnit.Absolute),
                DestinationRect = new RelativeRect(0, 0, 20, 20, RelativeUnit.Absolute),
                Drawing = new GeometryDrawing
                {
                    Brush = Brushes.LightGray,
                    Geometry = Geometry.Parse("M0,0 H10 V10 H0 Z M10,10 H20 V20 H10 Z")
                }
            };
        }
    }
}
