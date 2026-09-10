using System;
using System.Diagnostics;
using System.IO;

namespace Zero.Core
{
    public static class Logger
    {
        // 使用属性动态获取路径，避免静态变量初始化时的顺序问题
        private static string LogDirectory => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");

        // 线程锁
        private static readonly object _lockObj = new object();

        // ❌ 删除了危险的 static Logger() 静态构造函数

        /// <summary>
        /// 核心打印方法（已做绝对防崩溃处理）
        /// </summary>
        public static void Write(string message, bool isFatal = false)
        {
            // 将整个方法体包裹在 try-catch 中，哪怕系统炸了，日志也坚决不抛出异常
            try
            {
                string timeStamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string formattedMsg = $"[{timeStamp}] {message}";

                // 永远输出到 VS 调试窗口
                Debug.WriteLine(formattedMsg);

                // 如果没开日志，直接返回（安全退出）
                if (!ConfigManager.DebugLogEnabled)
                    return;

                lock (_lockObj)
                {
                    // 把创建目录移到这里，且在 try 块内部，安全无比
                    if (!Directory.Exists(LogDirectory))
                    {
                        Directory.CreateDirectory(LogDirectory);
                    }

                    // 写入日常 Debug 日志
                    string dailyLogFile = Path.Combine(LogDirectory, $"DebugLog_{DateTime.Now:yyyy-MM-dd}.txt");
                    File.AppendAllText(dailyLogFile, formattedMsg + Environment.NewLine);

                    // 写入死亡/重要事件日志
                    if (isFatal || message.Contains("死亡") || message.Contains("阵亡")
                              || message.Contains("释放灵魂") || message.Contains("复活") || message.Contains("存活"))
                    {
                        string deathLogFile = Path.Combine(LogDirectory, $"DeathLog_{DateTime.Now:yyyy-MM-dd}.txt");
                        File.AppendAllText(deathLogFile, formattedMsg + Environment.NewLine);
                    }
                }
            }
            catch
            {
                // 绝对吞掉一切异常。哪怕 C 盘满了、文件被别的软件死锁锁定，程序依然能正常运行！
            }
        }
    }
}