using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Provider;
using Android.Widget;
using Avalonia;
using Avalonia.Android;
using System;
using System.IO;
using UABEAvalonia;

namespace UABEA.Android.Android;

[Activity(
    Label = "UABEA",
    Theme = "@style/MyTheme.NoActionBar",
    MainLauncher = true,
    Exported = true,
    ScreenOrientation = ScreenOrientation.Portrait,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<global::UABEA.Android.App>
{
    private const int RequestStoragePermId = 1001;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // ===== 1. 最早设置：日志路径 + 剪贴板 + Toast（在任何可能崩溃的代码之前）=====
        global::UABEA.Android.CrashLogger.SetPrivateLogDir(FilesDir!.AbsolutePath);
        // 先尝试设置公共日志目录（即使权限不足也会安全失败，仅写私有目录）
        TryEnablePublicLog();

        // 注入剪贴板写入能力：用 Android 原生 ClipboardManager，无条件可写
        global::UABEA.Android.CrashLogger.ClipboardWriter = text =>
        {
            try
            {
                var clipboard = (ClipboardManager)GetSystemService(ClipboardService);
                var clip = ClipData.NewPlainText("UABEA Crash Log", text);
                clipboard.PrimaryClip = clip;
            }
            catch { }
        };

        // 注入 Toast 提示能力
        global::UABEA.Android.CrashLogger.ToastShower = msg =>
        {
            try { RunOnUiThread(() => Toast.MakeText(this, msg, ToastLength.Long)?.Show()); }
            catch { }
        };

        // ===== 2. 安装全局异常捕获（托管 + Android 运行时）=====
        global::UABEA.Android.CrashLogger.InstallManagedHandlers();
        AndroidEnvironment.UnhandledExceptionRaiser += (s, e) =>
        {
            global::UABEA.Android.CrashLogger.Log("AndroidEnv.UnhandledException", e.Exception);
            global::UABEA.Android.CrashLogger.CopyCrashToClipboard(
                global::UABEA.Android.CrashLogger.BuildCrashReport("AndroidEnv.UnhandledException", e.Exception));
            e.Handled = true;
        };

        global::UABEA.Android.CrashLogger.Log("MainActivity", $"OnCreate start, sdk={Build.VERSION.SdkInt}");

        try
        {
            ExtractClassData();
            base.OnCreate(savedInstanceState);
            global::UABEA.Android.CrashLogger.Log("MainActivity", "OnCreate base ok");

            // ===== 3. 主动申请存储权限 =====
            RequestStoragePermissions();
        }
        catch (Exception ex)
        {
            global::UABEA.Android.CrashLogger.Log("MainActivity.OnCreate", ex);
            global::UABEA.Android.CrashLogger.IsCrashed = true;
            global::UABEA.Android.CrashLogger.CopyCrashToClipboard(
                global::UABEA.Android.CrashLogger.BuildCrashReport("MainActivity.OnCreate", ex));
        }
    }

    /// <summary>尝试启用公共日志目录。Android 11+ 需要 MANAGE_EXTERNAL_STORAGE，否则回退到应用私有目录。</summary>
    private void TryEnablePublicLog()
    {
        try
        {
            // Android 11 (R, API 30) 及以上需要 MANAGE_EXTERNAL_STORAGE 才能写 /storage/emulated/0/logs
            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
                if (global::Android.OS.Environment.IsExternalStorageManager)
                {
                    global::UABEA.Android.CrashLogger.SetPublicLogDir("/storage/emulated/0/logs");
                    global::UABEA.Android.CrashLogger.Log("TryEnablePublicLog", "MANAGE_EXTERNAL_STORAGE 已授予，公共日志写入 /storage/emulated/0/logs");
                }
                else
                {
                    // 未授权，回退到应用专属外部目录（无需任何权限）
                    var extDir = GetExternalFilesDir(null)?.AbsolutePath;
                    if (!string.IsNullOrEmpty(extDir))
                    {
                        var logDir = Path.Combine(extDir, "logs");
                        global::UABEA.Android.CrashLogger.SetPublicLogDir(logDir);
                        global::UABEA.Android.CrashLogger.Log("TryEnablePublicLog", $"MANAGE_EXTERNAL_STORAGE 未授予，日志回退到 {logDir}");
                    }
                }
            }
            else
            {
                // Android 10 及以下，传统权限即可
                global::UABEA.Android.CrashLogger.SetPublicLogDir("/storage/emulated/0/logs");
                global::UABEA.Android.CrashLogger.Log("TryEnablePublicLog", "Android<=10，公共日志写入 /storage/emulated/0/logs");
            }
        }
        catch (Exception ex)
        {
            global::UABEA.Android.CrashLogger.Log("TryEnablePublicLog", ex);
        }
    }

    /// <summary>申请存储权限。Android 11+ 引导用户去设置开启 MANAGE_EXTERNAL_STORAGE。</summary>
    private void RequestStoragePermissions()
    {
        try
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
                // Android 11+：需要 MANAGE_EXTERNAL_STORAGE 才能访问公共目录
                if (!global::Android.OS.Environment.IsExternalStorageManager)
                {
                    global::UABEA.Android.CrashLogger.Log("RequestStorage", "请求 MANAGE_EXTERNAL_STORAGE（跳转设置）");
                    try
                    {
                        var intent = new Intent(Settings.ActionManageAppAllFilesAccessPermission);
                        intent.SetData(global::Android.Net.Uri.Parse("package:" + PackageName));
                        StartActivityForResult(intent, RequestStoragePermId);
                    }
                    catch (Exception ex)
                    {
                        global::UABEA.Android.CrashLogger.Log("RequestStorage.ManageAll", ex);
                        // 部分设备不支持该 Intent，回退到普通权限申请
                        RequestLegacyStoragePermissions();
                    }
                }
                else
                {
                    global::UABEA.Android.CrashLogger.Log("RequestStorage", "MANAGE_EXTERNAL_STORAGE 已拥有");
                }
            }
            else
            {
                RequestLegacyStoragePermissions();
            }
        }
        catch (Exception ex)
        {
            global::UABEA.Android.CrashLogger.Log("RequestStorage", ex);
        }
    }

    /// <summary>申请传统存储权限（Android 10 及以下）</summary>
    private void RequestLegacyStoragePermissions()
    {
        try
        {
            string[] perms = {
                global::Android.Manifest.Permission.ReadExternalStorage,
                global::Android.Manifest.Permission.WriteExternalStorage
            };
            var need = new System.Collections.Generic.List<string>();
            foreach (var p in perms)
            {
                if (CheckSelfPermission(p) != Permission.Granted)
                    need.Add(p);
            }
            if (need.Count > 0)
            {
                global::UABEA.Android.CrashLogger.Log("RequestLegacy", "申请权限: " + string.Join(", ", need));
                RequestPermissions(need.ToArray(), RequestStoragePermId);
            }
            else
            {
                global::UABEA.Android.CrashLogger.Log("RequestLegacy", "传统存储权限已拥有");
            }
        }
        catch (Exception ex)
        {
            global::UABEA.Android.CrashLogger.Log("RequestLegacy", ex);
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        try
        {
            if (requestCode == RequestStoragePermId)
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.R && global::Android.OS.Environment.IsExternalStorageManager)
                {
                    global::UABEA.Android.CrashLogger.SetPublicLogDir("/storage/emulated/0/logs");
                    global::UABEA.Android.CrashLogger.Log("OnActivityResult", "用户已授予 MANAGE_EXTERNAL_STORAGE，公共日志已切换到 /storage/emulated/0/logs");
                    global::UABEA.Android.CrashLogger.ToastShower?.Invoke("已获得所有文件访问权限，日志将写入 /storage/emulated/0/logs/");
                }
                else
                {
                    global::UABEA.Android.CrashLogger.Log("OnActivityResult", "用户未授予 MANAGE_EXTERNAL_STORAGE，继续使用应用私有目录");
                }
            }
        }
        catch (Exception ex)
        {
            global::UABEA.Android.CrashLogger.Log("OnActivityResult", ex);
        }
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        try
        {
            if (requestCode == RequestStoragePermId)
            {
                bool allGranted = grantResults.Length > 0;
                foreach (var r in grantResults)
                {
                    if (r != Permission.Granted) allGranted = false;
                }
                global::UABEA.Android.CrashLogger.Log("OnRequestPermResult", $"传统权限结果: allGranted={allGranted}");
            }
        }
        catch (Exception ex)
        {
            global::UABEA.Android.CrashLogger.Log("OnRequestPermResult", ex);
        }
    }

    private void ExtractClassData()
    {
        try
        {
            var path = Path.Combine(FilesDir!.AbsolutePath, "classdata.tpk");
            if (!File.Exists(path))
            {
                using var src = Assets!.Open("classdata.tpk");
                using var dst = File.Create(path);
                src.CopyTo(dst);
            }
            AppPaths.ClassDataPath = path;
            global::UABEA.Android.CrashLogger.Log("ExtractClassData", $"path={path}");
        }
        catch (Exception ex)
        {
            global::UABEA.Android.CrashLogger.Log("ExtractClassData", ex);
        }
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        global::UABEA.Android.CrashLogger.Log("CustomizeAppBuilder", "start");
        try
        {
            var b = base.CustomizeAppBuilder(builder).LogToTrace();
            global::UABEA.Android.CrashLogger.Log("CustomizeAppBuilder", "ok");
            return b;
        }
        catch (Exception ex)
        {
            global::UABEA.Android.CrashLogger.Log("CustomizeAppBuilder", ex);
            global::UABEA.Android.CrashLogger.CopyCrashToClipboard(
                global::UABEA.Android.CrashLogger.BuildCrashReport("CustomizeAppBuilder", ex));
            throw;
        }
    }
}
