using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace UABEAvalonia.Plugins
{
    /// <summary>
    /// 新版插件加载器。参考 UABEANext4 的 PluginLoader。
    /// 扫描 plugins 目录，用独立的 <see cref="PluginLoadContext"/> 加载每个 dll，
    /// 反射收集实现 <see cref="IUavPluginOption"/> 或 <see cref="IUavPluginPreviewer"/> 的类型。
    ///
    /// 与旧版 <c>PluginManager</c>（基于 <c>UABEAPlugin</c>，供 InfoWindow 等使用）并行存在，
    /// 互不影响。新版 PluginLoader 面向 Dock 界面的 PreviewerTool 与未来的 AssetDocument，
    /// 通过 <see cref="IUavPluginFunctions"/> 注入宿主服务，避免插件直接依赖 Avalonia。
    /// </summary>
    public class PluginLoader
    {
        // 已加载的插件选项列表
        private readonly List<IUavPluginOption> _options = new();
        // 已加载的插件预览器列表
        private readonly List<IUavPluginPreviewer> _previewers = new();
        // 已成功加载的 dll 全路径，用于去重避免重复加载
        private readonly HashSet<string> _loadedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 加载单个插件 dll。返回是否成功加载到至少一个插件类型。
        /// 加载失败（文件不存在 / 反射异常等）返回 false，不抛出异常。
        /// </summary>
        public bool LoadPlugin(string path)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);

                if (_loadedPaths.Contains(fullPath))
                    return true;

                if (!File.Exists(fullPath))
                    return false;

                // 每个 dll 一个独立的 PluginLoadContext，实现插件间依赖隔离
                var plugLoadCtx = new PluginLoadContext(fullPath);
                Assembly asm = plugLoadCtx.LoadAssemblyByPath(fullPath);

                bool anyAdded = false;

                foreach (Type type in asm.GetTypes())
                {
                    if (type.IsAbstract || type.IsInterface)
                        continue;

                    if (typeof(IUavPluginOption).IsAssignableFrom(type))
                    {
                        // 同一类型不重复添加
                        if (_options.Any(o => o.GetType() == type))
                            continue;

                        if (Activator.CreateInstance(type) is IUavPluginOption plugInst)
                        {
                            _options.Add(plugInst);
                            anyAdded = true;
                        }
                    }
                    else if (typeof(IUavPluginPreviewer).IsAssignableFrom(type))
                    {
                        if (_previewers.Any(p => p.GetType() == type))
                            continue;

                        if (Activator.CreateInstance(type) is IUavPluginPreviewer plugInst)
                        {
                            _previewers.Add(plugInst);
                            anyAdded = true;
                        }
                    }
                }

                if (anyAdded)
                    _loadedPaths.Add(fullPath);

                return anyAdded;
            }
            catch
            {
                // 单个插件加载失败不影响其它插件与主程序
                return false;
            }
        }

        /// <summary>
        /// 扫描目录下所有 *.dll 并加载。目录不存在则创建（首次运行时自动建 plugins 目录）。
        /// </summary>
        public void LoadPluginsInDirectory(string directory)
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                return;
            }

            foreach (string file in Directory.EnumerateFiles(directory, "*.dll"))
            {
                LoadPlugin(file);
            }
        }

        /// <summary>
        /// 返回支持指定模式与选择项的所有选项。
        /// 将请求模式与选项 Options 取交集，再按单独 flag 逐个调用
        /// <see cref="IUavPluginOption.SupportsSelection"/> 检查，每个支持的 flag 产生一条结果。
        /// </summary>
        public List<PluginOptionModePair> GetOptionsThatSupport(
            Workspace workspace, UavPluginMode mode, List<AssetContainer> selection)
        {
            var options = new List<PluginOptionModePair>();
            foreach (var option in _options)
            {
                UavPluginMode bothOpt = mode & option.Options;
                foreach (var flag in bothOpt.GetUniqueFlags())
                {
                    // All 是组合值，不应作为单独模式出现
                    if (flag == UavPluginMode.All)
                        continue;

                    bool supported = option.SupportsSelection(workspace, flag, selection);
                    if (supported)
                        options.Add(new PluginOptionModePair(option, flag));
                }
            }

            return options;
        }

        /// <summary>
        /// 返回支持当前选择项的所有预览器及其预览类型。
        /// PreviewerToolViewModel 用此方法查找首个可用的预览器。
        /// </summary>
        public List<PluginPreviewerTypePair> GetPreviewersThatSupport(
            Workspace workspace, List<AssetContainer> selection)
        {
            var previewers = new List<PluginPreviewerTypePair>();
            foreach (var previewer in _previewers)
            {
                var previewType = previewer.SupportsPreview(workspace, selection);
                if (previewType != UavPluginPreviewerType.None)
                    previewers.Add(new PluginPreviewerTypePair(previewer, previewType));
            }

            return previewers;
        }
    }
}
