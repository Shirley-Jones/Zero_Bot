using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Zero.Core
{
    public static class MemoryAPI
    {
        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll")]
        public static extern bool ReadProcessMemory(IntPtr hProcess, int lpBaseAddress, byte[] lpBuffer, int dwSize, out int lpNumberOfBytesRead);

        // 🔥 新增：内存写入 API
        [DllImport("kernel32.dll")]
        public static extern bool WriteProcessMemory(IntPtr hProcess, int lpBaseAddress, byte[] lpBuffer, int nSize, out int lpNumberOfBytesWritten);

        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr hObject);

        // 🔥 修改：从只读权限改为拥有 “所有访问权限”（最高权限，才能写入内存）
        public const int PROCESS_ALL_ACCESS = 0x001F0FFF;

        // 🔥 新增：跨进程修改内存保护属性 API
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool VirtualProtectEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        public const uint PAGE_EXECUTE_READWRITE = 0x40;

        // ========================== 核心内存 Patch 方法 ==========================
        public static bool PatchMemory(IntPtr handle, int address, byte[] patchBytes)
        {
            uint oldProtect;
            // 1. 提升权限：将目标内存改为 可读可写可执行 (RWX)
            if (VirtualProtectEx(handle, (IntPtr)address, (UIntPtr)patchBytes.Length, PAGE_EXECUTE_READWRITE, out oldProtect))
            {
                // 2. 写入修改字节 (NOP)
                WriteProcessMemory(handle, address, patchBytes, patchBytes.Length, out _);

                // 3. 恢复原来的内存权限 (非常重要，保持内存整洁)
                VirtualProtectEx(handle, (IntPtr)address, (UIntPtr)patchBytes.Length, oldProtect, out _);
                return true;
            }
            return false;
        }

        // 专门用于屏蔽 1.12.1 客户端甩鼠标导致 CTM 丢失的补丁
        public static void FixCTMMouseShake(int pid, int moduleBaseAddress)
        {
            IntPtr hProcess = OpenProcess(PROCESS_ALL_ACCESS, false, pid);
            if (hProcess != IntPtr.Zero)
            {
                // 目标地址: WoW.exe + 0x210C6C
                int targetAddress = moduleBaseAddress + 0x210C6C;

                // 连续 11 个字节的 NOP (0x90)，覆盖 push 01, push 00, mov ecx,esi, call xxxxxxxx
                byte[] nopBytes = new byte[] { 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90 };

                PatchMemory(hProcess, targetAddress, nopBytes);
                CloseHandle(hProcess);
            }
        }

        // ========================== 读取方法 ==========================
        public static int ReadInteger(IntPtr handle, int address)
        {
            byte[] buffer = new byte[4];
            ReadProcessMemory(handle, address, buffer, 4, out _);
            return BitConverter.ToInt32(buffer, 0);
        }

        public static ulong ReadQword(IntPtr handle, int address)
        {
            byte[] buffer = new byte[8];
            ReadProcessMemory(handle, address, buffer, 8, out _);
            return BitConverter.ToUInt64(buffer, 0);
        }

        public static float ReadFloat(IntPtr handle, int address)
        {
            byte[] buffer = new byte[4];
            ReadProcessMemory(handle, address, buffer, 4, out _);
            return BitConverter.ToSingle(buffer, 0);
        }

        public static byte[] ReadBytes(IntPtr handle, int address, int length)
        {
            byte[] buffer = new byte[length];
            ReadProcessMemory(handle, address, buffer, length, out _);
            return buffer;
        }

        public static string ReadString(IntPtr handle, int address, int maxLength = 256)
        {
            byte[] buffer = ReadBytes(handle, address, maxLength);
            int len = 0;
            while (len < buffer.Length && buffer[len] != 0) len++;
            return Encoding.UTF8.GetString(buffer, 0, len);
        }

        // ========================== 补充：无符号读取 ==========================
        public static uint ReadUInt32(IntPtr handle, int address)
        {
            byte[] buffer = new byte[4];
            ReadProcessMemory(handle, address, buffer, 4, out _);
            return BitConverter.ToUInt32(buffer, 0);
        }

        // ========================== 写入方法 ==========================
        public static void WriteInteger(IntPtr handle, int address, int value)
        {
            WriteProcessMemory(handle, address, BitConverter.GetBytes(value), 4, out _);
        }

        public static void WriteFloat(IntPtr handle, int address, float value)
        {
            WriteProcessMemory(handle, address, BitConverter.GetBytes(value), 4, out _);
        }

        public static void WriteQword(IntPtr handle, int address, ulong value)
        {
            WriteProcessMemory(handle, address, BitConverter.GetBytes(value), 8, out _);
        }
    }
}