using System.Threading;

namespace BatteryWidget;

internal static class Program
{
    [STAThread]
            private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // 调试用：直接打开设备选择窗口
        if (args.Contains("--select"))
        {
            Application.Run(new DeviceSelectionForm(Array.Empty<string>()));
            return;
        }

        using var mutex = new Mutex(true, "BatteryWidget.2.4G.SingleInstance", out var created);
        if (!created)
        {
            MessageBox.Show("程序已经在运行，请查看系统托盘。", "2.4G Battery",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.Run(new MainForm());
    }
}





