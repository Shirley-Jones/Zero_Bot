using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Injector32
{
    class Program
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);
        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        public static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out UIntPtr lpNumberOfBytesWritten);
        [DllImport("kernel32.dll")]
        public static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, out IntPtr lpThreadId);

        static bool CheckToken(string t)
        {
            if (t == null) return false;
            var a = "Zero";
            var b = "Bot";
            var c = "_Auth_";
            return t.StartsWith(a + b + c) && t.Length > 20;
        }

        static int Main(string[] args)
        {
            try
            {
                // 不再读取 args 参数！别人在命令行输入什么都没用。

                // 设定读取超时。如果直接双击运行，不会一直卡着黑窗口，而是瞬间退出
                Console.SetIn(new StreamReader(Console.OpenStandardInput(), Encoding.UTF8, false, 1024));

                // 从隐形管道中读取主程序发来的指令
                string input = Console.ReadLine();
                if (string.IsNullOrEmpty(input)) return 99; // 没有收到指令，直接自毁退出

                // 解析数据格式： 令牌|PID|DLL路径
                string[] parts = input.Split('|');
                if (parts.Length != 3) return 98; // 格式不对，自毁退出

                string token = parts[0];
                string pidStr = parts[1];
                string dllPath = parts[2];

                // 校验握手令牌！只有主程序知道前缀是 ZeroBot_Auth_
                if (!CheckToken(token)) return 97; // 密码错误，自毁退出

                if (!int.TryParse(pidStr, out int pid)) return 96;

                // ============ 验证通过，开始执行核心注入逻辑 ============

                uint PROCESS_ALL_ACCESS = 0x001F0FFF;
                uint MEM_COMMIT_RESERVE = 0x3000;
                uint PAGE_READWRITE = 0x04;

                IntPtr hProcess = OpenProcess(PROCESS_ALL_ACCESS, false, pid);
                if (hProcess == IntPtr.Zero) return 3; // 获取句柄失败

                IntPtr loadLibraryAddr = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryA");
                if (loadLibraryAddr == IntPtr.Zero) return 4;

                byte[] bytes = Encoding.Default.GetBytes(dllPath + "\0");

                IntPtr allocMemAddress = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)bytes.Length, MEM_COMMIT_RESERVE, PAGE_READWRITE);
                if (allocMemAddress == IntPtr.Zero) return 5;

                WriteProcessMemory(hProcess, allocMemAddress, bytes, (uint)bytes.Length, out _);

                IntPtr hThread = CreateRemoteThread(hProcess, IntPtr.Zero, 0, loadLibraryAddr, allocMemAddress, 0, out _);
                if (hThread == IntPtr.Zero) return 6; // 线程创建失败

                return 0; // 成功返回 0
            }
            catch
            {
                return 100; // 发生任何未处理异常，直接返回错误码
            }
        }
    }
}