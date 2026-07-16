using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace UABEAvalonia.Plugins
{
    /// <summary>
    /// 插件隔离加载上下文。参考 UABEANext4 的 PluginLoadContext。
    /// 每个插件 dll 拥有独立的 <see cref="AssemblyLoadContext"/>，配合
    /// <see cref="AssemblyDependencyResolver"/> 解析插件依赖（含非托管 dll），
    /// 避免不同插件间的依赖冲突。隔离粒度为单个 dll。
    ///
    /// System.Runtime.Loader（AssemblyLoadContext / AssemblyDependencyResolver）
    /// 在 .NET 8 中属内建库，无需额外 PackageReference。
    /// </summary>
    internal class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string pluginPath)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        /// <summary>按程序集名加载（走 resolver 解析插件 deps.json）。</summary>
        public Assembly LoadAssemblyByName(string name)
        {
            return LoadFromAssemblyName(new AssemblyName(name));
        }

        /// <summary>
        /// 按 dll 路径加载：用文件名构造 <see cref="AssemblyName"/>，
        /// 再交由 <see cref="Load"/> 通过 resolver 解析到该插件 dll。
        /// </summary>
        public Assembly LoadAssemblyByPath(string path)
        {
            return LoadFromAssemblyName(new AssemblyName(Path.GetFileNameWithoutExtension(path)));
        }

        /// <summary>
        /// 解析插件依赖程序集：优先用插件的 deps.json 解析，
        /// 返回 null 则回退到默认上下文（共享宿主已加载的程序集）。
        /// </summary>
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            if (assemblyPath != null)
            {
                return LoadFromAssemblyPath(assemblyPath);
            }

            return null;
        }

        /// <summary>解析插件依赖的非托管 dll（如 native 库）。</summary>
        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            string? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (libraryPath != null)
            {
                return LoadUnmanagedDllFromPath(libraryPath);
            }

            return IntPtr.Zero;
        }
    }
}
