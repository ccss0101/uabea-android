using System;
using System.Collections.Generic;

namespace UABEAvalonia.Plugins
{
    /// <summary>
    /// 插件操作模式标志。参考 UABEANext4 的 UavPluginMode 枚举，
    /// 用于描述插件选项支持的触发场景（导入 / 导出 / 控制台 / 创建）。
    /// 位于平台无关的 UABEA.Core，供主项目与 Android 端共用。
    /// </summary>
    [Flags]
    public enum UavPluginMode
    {
        /// <summary>无模式。</summary>
        None = 0,
        /// <summary>导入模式（从外部数据导入到资产）。</summary>
        Import = 1,
        /// <summary>导出模式（从资产导出到外部数据）。</summary>
        Export = 2,
        /// <summary>控制台模式（命令行 / 脚本调用）。</summary>
        Console = 4,
        /// <summary>创建模式（新建资产）。</summary>
        Create = 8,
        /// <summary>所有模式（Import | Export | Console | Create 的组合）。</summary>
        All = 15
    }

    /// <summary>
    /// <see cref="UavPluginMode"/> 的扩展方法集合。GetUniqueFlags 既是静态方法，
    /// 也可作为扩展方法 <c>someMode.GetUniqueFlags()</c> 调用。
    /// </summary>
    public static class UavPluginModeExtensions
    {
        // 所有单独的 flag（不含 None 与 All 组合），用于遍历检查。
        private static readonly UavPluginMode[] _uniqueFlags =
        {
            UavPluginMode.Import,
            UavPluginMode.Export,
            UavPluginMode.Console,
            UavPluginMode.Create
        };

        /// <summary>
        /// 返回当前组合值中实际被设置的单独 flag（Import / Export / Console / Create）。
        /// 不含 None 与 All 组合值。
        /// 例如 <see cref="UavPluginMode.All"/> 返回全部四个单独 flag，
        /// <see cref="UavPluginMode.None"/> 返回空集合。
        /// PluginLoader 用此方法将组合模式拆分为单独 flag 逐个检查。
        /// </summary>
        public static IEnumerable<UavPluginMode> GetUniqueFlags(this UavPluginMode mode)
        {
            foreach (var flag in _uniqueFlags)
            {
                if ((mode & flag) == flag)
                {
                    yield return flag;
                }
            }
        }
    }
}
