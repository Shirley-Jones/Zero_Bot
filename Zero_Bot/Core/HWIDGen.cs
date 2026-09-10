using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Zero.Core
{
    public class HWIDGen
    {
        /// <summary>
        /// 获取最终的唯一设备码
        /// </summary>
        [Obfuscation(Feature = "ultra", Exclude = false)]
        public static string GetDeviceID()
        {
            // 1. 获取四大件的物理序列号
            string cpu = GetCPUId();
            string board = GetMotherboardId();
            string disk = GetDiskId(); // 已排除 USB 外设
            string ram = GetRamId();

            // 2. 将它们拼接在一起
            string rawHWID = cpu + board + disk + ram;

            // 3. 计算 MD5 哈希，让它变成一个整齐好看的字符串
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(rawHWID));
                string hex = BitConverter.ToString(hash).Replace("-", "");

                // 格式化输出
                return $"{hex.Substring(0, 8)}{hex.Substring(8, 8)}";
            }
        }

        private static string GetCPUId()
        {
            try
            {
                ManagementClass mc = new ManagementClass("Win32_Processor");
                foreach (ManagementObject mo in mc.GetInstances())
                {
                    return mo["ProcessorId"]?.ToString().Trim() ?? "";
                }
            }
            catch { }
            return "CPU_UNKNOWN";
        }

        private static string GetMotherboardId()
        {
            try
            {
                ManagementClass mc = new ManagementClass("Win32_BaseBoard");
                foreach (ManagementObject mo in mc.GetInstances())
                {
                    return mo["SerialNumber"]?.ToString().Trim() ?? "";
                }
            }
            catch { }
            return "BOARD_UNKNOWN";
        }

        private static string GetDiskId()
        {
            try
            {
                // 核心细节：使用 WQL 查询语句，明确排除 InterfaceType = 'USB' 的硬盘
                ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_DiskDrive WHERE InterfaceType != 'USB'");
                foreach (ManagementObject mo in searcher.Get())
                {
                    // 只抓取找到的第一块非 USB 硬盘（通常是系统盘）
                    return mo["SerialNumber"]?.ToString().Trim() ?? "";
                }
            }
            catch { }
            return "DISK_UNKNOWN";
        }

        private static string GetRamId()
        {
            string ramIds = "";
            try
            {
                ManagementClass mc = new ManagementClass("Win32_PhysicalMemory");
                foreach (ManagementObject mo in mc.GetInstances())
                {
                    // 电脑可能有多根内存条，把所有内存条的序列号拼起来
                    // 这样只要拔掉或更换任意一根，机器码就会变
                    ramIds += mo["SerialNumber"]?.ToString().Trim() ?? "";
                }
                return ramIds;
            }
            catch { }
            return "RAM_UNKNOWN";
        }
    }
}
