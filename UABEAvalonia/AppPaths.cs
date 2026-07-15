using System.IO;

namespace UABEAvalonia
{
    /// <summary>平台路径配置。Android 项目在启动时设置 ClassDataPath / CacheDir / PublicLogDir。</summary>
    public static class AppPaths
    {
        public static string ClassDataPath { get; set; } = Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "classdata.tpk");

        /// <summary>平台缓存目录（Android 注入 CacheDir.AbsolutePath）。用于把选择器返回的流复制为本地文件。</summary>
        public static string? CacheDir { get; set; }

        /// <summary>公共日志目录（Android 注入 /storage/emulated/0/logs 或私有目录）。null 表示不写公共日志。</summary>
        public static string? PublicLogDir { get; set; }

        /// <summary>获取一个可用的缓存目录。优先使用注入的 CacheDir，否则用系统临时目录。</summary>
        public static string GetCacheDir()
        {
            if (!string.IsNullOrEmpty(CacheDir) && Directory.Exists(CacheDir))
                return CacheDir;
            return Path.GetTempPath();
        }
    }
}
