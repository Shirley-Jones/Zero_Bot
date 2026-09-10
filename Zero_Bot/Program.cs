using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using Zero.Core;

namespace Zero
{
    internal static class Program
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        private static readonly IntPtr HWND_BROADCAST = (IntPtr)0xffff;

        public static readonly int WM_SHOWME = RegisterWindowMessage("WM_SHOWME_9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d");

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (!IsAdministrator())
            {
                MessageBox.Show(
                    "权限不足，无法启动程序！\n\n请右键点击本程序，选择【以管理员身份运行】。",
                    "缺少管理员权限",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return;
            }

            bool createNew;
            using (Mutex mutex = new Mutex(true, "Global\\Zero_Shirley_9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d", out createNew))
            {
                if (createNew)
                {
                    // ★ 先读取配置，把日志开关打开
                    ConfigManager.LoadDebugLogSetting();

                    // ★ 然后再加载数据库，这样里面的 Logger 才能正常写入到本地 txt
                    ItemDbManager.LoadDatabase();
                    // 这时登录界面已经安全关闭并在内存中释放，主界面正式接管进程
                    Application.Run(new Main());
                }
                else
                {
                    // 多开拦截：向系统中已经在运行的那个实例发广播
                    PostMessage(HWND_BROADCAST, WM_SHOWME, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
            }
        }

        private static bool IsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }
}