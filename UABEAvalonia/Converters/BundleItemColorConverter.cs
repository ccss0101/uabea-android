using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace UABEAvalonia
{
    /// <summary>
    /// 把 BundleItemType 转换为主题化的 IBrush。
    /// P3-A 改造：从硬编码 Dark/Light brush 对改为通过 TryFindResource 查找
    /// SimpleLight/SimpleDark.axaml 中的 WorkspaceItem*Brush 资源，
    /// 自动随 RequestedThemeVariant 切换。
    /// </summary>
    public class BundleItemColorConverter : IValueConverter
    {
        private const string BundleKey = "WorkspaceItemBundleBrush";
        private const string AssetsKey = "WorkspaceItemAssetsBrush";
        private const string ResourceKey = "WorkspaceItemResourceBrush";
        private const string OtherKey = "WorkspaceItemOtherBrush";
        private const string EtcKey = "WorkspaceItemEtcBrush";

        private static readonly SolidColorBrush Fallback = new SolidColorBrush(Colors.Gray);

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not BundleItemType itemType)
                return null;

            string key = itemType switch
            {
                BundleItemType.Serialized => AssetsKey,
                BundleItemType.Ress => OtherKey,
                BundleItemType.Resource => ResourceKey,
                _ => EtcKey,
            };

            return TryFindBrush(key);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private static IBrush TryFindBrush(string key)
        {
            var app = Application.Current;
            if (app != null
                && app.TryGetResource(key, app.ActualThemeVariant, out object? resource)
                && resource is IBrush brush)
            {
                return brush;
            }
            return Fallback;
        }
    }
}
