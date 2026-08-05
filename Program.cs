using System;
using System.Threading;
using System.Windows.Forms;

namespace ccd_helper
{
    internal static class Program
    {
        internal const string LOCAL_SOFTWARE_VERSION = "2026.08.05";

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main()
        {
            SetProcessDPIAware();

            const string mutexName = "Global\\796796797";
            bool createdNew;
            using (Mutex mutex = new Mutex(true, mutexName, out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("程序已经在运行中，请勿重复打开！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // 1. 显示等待窗口（内部异步执行非静默云同步）
                using (var waiting = new WaitingForm())
                {
                    Application.Run(waiting);
                }

                // 2. 统一有效性检查（版本、License、有效期）
                if (!LicenseValidator.ValidateAll(LOCAL_SOFTWARE_VERSION))
                    return;   // 失败则退出

                // 3. 启动主窗体
                Application.Run(new Form1());
            }
        }
    }
}