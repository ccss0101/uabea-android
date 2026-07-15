using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Widget;
using Avalonia;
using Avalonia.Android;
using System;
using System.IO;
using System.Threading.Tasks;

namespace UABEAvalonia
{
    [Activity(
        Label = "UABEA",
        Theme = "@style/MyTheme",
        MainLauncher = true,
        Exported = true,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.KeyboardHidden)]
    public class MainActivity : AvaloniaMainActivity<App>
    {
        public const string PublicLogDir = "/storage/emulated/0/logs";
        public const string PublicLogFile = "/storage/emulated/0/logs/uabea.log";

        // 私有日志目录，无需任何权限，保证一定能写入
        private static string? _privateLogFile;

        static MainActivity()
        {
            // 在最早阶段挂全局异常捕获，确保启动期崩溃也能被记录
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                WriteLog("AppDomain.UnhandledException", e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                WriteLog("TaskScheduler.UnobservedTaskException", e.Exception);
                e.SetObserved();
            };
            AndroidEnvironment.UnhandledExceptionRaiser += (s, e) =>
            {
                WriteLog("AndroidEnvironment.UnhandledException", e.Exception);
                e.Handled = true;
            };
        }

        public static string PrivateLogFile
        {
            get
            {
                if (_privateLogFile == null)
                {
                    try
                    {
                        var dir = Android.App.Application.Context.GetExternalFilesDir(null)?.AbsolutePath
                                  ?? Android.App.Application.Context.FilesDir?.AbsolutePath
                                  ?? "/data/data/com.uabea.app/files";
                        Directory.CreateDirectory(dir);
                        _privateLogFile = Path.Combine(dir, "uabea.log");
                    }
                    catch
                    {
                        _privateLogFile = "/data/local/tmp/uabea.log";
                    }
                }
                return _privateLogFile;
            }
        }

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            WriteLog("MainActivity.OnCreate", "start, sdk=" + Build.VERSION.SdkInt);
            try
            {
                RequestStoragePermission();
                base.OnCreate(savedInstanceState);
                WriteLog("MainActivity.OnCreate", "base.OnCreate ok");
            }
            catch (Exception ex)
            {
                WriteLog("MainActivity.OnCreate", ex);
                ShowCrashDialog(ex);
            }
        }

        private void RequestStoragePermission()
        {
            try
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
                {
                    if (!Android.OS.Environment.IsExternalStorageManager)
                    {
                        try
                        {
                            var intent = new Intent(Android.Provider.Settings.ActionManageAllFilesAccessPermission);
                            intent.SetData(Android.Net.Uri.Parse("package:" + PackageName));
                            StartActivity(intent);
                        }
                        catch (Exception ex)
                        {
                            WriteLog("RequestStoragePermission", ex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog("RequestStoragePermission", ex);
            }
        }

        /// <summary>同时写入私有目录（保证成功）和公共目录（需权限）</summary>
        public static void WriteLog(string source, string message)
        {
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{source}] {message}\n";
            // 1. 私有目录，无需权限，一定写得进去
            try { File.AppendAllText(PrivateLogFile, line); } catch { }
            // 2. 公共目录，可能因权限失败
            try
            {
                Directory.CreateDirectory(PublicLogDir);
                File.AppendAllText(PublicLogFile, line);
            }
            catch { }
        }

        public static void WriteLog(string source, Exception? ex)
        {
            WriteLog(source, ex?.ToString() ?? "(null exception)");
        }

        /// <summary>Avalonia 没起来时，用原生 Android 对话框显示崩溃原因</summary>
        public void ShowCrashDialog(Exception ex)
        {
            try
            {
                RunOnUiThread(() =>
                {
                    new AlertDialog.Builder(this)
                        .SetTitle("UABEA crashed")
                        .SetMessage(ex.ToString())
                        .SetPositiveButton("OK", (s, e) => { })
                        .SetCancelable(false)
                        .Show();
                });
            }
            catch { }
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            WriteLog("CustomizeAppBuilder", "start");
            try
            {
                var b = base.CustomizeAppBuilder(builder).LogToTrace();
                WriteLog("CustomizeAppBuilder", "ok");
                return b;
            }
            catch (Exception ex)
            {
                WriteLog("CustomizeAppBuilder", ex);
                throw;
            }
        }
    }
}
