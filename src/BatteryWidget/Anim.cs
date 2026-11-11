using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BatteryWidget;

/// <summary>动画公共参数：60fps 的时间基准、缓动曲线与系统计时器精度提升。</summary>
internal static class Anim
{
    /// <summary>目标帧间隔（约 60fps）。</summary>
    public const int FrameIntervalMs = 12;

    /// <summary>定时器节拍：1ms 唤醒一次，靠 Stopwatch 节流到 60fps，比 WM_TIMER 的 15.6ms 精度高。</summary>
    public const int TickMs = 1;

    private static int _precisionUsers;

    /// <summary>高精度当前时间（毫秒）。</summary>
    public static double NowMs => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

    public static float EaseOutCubic(float t) => 1f - MathF.Pow(1f - t, 3f);

    /// <summary>
    /// 把系统计时器精度提到 1ms，否则 WM_TIMER 默认 15.6ms 会让 60fps 变成 30fps。
    /// 引用计数，动画全部结束才恢复。
    /// </summary>
    public static void BeginFramePrecision()
    {
        if (_precisionUsers++ == 0)
            timeBeginPeriod(1);
    }

    public static void EndFramePrecision()
    {
        if (_precisionUsers > 0 && --_precisionUsers == 0)
            timeEndPeriod(1);
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint timeBeginPeriod(uint period);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint timeEndPeriod(uint period);
}
