using System;
using System.Diagnostics;

namespace Zero.Core
{
    /// <summary>
    /// 跨进程/跨程序集握手通信中心 (带频道隔离)
    /// </summary>
    public static class IPCManager
    {
        // 辅助方法：动态拼接隔离键值
        private static string GetKey(string baseKey, string channelId) => $"{baseKey}_{channelId}";

        public static bool IsResetRequestPending(string channelId)
        {
            object val = AppDomain.CurrentDomain.GetData(GetKey("IPC_ResetRequestPending", channelId));
            return val != null && (bool)val;
        }

        public static bool IsResetCompleted(string channelId)
        {
            object val = AppDomain.CurrentDomain.GetData(GetKey("IPC_ResetCompleted", channelId));
            return val != null && (bool)val;
        }

        // 传入频道ID（建议使用队长名字作为 channelId）
        public static void RequestReset(string channelId)
        {
            AppDomain.CurrentDomain.SetData(GetKey("IPC_ResetCompleted", channelId), false);
            AppDomain.CurrentDomain.SetData(GetKey("IPC_ResetRequestPending", channelId), true);
            Logger.Write($"[通讯系统] 刷本号已向频道[{channelId}]发出退出队伍请求！");
        }

        public static void MarkResetComplete(string channelId)
        {
            AppDomain.CurrentDomain.SetData(GetKey("IPC_ResetRequestPending", channelId), false);
            AppDomain.CurrentDomain.SetData(GetKey("IPC_ResetCompleted", channelId), true);
            Logger.Write($"[通讯系统] 频道[{channelId}]重置号已完成任务，通知刷本号上线！");
        }

        public static void ClearSignals(string channelId)
        {
            AppDomain.CurrentDomain.SetData(GetKey("IPC_ResetRequestPending", channelId), false);
            AppDomain.CurrentDomain.SetData(GetKey("IPC_ResetCompleted", channelId), false);
        }

        // ================= 死亡重置同理 =================

        public static bool IsDeathResetRequestPending(string channelId)
        {
            object val = AppDomain.CurrentDomain.GetData(GetKey("IPC_DeathResetRequestPending", channelId));
            return val != null && (bool)val;
        }
        public static bool IsDeathResetCompleted(string channelId)
        {
            object val = AppDomain.CurrentDomain.GetData(GetKey("IPC_DeathResetCompleted", channelId));
            return val != null && (bool)val;
        }

        public static void RequestDeathReset(string channelId)
        {
            AppDomain.CurrentDomain.SetData(GetKey("IPC_DeathResetCompleted", channelId), false);
            AppDomain.CurrentDomain.SetData(GetKey("IPC_DeathResetRequestPending", channelId), true);
        }

        public static void MarkDeathResetComplete(string channelId)
        {
            AppDomain.CurrentDomain.SetData(GetKey("IPC_DeathResetRequestPending", channelId), false);
            AppDomain.CurrentDomain.SetData(GetKey("IPC_DeathResetCompleted", channelId), true);
        }

        public static void ClearDeathSignals(string channelId)
        {
            AppDomain.CurrentDomain.SetData(GetKey("IPC_DeathResetRequestPending", channelId), false);
            AppDomain.CurrentDomain.SetData(GetKey("IPC_DeathResetCompleted", channelId), false);
        }
    }
}