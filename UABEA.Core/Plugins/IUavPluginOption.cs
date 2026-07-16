using System.Collections.Generic;
using System.Threading.Tasks;

namespace UABEAvalonia.Plugins
{
    /// <summary>
    /// 新版插件操作接口。与旧版 <c>UABEAPluginOption</c> 并行存在，
    /// 参考 UABEANext4 的 IUavPluginOption，但做了平台无关化改造：
    ///   - 选择项使用 <see cref="List{AssetContainer}"/> 而非单个资产
    ///   - 宿主服务通过 <see cref="IUavPluginFunctions"/> 注入，避免直接依赖 Avalonia Window
    /// 插件实现此接口后由 <see cref="PluginLoader"/> 反射加载并注册。
    /// </summary>
    public interface IUavPluginOption
    {
        /// <summary>选项名称（菜单 / 列表显示用）。</summary>
        string Name { get; }

        /// <summary>选项描述（工具提示用）。</summary>
        string Description { get; }

        /// <summary>本选项支持的模式标志组合。</summary>
        UavPluginMode Options { get; }

        /// <summary>
        /// 判断当前选择项在指定模式下是否可由本选项处理。
        /// </summary>
        bool SupportsSelection(Workspace workspace, UavPluginMode mode, List<AssetContainer> selection);

        /// <summary>
        /// 执行插件操作。返回 true 表示执行成功（可触发后续刷新），false 表示失败或取消。
        /// </summary>
        Task<bool> Execute(Workspace workspace, IUavPluginFunctions funcs, UavPluginMode mode, List<AssetContainer> selection);
    }
}
