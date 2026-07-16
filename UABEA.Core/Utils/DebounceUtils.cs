using System;
using System.Threading;
using System.Threading.Tasks;

namespace UABEAvalonia
{
    /// <summary>
    /// 简易防抖工具：在指定窗口内多次调用时，只有最后一次会真正执行。
    /// 用于配置项变更后延迟写盘，避免每次属性变更都触发同步 IO。
    /// 实现参考 UABEANext 的 DebounceUtils，使用 CancellationTokenSource 取消未触发的执行。
    /// </summary>
    public static class DebounceUtils
    {
        public static Action<T> Debounce<T>(Action<T> func, int milliseconds)
        {
            CancellationTokenSource? cts = null;

            return arg =>
            {
                try
                {
                    cts?.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // 上一次的 token 已执行完毕被 dispose，忽略
                }

                cts = new CancellationTokenSource();

                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(milliseconds, cts.Token);
                        func(arg);
                    }
                    catch (TaskCanceledException)
                    {
                        // 被新调用取消，正常情况
                    }
                }, cts.Token);
            };
        }
    }
}
