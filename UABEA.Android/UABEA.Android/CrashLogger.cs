using System;
using System.IO;
using UABEAvalonia;

namespace UABEA.Android
{
    /// <summary>全局崩溃日志工具，双写：私有目录（保证成功）+ 公共 logs 目录</summary>
    public static class CrashLogger
    {
        public static string PrivateLogPath { get; internal set; } = "/data/local/tmp/uabea.log";

        /// <summary>公共日志目录。由 MainActivity 注入。null 时只写私有目录。</summary>
        public static string? PublicLogDir { get; set; } = "/storage/emulated/0/logs";

        /// <summary>实际公共日志文件路径（根据 PublicLogDir 派生）</summary>
        public static string PublicLogPath => string.IsNullOrEmpty(PublicLogDir)
            ? "/storage/emulated/0/logs/uabea.log"
            : Path.Combine(PublicLogDir, "uabea.log");

        /// <summary>由 Android 端注入的剪贴板写入方法。任何崩溃都能把日志复制到剪贴板</summary>
        public static Action<string>? ClipboardWriter { get; set; }

        /// <summary>由 Android 端注入的 Toast 提示方法</summary>
        public static Action<string>? ToastShower { get; set; }

        /// <summary>由 Android 端 MainActivity 在启动时注入私有目录路径</summary>
        public static void SetPrivateLogDir(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                PrivateLogPath = Path.Combine(dir, "uabea.log");
                // 同步到 AppPaths 供其他模块使用
                AppPaths.CacheDir = dir;
            }
            catch { }
        }

        /// <summary>设置公共日志目录（由 MainActivity 在确认权限后注入）</summary>
        public static void SetPublicLogDir(string? dir)
        {
            PublicLogDir = dir;
            AppPaths.PublicLogDir = dir;
        }

        public static void Log(string source, string message)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{source}] {message}\n";
            try { File.AppendAllText(PrivateLogPath, line); } catch { }
            // 公共日志：仅在 PublicLogDir 非空且可写时尝试
            if (!string.IsNullOrEmpty(PublicLogDir))
            {
                try
                {
                    Directory.CreateDirectory(PublicLogDir);
                    File.AppendAllText(PublicLogPath, line);
                }
                catch { }
            }
        }

        public static void Log(string source, Exception ex) => Log(source, ex?.ToString() ?? "(null)");

        public static string ReadAll()
        {
            try { return File.ReadAllText(PrivateLogPath); }
            catch { return "(无法读取日志)"; }
        }

        /// <summary>把崩溃信息写入剪贴板 + Toast 提示。任何应用都能粘贴查看</summary>
        public static void CopyCrashToClipboard(string crashInfo)
        {
            try { ClipboardWriter?.Invoke(crashInfo); } catch { }
            try { ToastShower?.Invoke("UABEA 崩溃日志已复制到剪贴板，请在任意输入框粘贴查看"); } catch { }
        }

        /// <summary>崩溃标志。Android 端 OnCreate 出错时设为 true</summary>
        public static bool IsCrashed { get; set; } = false;

        public static void InstallManagedHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                Log("AppDomain.UnhandledException", ex);
                CopyCrashToClipboard(BuildCrashReport("AppDomain.UnhandledException", ex));
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Log("TaskScheduler.Unobserved", e.Exception);
                CopyCrashToClipboard(BuildCrashReport("TaskScheduler.Unobserved", e.Exception));
                e.SetObserved();
            };
        }

        public static string BuildCrashReport(string source, Exception? ex)
        {
            var report = $"===== UABEA 崩溃报告 =====\n";
            report += $"时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n";
            report += $"来源: {source}\n";
            report += $"设备: {GetDeviceInfo()}\n";
            report += $"------------------------\n";
            report += $"{ex?.ToString() ?? "(无异常对象)"}\n";
            report += $"------------------------\n";
            report += $"=== 完整启动日志 ===\n{ReadAll()}";
            return report;
        }

        private static string GetDeviceInfo()
        {
            try { return Environment.OSVersion + " / " + Environment.MachineName; }
            catch { return "unknown"; }
        }
    }
}
