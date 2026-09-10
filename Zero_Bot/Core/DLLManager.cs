using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Zero.Core
{

    // ========================================================
    //   跨境内存对齐结构体（必须与 C++ DLL 保持绝对 1 字节对齐）
    // ========================================================
    [StructLayout(LayoutKind.Sequential, Pack = 1)]



    public struct Vector3
    {
        public float X;
        public float Y;
        public float Z;
    }

    // 1. 定义移动精度类型
    public enum PathType
    {
        Rough,   // 粗略切弯 (适用于野外大直道，容错 7.0f)
        Precise  // 精确贴脸 (适用于进门、绕桌子、终点，容错 1.0f)
    }

    // 2. 定义包含精度属性的高级航点
    public struct Waypoint
    {
        public float X;
        public float Y;
        public float Z;
        public PathType Type; // 精确或粗略

        // 快捷方法：把当前航点无损降维成底层需要的 Vector3
        public Vector3 ToVector3()
        {
            return new Vector3 { X = this.X, Y = this.Y, Z = this.Z };
        }
    }


    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SharedMemoryData
    {
        public int SecurityToken;    // 对应 C++ 的动态令牌
        public int commandType;      // 0=空闲, 1=Spell, 2=Move_To, 3=RightClick
        public uint myPlayerBase;    // 玩家对象本地基址参数
        public int ctmActionId;      // CTM 动作ID (移动通常为 4)
        public ulong targetGuid;     // 目标 GUID 参数
        public Vector3 dest;         // 目标 XYZ 坐标参数
        public uint targetBase;      // 右键交互目标基址参数
        public int isCompleted;      // 状态：0=执行中, 1=已完成
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10240)]
        public byte[] luaPayload;    // 在 C# 中对应 C++ 的 char luaPayload[1024]

    }
    public class InventoryItem
    {
        public ulong Guid { get; set; }
        public int BaseAddress { get; set; }
        public int ItemId { get; set; }
        public int StackCount { get; set; }
        public int BagId { get; set; }
        public int SlotId { get; set; } // 注意：这里直接存 Lua 用的从 1 开始的格子 ID
        public bool IsSoulbound { get; set; }
    }

    public class Entry_ID_List
    {
        public int BaseAddress { get; set; }
        public ulong Guid { get; set; }
        public int EntryId { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
    }

    public abstract class DLLManager : IDisposable
    {
        // ========================== Win32 API 引入 ==========================
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        //引入 Windows 底层开机时钟，和魔兽世界底层算法保持 100% 绝对一致！
        [DllImport("kernel32.dll")]

        public static extern uint GetTickCount();

        // 在 DLLManager.cs 的 Win32 API 引入区加上这个：
        private const uint WM_MOUSEMOVE = 0x0200;
        private const uint WM_WAKEUP_LUA = 0x0400 + 1337;

        // ========================== 公共属性区 ==========================
        public Main MainForm { get; set; }
        public int PID { get; set; }
        public int ModuleBaseAddress { get; set; }
        public abstract string ScriptName { get; }
        public string AccountName { get; set; }
        public string PlayerName { get; set; }
        // 🌟 核心控制权：告诉主程序是否要挂起自动登录
        public bool PauseGlobalAutoLogin { get; set; } = false;

        // ========================== IPC 内部私有资源 ==========================
        public MemoryMappedFile _sharedMem = null;
        public MemoryMappedViewAccessor _memAccessor = null;
        public IntPtr _gameWindowHandle = IntPtr.Zero;

        // ========================== 全局上下文变量 ==========================
        protected string _cachedPlayerName = string.Empty;

        // 🌟 在你的类（比如 DLLManager 或 Movement 模块）的成员变量区，添加这个全局时间戳
        private static DateTime _lastCtmTime = DateTime.MinValue;

        // 全局通用属性：无论在基类、子类还是行为树节点里，都能直接读取它！
        public string CurrentPlayerName
        {
            get { return string.IsNullOrEmpty(_cachedPlayerName) ? "未知角色" : _cachedPlayerName; }
            set { _cachedPlayerName = value; }
        }

        public DateTime _pauseAntiBotUntil = DateTime.MinValue;  // 用于在关键操作（如引怪、读条）时暂时屏蔽防检测平移
        /// <summary>
        /// 懒加载初始化专属本进程的共享内存通道与窗口句柄
        /// </summary>
        public void InitializeIPC()
        {
            if (_sharedMem == null)
            {
                try
                {
                    string customSharedMemName = $"WoW_Bot_SharedMem_{PID}";
                    // 开启 64 字节安全安全视图，容纳 44 字节的完整指令结构体
                    _sharedMem = MemoryMappedFile.CreateOrOpen(customSharedMemName, 15000);
                    _memAccessor = _sharedMem.CreateViewAccessor();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[IPC 初始化失败] PID: {PID}, 原因: {ex.Message}");
                }
            }

            // 🌟 核心破局点：把抓取句柄独立出来！
            // 只要发现句柄是空的（上次没抓到），这次必须重新抓！绝不死锁！
            if (_gameWindowHandle == IntPtr.Zero)
            {
                try
                {
                    Process p = Process.GetProcessById(PID);
                    if (p != null)
                    {
                        p.Refresh(); // 强行刷新一下进程状态
                        _gameWindowHandle = p.MainWindowHandle;
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// 将整块指令结构体无脑写入共享内存并投递消息（非unsafe纯安全版）
        /// </summary>
        public void SendDllCommand(SharedMemoryData data)
        {
            InitializeIPC();
            if (_memAccessor == null || _gameWindowHandle == IntPtr.Zero) return;

            // 1. 先把 C++ 底层生成的动态令牌读出来！
            int currentToken = _memAccessor.ReadInt32(0);
            data.SecurityToken = currentToken;

            // 🌟 致命防御锁：如果是普通的指令（比如施法、移动），你可能没给 luaPayload 赋值
            // 但在非 unsafe 方案下，哪怕是空数组也必须 new 出来，否则 Marshal 会报空指针！
            if (data.luaPayload == null)
            {
                data.luaPayload = new byte[10240];
            }

            // 2. 🌟 整个结构体一次性写入（Marshal 手动序列化安全版）
            int size = Marshal.SizeOf(typeof(SharedMemoryData));
            IntPtr ptr = Marshal.AllocHGlobal(size); // 在内存中申请空地

            try
            {
                // 将包含引用(byte[])的结构体“压平”到刚刚申请的内存中
                Marshal.StructureToPtr(data, ptr, false);

                // 准备一个干净的 byte 数组来接收压平后的纯数据
                byte[] rawBytes = new byte[size];
                Marshal.Copy(ptr, rawBytes, 0, size);

                // 💥 终极替换：用 WriteArray 写入纯字节流！完美绕过 Write<T> 的限制！
                _memAccessor.WriteArray(0, rawBytes, 0, size);
            }
            finally
            {
                // 必须释放申请的内存，杜绝内存泄漏
                Marshal.FreeHGlobal(ptr);
            }

            // 3. 通知注入到魔兽内部的 DLL 挂钩函数去执行
            PostMessage(_gameWindowHandle, WM_WAKEUP_LUA, (IntPtr)currentToken, IntPtr.Zero);
        }

        // ========================================================
        //       高级封装 API：供各副本具体子脚本类直接调用
        // ========================================================

        /// <summary>
        /// 驱动 DLL 直接 CALL 游戏内部的 Click-To-Move (CTM) 引擎寻路 (带硬件级防封节流)
        /// </summary>
        public bool MoveTo(IntPtr hProcess, int playerBase, float targetX, float targetY, float targetZ, float stopDistance = 2.0f)
        {
            // [重构优化]：基类内部直接读取当前坐标，无需子脚本重复读取
            float myX = MemoryAPI.ReadFloat(hProcess, playerBase + 0x9B8);
            float myY = MemoryAPI.ReadFloat(hProcess, playerBase + 0x9BC);

            double distance = Math.Sqrt(Math.Pow(myX - targetX, 2) + Math.Pow(myY - targetY, 2));
            if (distance <= stopDistance)
            {
                return true; // 已经到达目的地节点
            }

            // 读取 CTM 状态，防止主线程定时器高频重复发包导致角色在原地“鬼畜”
            int currentCtmState = MemoryAPI.ReadInteger(hProcess, ModuleBaseAddress + 0x84D888);
            float ctmDestX = MemoryAPI.ReadFloat(hProcess, ModuleBaseAddress + 0x84D890);
            float ctmDestY = MemoryAPI.ReadFloat(hProcess, ModuleBaseAddress + 0x84D894);

            bool isInterrupted = (currentCtmState != 4) ||
                                 (Math.Abs(ctmDestX - targetX) > 1.0f) ||
                                 (Math.Abs(ctmDestY - targetY) > 1.0f);

            // 如果 CTM 点丢失，准备发包补救
            if (isInterrupted)
            {
                // ==========================================
                // 🛡️ 核心防封节流阀：限制发包频率
                // 距离上一次真正在内存中触发 CTM，必须超过 333 毫秒 (即 1 秒最多发 3 次)
                // 哪怕 CTM 光点在 333 毫秒内闪烁/消失了 20 次，也绝不补发！
                // ==========================================
                if ((DateTime.Now - _lastCtmTime).TotalMilliseconds > 333.0)
                {
                    SharedMemoryData command = new SharedMemoryData
                    {
                        commandType = 1, // 1 = Move_To 寻路指令
                        myPlayerBase = (uint)playerBase,
                        ctmActionId = 4, // 4 = 游戏内部常规移动到点
                        targetGuid = 0,
                        dest = new Vector3 { X = targetX, Y = targetY, Z = targetZ },
                        isCompleted = 0
                    };

                    SendDllCommand(command);

                    // 刷新发包时间戳！
                    _lastCtmTime = DateTime.Now;
                }
            }

            return false; // 还在路上
        }

        /// <summary>
        /// 单次触发 CTM（只发包，绝对不进行高频坐标比对纠正），专用于上下船进出副本！
        /// </summary>
        public void MoveToOneShot(int playerBase, float targetX, float targetY, float targetZ)
        {
            SharedMemoryData command = new SharedMemoryData
            {
                commandType = 1, // 1 = Move_To 寻路指令
                myPlayerBase = (uint)playerBase,
                ctmActionId = 4, // 4 = 走过去
                targetGuid = 0,
                dest = new Vector3 { X = targetX, Y = targetY, Z = targetZ },
                isCompleted = 0
            };
            SendDllCommand(command);
        }

        /// <summary>
        /// 强制打断 CTM 自动寻路，让角色立刻原地刹车
        /// </summary>
        public void StopMovement(IntPtr hProcess, int playerBase)
        {
            // 【核心修复】：读取玩家当前的真实坐标，绝对不能传 (0,0,0)！
            float myX = MemoryAPI.ReadFloat(hProcess, playerBase + 0x9B8);
            float myY = MemoryAPI.ReadFloat(hProcess, playerBase + 0x9BC);
            float myZ = MemoryAPI.ReadFloat(hProcess, playerBase + 0x9C0);

            SharedMemoryData command = new SharedMemoryData
            {
                commandType = 1,
                myPlayerBase = (uint)playerBase,
                ctmActionId = 3, // 3 = CTM_STOP 
                targetGuid = 0,
                // 把当前坐标传进去，距离计算等于 0，绝不会再报距离太远！
                dest = new Vector3 { X = myX, Y = myY, Z = myZ },
                isCompleted = 0
            };
            SendDllCommand(command);
        }


        /// <summary>
        /// 终极发包锁头：利用 CTM 引擎，瞬间面朝指定的实体 (GUID) 并向服务器同步
        /// </summary>
        public void SetFacingByGuid(int playerBase, ulong targetGuid)
        {
            SharedMemoryData command = new SharedMemoryData
            {
                commandType = 1,           // 依然是 CTM 寻路大类指令
                myPlayerBase = (uint)playerBase,
                ctmActionId = 1,           // 1 = CTM_FACE_TARGET (面朝 GUID)
                targetGuid = targetGuid,   // 把目标的 GUID 塞进去！
                dest = new Vector3 { X = 0, Y = 0, Z = 0 }, // 坐标填0，因为 Action 1 只认 GUID，不看坐标！
                isCompleted = 0
            };
            SendDllCommand(command);
        }


        /// <summary>
        /// 驱动 DLL 直接 CALL 游戏内部的右键点击函数（支持自动拾取、对话、交接任务）
        /// </summary>
        public void RightClickUnit(uint targetBase)
        {
            SharedMemoryData command = new SharedMemoryData
            {
                commandType = 2, // 2 = RightClick 右键交互
                targetBase = targetBase,
                isCompleted = 0
            };
            SendDllCommand(command);
        }

        /// <summary>
        /// 驱动 DLL 直接 CALL 游戏内部的 GameObject 右键点击（专开机关、门、宝箱、矿草）
        /// </summary>
        public void RightClickGameObject(uint targetBase)
        {
            SharedMemoryData command = new SharedMemoryData
            {
                commandType = 3, // 3 = GameObject 专属右键交互
                targetBase = targetBase,
                isCompleted = 0
            };
            SendDllCommand(command);
        }


        public void CastGroundAOE(string spellname, float destX, float destY, float destZ)
        {
            // 1. 发送施法指令，让游戏底层产生绿圈，标志位置 0x40
            //CastSpell(spellname);
            ExecuteDynamicLua($"CastSpellByName('{spellname}');");

            // 2. 必须稍等几十毫秒，让魔兽的主线程把绿圈状态挂载好
            System.Threading.Thread.Sleep(50);

            // 3. 发送 commandType = 4 指令，CALL C++ 里的 HandleTerrainClick
            SharedMemoryData command = new SharedMemoryData
            {
                commandType = 4,
                dest = new Vector3 { X = destX, Y = destY, Z = destZ },
                isCompleted = 0
            };
            SendDllCommand(command);
        }

        /// <summary>
        /// 封装一个极其强大的动态 Lua 执行接口
        /// </summary>
        public void ExecuteDynamicLua(string luaCode)
        {
            // 1. 组装数据
            SharedMemoryData command = new SharedMemoryData();
            command.commandType = 5;
            command.isCompleted = 0;
            command.luaPayload = new byte[10240]; // 必须实例化

            // 2. 转换并填装 UTF-8 字节
            byte[] utf8Bytes = System.Text.Encoding.UTF8.GetBytes(luaCode);
            int copyLength = Math.Min(utf8Bytes.Length, 10230);
            Array.Copy(utf8Bytes, command.luaPayload, copyLength);
            command.luaPayload[copyLength] = 0; // 封口符

            // 3. 直接交给升级后的神级通用发送器！
            SendDllCommand(command);
        }


        /// <summary>
        /// 将鼠标上挂起的法术（如大型爆盐炸弹）直接应用于指定的 GUID 目标
        /// </summary>
        public void ExecuteSpellTargetGuid(ulong targetGuid)
        {
            SharedMemoryData command = new SharedMemoryData
            {
                commandType = 6, // 6 = 触发 DLL 内部的 将鼠标上挂起的法术直接应用于指定的 GUID 目标 逻辑
                targetGuid = targetGuid,    // 传入targetGuid
                isCompleted = 0
            };

            SendDllCommand(command);
        }

        /// <summary>
        /// 按键模拟区域
        /// </summary>
        /// // 在 WM_WAKEUP_LUA 下面补充这三个按键相关的常量
        public const uint WM_KEYDOWN = 0x0100;
        public const uint WM_KEYUP = 0x0101;
        public const int VK_SPACE = 0x20; // 空格键的虚拟键码
        public const int VK_Number_Zero = 0x30; // 英文上方0键的虚拟键码
        public const int VK_Enter = 0x0D; // 回车键的虚拟键码

        // 👇 新增的 Q 和 E 键虚拟键码
        public const int VK_Q = 0x51; // Q键 (默认左平移)
        public const int VK_E = 0x45; // E键 (默认右平移)


        public async Task Q_keyboard(int waiting_time)
        {
            InitializeIPC();
            // 向魔兽窗口投递Q键按下和抬起的硬件消息，用于触发防封清零
            if (_gameWindowHandle != IntPtr.Zero)
            {
                PostMessage(_gameWindowHandle, WM_KEYDOWN, (IntPtr)VK_Q, IntPtr.Zero);

                // 异步等待时间 单位：毫秒，在此期间把控制权交还给 UI 和主程序
                await Task.Delay(waiting_time);

                PostMessage(_gameWindowHandle, WM_KEYUP, (IntPtr)VK_Q, IntPtr.Zero);
            }
        }

        public async Task E_keyboard(int waiting_time)
        {
            InitializeIPC();
            // 向魔兽窗口投递E键按下和抬起的硬件消息，用于触发防封清零
            if (_gameWindowHandle != IntPtr.Zero)
            {
                PostMessage(_gameWindowHandle, WM_KEYDOWN, (IntPtr)VK_E, IntPtr.Zero);

                // 异步等待时间 单位：毫秒，在此期间把控制权交还给 UI 和主程序
                await Task.Delay(waiting_time);

                PostMessage(_gameWindowHandle, WM_KEYUP, (IntPtr)VK_E, IntPtr.Zero);
            }
        }

        public async Task Jump(int waiting_time)
        {
            InitializeIPC();
            // 向魔兽窗口投递空格键按下和抬起的硬件消息
            if (_gameWindowHandle != IntPtr.Zero)
            {
                PostMessage(_gameWindowHandle, WM_KEYDOWN, (IntPtr)VK_SPACE, IntPtr.Zero);
                // 异步等待时间 单位：毫秒，在此期间把控制权交还给 UI 和主程序
                await Task.Delay(waiting_time);
                PostMessage(_gameWindowHandle, WM_KEYUP, (IntPtr)VK_SPACE, IntPtr.Zero);
            }
        }

        public async Task Use_Mount(int waiting_time)
        {
            InitializeIPC();
            // 向魔兽窗口投递空格键按下和抬起的硬件消息
            if (_gameWindowHandle != IntPtr.Zero)
            {
                PostMessage(_gameWindowHandle, WM_KEYDOWN, (IntPtr)VK_Number_Zero, IntPtr.Zero);
                // 异步等待时间 单位：毫秒，在此期间把控制权交还给 UI 和主程序
                await Task.Delay(waiting_time);
                PostMessage(_gameWindowHandle, WM_KEYUP, (IntPtr)VK_Number_Zero, IntPtr.Zero);
            }
        }

        public async Task Enter_keyboard(int waiting_time)
        {
            InitializeIPC();
            // 向魔兽窗口投递空格键按下和抬起的硬件消息
            if (_gameWindowHandle != IntPtr.Zero)
            {
                PostMessage(_gameWindowHandle, WM_KEYDOWN, (IntPtr)VK_Enter, IntPtr.Zero);
                // 异步等待时间 单位：毫秒，在此期间把控制权交还给 UI 和主程序
                await Task.Delay(waiting_time);
                PostMessage(_gameWindowHandle, WM_KEYUP, (IntPtr)VK_Enter, IntPtr.Zero);
            }
        }

        /// <summary>
        /// 获取游戏单位(玩家/宠物/怪物/NPC)的行为状态，返回 bool 值
        /// </summary>
        public static bool Unit_Behavioral_State(IntPtr hProcess, int unitBase, int bitPosition)
        {
            // 兜底防错
            if (unitBase == 0) return false;

            // 读取属性基址 (Descriptor Fields)
            int descriptors = MemoryAPI.ReadInteger(hProcess, unitBase + 0x08);
            if (descriptors == 0) return false;

            // 读取单位状态标志集合 (UNIT_FIELD_FLAGS)
            int flags = MemoryAPI.ReadInteger(hProcess, descriptors + 0xB8);

            // 位运算比对，真为 1，假为 0
            return (flags & (1 << bitPosition)) != 0;
        }

        /// <summary>
        /// 获取游戏单位(玩家/宠物/怪物/NPC)的动态状态，返回 bool 值
        /// </summary>
        public static bool Unit_Dynamic_State(IntPtr hProcess, int unitBase, int bitPosition)
        {
            // 兜底防错
            if (unitBase == 0) return false;

            // 读取属性基址 (Descriptor Dynamic)
            int descriptors = MemoryAPI.ReadInteger(hProcess, unitBase + 0x08);
            if (descriptors == 0) return false;

            // 读取单位状态标志集合 (UNIT_Dynamic_FLAGS)
            int flags = MemoryAPI.ReadInteger(hProcess, descriptors + 0x23C);

            // 位运算比对，真为 1，假为 0
            return (flags & (1 << bitPosition)) != 0;
        }



        /// <summary>
        /// 核心功能函数：去内存查户口，直接返回当前角色的阵营名称
        /// </summary>
        public string GetPlayerFaction(IntPtr hProcess, int playerBase)
        {
            int descriptors = MemoryAPI.ReadInteger(hProcess, playerBase + 0x08);
            // 防错拦截：如果是 0 说明角色还没加载出来（在蓝条里）
            if (descriptors == 0) return "未知";

            // 读取 0x90 偏移的 4 个字节，第 0 位就是种族 ID
            byte[] bytes = MemoryAPI.ReadBytes(hProcess, descriptors + 0x90, 4);
            int raceId = bytes[0];

            // 兽人=2, 亡灵=5, 牛头人=6, 巨魔=8
            if (raceId == 2 || raceId == 5 || raceId == 6 || raceId == 8)
            {
                return "部落";
            }
            else
            {
                // 人类=1, 矮人=3, 暗夜精灵=4, 侏儒=7
                return "联盟";
            }
        }

        /// <summary>
        /// 核心功能函数：去内存查户口，获取玩家职业ID
        /// </summary>
        public static int GetPlayerClass(IntPtr hProcess, int playerBase)
        {
            if (playerBase == 0) return 0;

            // 1. 读取属性基址 (descriptors) -> 偏移 0x08
            int descriptors = MemoryAPI.ReadInteger(hProcess, playerBase + 0x08);
            if (descriptors == 0) return 0;

            // 2. 读取 UNIT_FIELD_BYTES_0 -> 偏移 0x90，长度 4 字节
            byte[] byte0 = MemoryAPI.ReadBytes(hProcess, descriptors + 0x90, 4);

            if (byte0 != null && byte0.Length >= 4)
            {
                // 对应的 C# 字节位 (0-based):
                // byte0[0] = 种族 (Race)
                // byte0[1] = 职业 (Class)
                // byte0[2] = 性别 (Gender)
                // byte0[3] = 能量类型 (PowerType)
                return byte0[1];
            }

            return 0; // 读取失败或未知职业
        }


        // ==========================================
        // 获取玩家当前等级
        // ==========================================
        public static int GetPlayerLevel(IntPtr hProcess, int playerBase)
        {
            if (playerBase == 0) return 0;

            // 1. 读取属性基址 (descriptors) -> 偏移 0x08
            int descriptors = MemoryAPI.ReadInteger(hProcess, playerBase + 0x08);
            if (descriptors == 0) return 0;

            // 2. 读取等级 (Level) -> 偏移 0x88
            int level = MemoryAPI.ReadInteger(hProcess, descriptors + 0x88);

            return level;
        }

        /// <summary>
        /// 修改角色朝向
        /// </summary>
        public void SetFacing(IntPtr hProcess, int playerBase, float facing)
        {
            MemoryAPI.WriteFloat(hProcess, playerBase + 0x9C4, facing);
        }


        /// <summary>
        /// 强制修改内存选中目标
        /// </summary>
        public void Select_Target(IntPtr hProcess, ulong guid)
        {
            // 写入 8 字节的 GUID 到目标地址
            MemoryAPI.WriteQword(hProcess, ModuleBaseAddress + 0x74E2D8, guid);
        }

        /// <summary>
        /// 遍历对象管理器，根据 GUID 查找目标在内存中的动态基址 (targetBase)
        /// </summary>
        public static int GetTargetBaseByGuid(IntPtr hProcess, ulong targetGuid)
        {
            int objMgr = MemoryAPI.ReadInteger(hProcess, 0x00B41414);
            if (objMgr == 0) return 0;

            int currentObj = MemoryAPI.ReadInteger(hProcess, objMgr + 0xAC);

            while (currentObj != 0)
            {
                ulong guid = MemoryAPI.ReadQword(hProcess, currentObj + 0x30);
                if (guid == targetGuid)
                {
                    return currentObj; // 找到了！返回怪物动态基址
                }
                currentObj = MemoryAPI.ReadInteger(hProcess, currentObj + 0x3C); // 找下一个
            }
            return 0; // 没找到（可能离得太远没刷出来，或者死了）
        }

        /// <summary>
        /// 内存检索：根据 EntryID 获取指定游戏对象(机关/门)的 GUID
        /// </summary>
        public ulong GetGameObjectGuidByEntry(IntPtr hProcess, int targetEntryId)
        {
            int objMgr = MemoryAPI.ReadInteger(hProcess, 0x00B41414);
            if (objMgr == 0) return 0;

            int currentObj = MemoryAPI.ReadInteger(hProcess, objMgr + 0xAC);
            while (currentObj != 0)
            {
                int objType = MemoryAPI.ReadInteger(hProcess, currentObj + 0x14);
                if (objType == 5) // 5 = GameObject
                {
                    int desc = MemoryAPI.ReadInteger(hProcess, currentObj + 0x08);
                    if (desc != 0)
                    {
                        int entryId = MemoryAPI.ReadInteger(hProcess, desc + 0x0C);
                        if (entryId == targetEntryId)
                        {
                            // 找到了！返回它的终极身份证 GUID
                            return MemoryAPI.ReadQword(hProcess, desc + 0x00);
                        }
                    }
                }
                currentObj = MemoryAPI.ReadInteger(hProcess, currentObj + 0x3C);
            }
            return 0; // 没找到
        }


        /// <summary>
        /// 内存检索：根据 EntryID 获取指定游戏对象(机关/门)的 内存基址(Base)
        /// </summary>
        public uint GetGameObjectBaseByEntry(IntPtr hProcess, int targetEntryId)
        {
            int objMgr = MemoryAPI.ReadInteger(hProcess, 0x00B41414);
            if (objMgr == 0) return 0;

            int currentObj = MemoryAPI.ReadInteger(hProcess, objMgr + 0xAC);
            while (currentObj != 0)
            {
                int objType = MemoryAPI.ReadInteger(hProcess, currentObj + 0x14);
                if (objType == 5) // 5 = GameObject
                {
                    int desc = MemoryAPI.ReadInteger(hProcess, currentObj + 0x08);
                    if (desc != 0)
                    {
                        int entryId = MemoryAPI.ReadInteger(hProcess, desc + 0x0C);
                        if (entryId == targetEntryId)
                        {
                            // 🎯 找到了！直接返回它的 32位内存基址！
                            return (uint)currentObj;
                        }
                    }
                }
                currentObj = MemoryAPI.ReadInteger(hProcess, currentObj + 0x3C);
            }
            return 0; // 没找到
        }


        /// <summary>
        /// 内存检索：根据 EntryID 获取指定 Unit 单位的 GUID
        /// </summary>
        public ulong GetUnitGuidByEntry(IntPtr hProcess, int targetEntryId)
        {
            int objMgr = MemoryAPI.ReadInteger(hProcess, 0x00B41414);
            if (objMgr == 0) return 0;

            int currentObj = MemoryAPI.ReadInteger(hProcess, objMgr + 0xAC);
            while (currentObj != 0)
            {
                int objType = MemoryAPI.ReadInteger(hProcess, currentObj + 0x14);
                if (objType == 3) // 3 = 3 = Unit
                {
                    int desc = MemoryAPI.ReadInteger(hProcess, currentObj + 0x08);
                    if (desc != 0)
                    {
                        int entryId = MemoryAPI.ReadInteger(hProcess, desc + 0x0C);
                        if (entryId == targetEntryId)
                        {
                            // 找到了！返回它的终极身份证 GUID
                            return MemoryAPI.ReadQword(hProcess, desc + 0x00);
                        }
                    }
                }
                currentObj = MemoryAPI.ReadInteger(hProcess, currentObj + 0x3C);
            }
            return 0; // 没找到
        }


        /// <summary>
        /// 内存检索：根据 EntryID 获取指定 Unit 单位的 内存基址(Base)
        /// </summary>
        public uint GetUnitBaseByEntry(IntPtr hProcess, int targetEntryId)
        {
            int objMgr = MemoryAPI.ReadInteger(hProcess, 0x00B41414);
            if (objMgr == 0) return 0;

            int currentObj = MemoryAPI.ReadInteger(hProcess, objMgr + 0xAC);
            while (currentObj != 0)
            {
                int objType = MemoryAPI.ReadInteger(hProcess, currentObj + 0x14);
                if (objType == 3) // 3 = Unit
                {
                    int desc = MemoryAPI.ReadInteger(hProcess, currentObj + 0x08);
                    if (desc != 0)
                    {
                        int entryId = MemoryAPI.ReadInteger(hProcess, desc + 0x0C);
                        if (entryId == targetEntryId)
                        {
                            // 🎯 找到了！直接返回它的 32位内存基址！
                            return (uint)currentObj;
                        }
                    }
                }
                currentObj = MemoryAPI.ReadInteger(hProcess, currentObj + 0x3C);
            }
            return 0; // 没找到
        }


        /// <summary>
        /// 双圈全息雷达：同时扫描并返回【预警范围】和【打击范围】内的存活怪物数量
        /// </summary>
        public void GetAliveMobsStatus(IntPtr hProcess, int playerBase, float detectRadius, float strikeRadius, out int detectCount, out int strikeCount)
        {
            detectCount = 0;
            strikeCount = 0;

            // 1. 读取玩家坐标
            float myX = MemoryAPI.ReadFloat(hProcess, playerBase + 0x9B8);
            float myY = MemoryAPI.ReadFloat(hProcess, playerBase + 0x9BC);

            // 【算法优化】：提前计算两个半径的平方
            float detectRadiusSq = detectRadius * detectRadius;
            float strikeRadiusSq = strikeRadius * strikeRadius;

            // 2. 获取对象管理器
            int objMgr = MemoryAPI.ReadInteger(hProcess, 0x00B41414);
            if (objMgr == 0) return;

            // 3. 遍历对象链表
            int currentObj = MemoryAPI.ReadInteger(hProcess, objMgr + 0xAC);
            while (currentObj != 0)
            {
                int objType = MemoryAPI.ReadInteger(hProcess, currentObj + 0x14);

                if (objType == 3) // 怪物/NPC
                {
                    int desc = MemoryAPI.ReadInteger(hProcess, currentObj + 0x08);
                    if (desc != 0)
                    {
                        int hp = MemoryAPI.ReadInteger(hProcess, desc + 0x58);
                        int maxHp = MemoryAPI.ReadInteger(hProcess, desc + 0x70);

                        if (hp > 0 && hp < maxHp) // 活着且已掉血
                        {
                            float mX = MemoryAPI.ReadFloat(hProcess, currentObj + 0x9B8);
                            float mY = MemoryAPI.ReadFloat(hProcess, currentObj + 0x9BC);

                            // 计算距离的平方
                            float dx = myX - mX;
                            float dy = myY - mY;
                            float distSquared = (dx * dx) + (dy * dy);

                            // 判断是否在【大圈：15码预警范围】内
                            if (distSquared <= detectRadiusSq)
                            {
                                detectCount++;

                                // 判断是否在【小圈：10码打击范围】内
                                if (distSquared <= strikeRadiusSq)
                                {
                                    strikeCount++;
                                }
                            }
                        }
                    }
                }
                currentObj = MemoryAPI.ReadInteger(hProcess, currentObj + 0x3C);
            }
        }

        /// <summary>
        /// 寻尸雷达：扫描指定范围内未拾取且【包含战利品】的最近尸体
        /// </summary>
        public bool FindNearestCorpse(IntPtr hProcess, int playerBase, float maxRadius, System.Collections.Generic.HashSet<ulong> lootedSet, out int targetBase, out ulong targetGuid, out float tX, out float tY, out float tZ)
        {
            targetBase = 0; targetGuid = 0; tX = 0; tY = 0; tZ = 0;
            float myX = MemoryAPI.ReadFloat(hProcess, playerBase + 0x9B8);
            float myY = MemoryAPI.ReadFloat(hProcess, playerBase + 0x9BC);

            int objMgr = MemoryAPI.ReadInteger(hProcess, 0x00B41414);
            if (objMgr == 0) return false;

            int currentObj = MemoryAPI.ReadInteger(hProcess, objMgr + 0xAC);
            float minDistance = float.MaxValue;

            while (currentObj != 0)
            {
                int objType = MemoryAPI.ReadInteger(hProcess, currentObj + 0x14);
                if (objType == 3) // Unit (怪物)
                {
                    int desc = MemoryAPI.ReadInteger(hProcess, currentObj + 0x08);
                    if (desc != 0)
                    {
                        int hp = MemoryAPI.ReadInteger(hProcess, desc + 0x58);

                        if (hp == 0) // HP=0 代表尸体
                        {
                            ulong guid = MemoryAPI.ReadQword(hProcess, currentObj + 0x30);

                            // 【第一层过滤】：查阅黑名单，没摸过的尸体才处理
                            if (!lootedSet.Contains(guid))
                            {
                                // ==========================================
                                // 【第二层过滤】：新增的动态状态检测！
                                // 读取第 0 位，只有当尸体闪光（可拾取）时才去捡！
                                // ==========================================
                                if (Unit_Dynamic_State(hProcess, currentObj, 0))
                                {
                                    float mX = MemoryAPI.ReadFloat(hProcess, currentObj + 0x9B8);
                                    float mY = MemoryAPI.ReadFloat(hProcess, currentObj + 0x9BC);
                                    float mZ = MemoryAPI.ReadFloat(hProcess, currentObj + 0x9C0);

                                    float dist = (float)Math.Sqrt(Math.Pow(myX - mX, 2) + Math.Pow(myY - mY, 2));

                                    // 找寻范围内的，并且是距离玩家最近的
                                    if (dist <= maxRadius && dist < minDistance)
                                    {
                                        minDistance = dist;
                                        targetBase = currentObj;
                                        targetGuid = guid;
                                        tX = mX; tY = mY; tZ = mZ;
                                    }
                                }
                            }
                        }
                    }
                }
                currentObj = MemoryAPI.ReadInteger(hProcess, currentObj + 0x3C);
            }

            // 如果 targetBase 不为 0，说明找到了合法且带物品的尸体
            return targetBase != 0;
        }

        /// <summary>
        /// 内存遍历：获取当前背包的剩余空位格数 
        /// </summary>
        public int GetFreeBagSlots(IntPtr hProcess, int playerBase)
        {
            int freeSlots = 0;
            int playerDesc = MemoryAPI.ReadInteger(hProcess, playerBase + 0x08);
            //if (playerDesc == 0) return 0;
            if (playerDesc == 0) return 99;
            // 1. 扫描自带行囊 (16个槽位，私服专属偏移 0x850)
            for (int i = 0; i < 16; i++)
            {
                ulong itemGuid = MemoryAPI.ReadQword(hProcess, playerDesc + 0x850 + (i * 8));
                if (itemGuid == 0) freeSlots++;
            }

            // 2. 扫描 4 个扩展背包 (装备槽私服专属偏移 0x830)
            for (int i = 0; i < 4; i++)
            {
                ulong bagGuid = MemoryAPI.ReadQword(hProcess, playerDesc + 0x830 + (i * 8));
                if (bagGuid != 0)
                {
                    // 直接复用你基类里已经写好的神级方法找包包实体
                    int bagBase = GetTargetBaseByGuid(hProcess, bagGuid);
                    if (bagBase != 0)
                    {
                        int bagDesc = MemoryAPI.ReadInteger(hProcess, bagBase + 0x08);

                        // 读取总格数 (私服专属偏移 0xC0)
                        int numSlots = MemoryAPI.ReadInteger(hProcess, bagDesc + 0xC0);

                        // 遍历内部格子 (私服专属偏移 0xC8)
                        for (int j = 0; j < numSlots; j++)
                        {
                            ulong itemInBagGuid = MemoryAPI.ReadQword(hProcess, bagDesc + 0xC8 + (j * 8));
                            if (itemInBagGuid == 0) freeSlots++;
                        }
                    }
                }
            }
            return freeSlots;
        }


        /// <summary>
        /// 内存遍历：获取背包中指定 ItemID 的物品总数量 
        /// </summary>
        public int GetItemCount(IntPtr hProcess, int targetItemId)
        {
            int count = 0;
            int objMgr = MemoryAPI.ReadInteger(hProcess, 0x00B41414);
            if (objMgr == 0) return 0;

            // 读取玩家自己的 GUID，防止扫到别人包里的东西
            ulong myGuid = MemoryAPI.ReadQword(hProcess, objMgr + 0xC0);
            int currentObj = MemoryAPI.ReadInteger(hProcess, objMgr + 0xAC);

            while (currentObj != 0)
            {
                int objType = MemoryAPI.ReadInteger(hProcess, currentObj + 0x14);

                // 1 = 普通物品(Item)，2 = 容器(Container)
                if (objType == 1 || objType == 2)
                {
                    int desc = MemoryAPI.ReadInteger(hProcess, currentObj + 0x08);
                    if (desc != 0)
                    {
                        // ITEM_FIELD_OWNER (偏移 0x18)：读取物品主人
                        ulong ownerGuid = MemoryAPI.ReadQword(hProcess, desc + 0x18);

                        if (ownerGuid == myGuid)
                        {
                            // OBJECT_FIELD_ENTRY (偏移 0x0C)：读取物品 ID
                            int entryId = MemoryAPI.ReadInteger(hProcess, desc + 0x0C);
                            if (entryId == targetItemId)
                            {
                                // ITEM_FIELD_STACK_COUNT (偏移 0x38)：读取堆叠数量
                                int stackCount = MemoryAPI.ReadInteger(hProcess, desc + 0x38);
                                count += stackCount;
                            }
                        }
                    }
                }
                currentObj = MemoryAPI.ReadInteger(hProcess, currentObj + 0x3C);
            }
            return count;
        }

        /// <summary>
        /// 内存遍历：寻找包里指定物品，并返回它的【内存实体基址】
        /// </summary>
        public int GetItemBase(IntPtr hProcess, int targetItemId)
        {
            int objMgr = MemoryAPI.ReadInteger(hProcess, 0x00B41414);
            if (objMgr == 0) return 0;

            ulong myGuid = MemoryAPI.ReadQword(hProcess, objMgr + 0xC0);
            int currentObj = MemoryAPI.ReadInteger(hProcess, objMgr + 0xAC);

            while (currentObj != 0)
            {
                int objType = MemoryAPI.ReadInteger(hProcess, currentObj + 0x14);
                // 1 = 物品(Item)，2 = 容器(Container)
                if (objType == 1 || objType == 2)
                {
                    int desc = MemoryAPI.ReadInteger(hProcess, currentObj + 0x08);
                    if (desc != 0 && MemoryAPI.ReadQword(hProcess, desc + 0x18) == myGuid)
                    {
                        if (MemoryAPI.ReadInteger(hProcess, desc + 0x0C) == targetItemId)
                        {
                            return currentObj; // 找到了！直接返回它的基址！
                        }
                    }
                }
                currentObj = MemoryAPI.ReadInteger(hProcess, currentObj + 0x3C);
            }
            return 0; // 没找到
        }


        /// <summary>
        /// 纯净探测：检查包里现存最高级的【法力宝石】ItemID
        /// </summary>
        public int DetectBestManaGem(IntPtr hProcess)
        {
            if (GetItemCount(hProcess, 8008) > 0) return 8008; // 魔法红宝石 (最高级)
            if (GetItemCount(hProcess, 8007) > 0) return 8007; // 魔法黄水晶
            //if (GetItemCount(hProcess, 5514) > 0) return 5514; // 魔法玛瑙
            //if (GetItemCount(hProcess, 5513) > 0) return 5513; // 魔法翡翠
            return 0; // 一颗宝石都没有
        }


        /// <summary>
        /// 纯净探测：检查包里现存最高级的【治疗药水】ItemID
        /// </summary>
        public int DetectHealingPotionGem(IntPtr hProcess)
        {
            if (GetItemCount(hProcess, 13446) > 0) return 13446; // 特效治疗药水 (最高级)
            if (GetItemCount(hProcess, 3928) > 0) return 3928; // 超强治疗药水
            if (GetItemCount(hProcess, 1710) > 0) return 1710; // 强效治疗药水
            if (GetItemCount(hProcess, 929) > 0) return 929; // 治疗药水
            if (GetItemCount(hProcess, 858) > 0) return 858; // 次级治疗药水
            if (GetItemCount(hProcess, 118) > 0) return 118; // 初级治疗药水
            return 0;
        }

        /// <summary>
        /// 纯净探测：检查包里现存最高级的【法力药水】ItemID
        /// </summary>
        public int DetectManaPotionGem(IntPtr hProcess)
        {
            if (GetItemCount(hProcess, 13444) > 0) return 13444; // 特效法力药水 (最高级)
            if (GetItemCount(hProcess, 13443) > 0) return 13443; // 超强法力药水
            if (GetItemCount(hProcess, 6149) > 0) return 6149; // 强效法力药水
            if (GetItemCount(hProcess, 3827) > 0) return 3827; // 法力药水
            if (GetItemCount(hProcess, 3385) > 0) return 3385; // 次级法力药水
            if (GetItemCount(hProcess, 2455) > 0) return 2455; // 初级法力药水
            return 0;
        }

        /// <summary>
        /// 纯净探测：检查包里最高等级的【魔法水】，返回其 ItemID，没找到返回 0
        /// </summary>
        public int DetectBestWater(IntPtr hProcess)
        {
            // 优先找 55 级大水 (8079)
            if (GetItemCount(hProcess, 8079) > 0) return 8079;

            // 如果没有大水，再找 45 级水 (8078)
            if (GetItemCount(hProcess, 8078) > 0) return 8078;

            // 包里一滴水都没有
            return 0;
        }

        /// <summary>
        /// 纯净探测：检查包里最高等级的【魔法面包】，返回其 ItemID，没找到返回 0
        /// </summary>
        public int DetectBestFood(IntPtr hProcess)
        {
            // 优先找 55 级面包 (22895)
            if (GetItemCount(hProcess, 22895) > 0) return 22895;

            // 如果没有 55 级面包，找 45 级面包 (8076)
            if (GetItemCount(hProcess, 8076) > 0) return 8076;

            // 包里一点面包都没有
            return 0;
        }

        /// <summary>
        /// 内存检索：根据 ItemID 自动寻找背包中的位置并使用 (彻底告别 Lua 宏和快捷键)
        /// </summary>
        public bool UseItemByItemId(IntPtr hProcess, int targetItemId)
        {
            if (targetItemId == 0) return false;

            // 调用你已有的遍历背包所有物品的方法
            List<InventoryItem> allItems = GetAllInventoryItems(hProcess);

            // 找到第一组匹配的物品
            var item = allItems.Find(i => i.ItemId == targetItemId);

            if (item != null)
            {
                // 找到了！直接通过真正的背包格坐标使用它
                //RightClickBagItem(item.BagId, item.SlotId);
                ExecuteDynamicLua($"UseContainerItem({item.BagId}, {item.SlotId})");
                return true;
            }
            return false;
        }

        /// <summary>
        /// 乘船安全锁：判断玩家是否绑定在船只上
        /// 使用你挖出的 0x9E0 偏移
        /// </summary>
        public bool IsOnTransport(IntPtr hProcess, int playerBase)
        {
            // 读取玩家基址 + 0x9E0，如果数值不为 0，说明有绑定的 Transport Entry ID
            ulong transportGuid = MemoryAPI.ReadQword(hProcess, playerBase + 0x9E0);
            return transportGuid != 0;
        }

        /// <summary>
        /// 获取指定船只/飞艇的实时世界 XYZ 坐标
        /// </summary>
        public bool GetTransportCoordinates(IntPtr hProcess, int targetEntryId, out float shipX, out float shipY)
        {
            shipX = 0; shipY = 0;
            int objMgr = MemoryAPI.ReadInteger(hProcess, 0x00B41414);
            if (objMgr == 0) return false;

            int currentObj = MemoryAPI.ReadInteger(hProcess, objMgr + 0xAC);
            while (currentObj != 0)
            {
                if (MemoryAPI.ReadInteger(hProcess, currentObj + 0x14) == 5) // 5 = GameObject
                {
                    int desc = MemoryAPI.ReadInteger(hProcess, currentObj + 0x08);
                    if (desc != 0 && MemoryAPI.ReadInteger(hProcess, desc + 0x0C) == targetEntryId)
                    {
                        // 0x210 渲染矩阵
                        int matrixPtr = MemoryAPI.ReadInteger(hProcess, currentObj + 0x210);

                        if (matrixPtr > 0x01000000) // 确保指针合法
                        {
                            shipX = MemoryAPI.ReadFloat(hProcess, matrixPtr + 0x44);
                            shipY = MemoryAPI.ReadFloat(hProcess, matrixPtr + 0x48);
                            return true; // 成功拿到！
                        }
                    }
                }
                currentObj = MemoryAPI.ReadInteger(hProcess, currentObj + 0x3C);
            }
            return false;
        }


        /// <summary>
        /// 监控羽月要塞船只是否已经靠岸
        /// </summary>
        public bool IsFeathermoonShipDocked(IntPtr hProcess, float distanceTolerance = 5.0f)
        {
            float shipX, shipY;
            int shipEntryId = 177233; // 羽月要塞船只 ID

            // 羽月要塞码头的理论停靠点
            float targetDockX = -4197.307f;
            float targetDockY = 3286.628f;

            if (GetTransportCoordinates(hProcess, shipEntryId, out shipX, out shipY))
            {
                float distance = (float)Math.Sqrt(Math.Pow(shipX - targetDockX, 2) + Math.Pow(shipY - targetDockY, 2));

                if (distance <= distanceTolerance)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            return false;
        }


        /// <summary>
        /// 监控船只是否已经抵达对岸（厄运码头）
        /// </summary>
        public bool IsShipAtDestination(IntPtr hProcess, float distanceTolerance = 5.0f)
        {
            float shipX, shipY;
            int shipEntryId = 177233; // 羽月要塞船只 ID

            // 对岸厄运码头的理论停靠点
            float targetDestX = -4347.825f;
            float targetDestY = 2444.370f;

            if (GetTransportCoordinates(hProcess, shipEntryId, out shipX, out shipY))
            {
                float distance = (float)Math.Sqrt(Math.Pow(shipX - targetDestX, 2) + Math.Pow(shipY - targetDestY, 2));

                if (distance <= distanceTolerance)
                {
                    return true;
                }
            }
            return false;
        }


        /// <summary>
        /// 判断玩家是否处于骑乘状态 
        /// </summary>
        public bool IsMounted(IntPtr hProcess, int playerBase)
        {
            // 获取属性基址
            int descriptors = MemoryAPI.ReadInteger(hProcess, playerBase + 0x08);
            if (descriptors == 0) return false;

            // 坐骑模型ID 偏移：0x214
            int mountDisplayId = MemoryAPI.ReadInteger(hProcess, descriptors + 0x214);

            // 只要屁股底下有模型（> 0），说明绝对在马上！
            return mountDisplayId > 0;
        }

        //获取背包内所有物品并建立全局缓存池
        public List<InventoryItem> GetAllInventoryItems(IntPtr hProcess)
        {
            List<InventoryItem> inventory = new List<InventoryItem>();
            Dictionary<ulong, InventoryItem> itemCache = new Dictionary<ulong, InventoryItem>();

            int objMgrPtr = 0x00B41414;
            int objMgr = MemoryAPI.ReadInteger(hProcess, objMgrPtr);
            if (objMgr == 0) return inventory;

            ulong myGuid = MemoryAPI.ReadQword(hProcess, objMgr + 0xC0);
            int currentObj = MemoryAPI.ReadInteger(hProcess, objMgr + 0xAC);
            int playerBase = 0;

            // 1. 建立全局缓存池
            while (currentObj != 0)
            {
                ulong objGuid = MemoryAPI.ReadQword(hProcess, currentObj + 0x30);
                int objType = MemoryAPI.ReadInteger(hProcess, currentObj + 0x14);

                if (objType == 4 && objGuid == myGuid)
                {
                    playerBase = currentObj;
                }
                else if (objType == 1 || objType == 2) // 物品或容器
                {
                    int desc = MemoryAPI.ReadInteger(hProcess, currentObj + 0x08);
                    if (desc != 0)
                    {
                        ulong ownerGuid = MemoryAPI.ReadQword(hProcess, desc + 0x18);
                        if (ownerGuid == myGuid)
                        {
                            int entryId = MemoryAPI.ReadInteger(hProcess, desc + 0x0C);
                            int stackCount = MemoryAPI.ReadInteger(hProcess, desc + 0x38);

                            // 读取 0x54 的标志位，判断第 0 位是否为 1 (即是否绑定)
                            int flags = MemoryAPI.ReadInteger(hProcess, desc + 0x54);
                            bool isSoulbound = (flags & 1) == 1;

                            itemCache[objGuid] = new InventoryItem
                            {
                                Guid = objGuid,
                                BaseAddress = currentObj,
                                ItemId = entryId,
                                StackCount = stackCount,
                                IsSoulbound = isSoulbound // 存入对象
                            };
                        }
                    }
                }
                currentObj = MemoryAPI.ReadInteger(hProcess, currentObj + 0x3C);
            }

            if (playerBase == 0) return inventory;
            int playerDesc = MemoryAPI.ReadInteger(hProcess, playerBase + 0x08);

            // 2. 映射主行囊 (Bag 0)
            for (int i = 0; i < 16; i++)
            {
                ulong itemGuid = MemoryAPI.ReadQword(hProcess, playerDesc + 0x850 + (i * 8));
                if (itemGuid != 0 && itemCache.ContainsKey(itemGuid))
                {
                    var item = itemCache[itemGuid];
                    item.BagId = 0;
                    item.SlotId = i + 1; // 内存偏移从 0 开始，Lua 交互从 1 开始
                    inventory.Add(item);
                }
            }

            // 3. 映射扩展包 (Bag 1 到 Bag 4)
            for (int bagIdx = 1; bagIdx <= 4; bagIdx++)
            {
                ulong bagGuid = MemoryAPI.ReadQword(hProcess, playerDesc + 0x830 + ((bagIdx - 1) * 8));
                if (bagGuid != 0 && itemCache.ContainsKey(bagGuid))
                {
                    int bagDesc = MemoryAPI.ReadInteger(hProcess, itemCache[bagGuid].BaseAddress + 0x08);
                    int numSlots = MemoryAPI.ReadInteger(hProcess, bagDesc + 0xC0);

                    for (int j = 0; j < numSlots; j++)
                    {
                        ulong itemGuid = MemoryAPI.ReadQword(hProcess, bagDesc + 0xC8 + (j * 8));
                        if (itemGuid != 0 && itemCache.ContainsKey(itemGuid))
                        {
                            var item = itemCache[itemGuid];
                            item.BagId = bagIdx;
                            item.SlotId = j + 1;
                            inventory.Add(item);
                        }
                    }
                }
            }

            return inventory;
        }


        /// <summary>
        /// 内存探测：检查玩家身上是否带有“鬼魂”或“小精灵”的 Debuff
        /// </summary>
        public bool HasGhostDebuff(IntPtr hProcess, int playerBase)
        {
            int descriptors = MemoryAPI.ReadInteger(hProcess, playerBase + 0x08);
            if (descriptors == 0) return false;

            // 🌟 同步修正为 0xBC，从头到尾全量扫描，一个都不放过！
            int auraStartOffset = 0xBC;

            for (int i = 0; i < 48; i++)
            {
                int spellId = MemoryAPI.ReadInteger(hProcess, descriptors + auraStartOffset + (i * 4));

                // 8326 = 鬼魂, 20584 = 小精灵
                if (spellId == 8326 || spellId == 20584)
                {
                    return true; // 确诊为灵魂状态！
                }
            }
            return false;
        }

        /// <summary>
        /// 内存探测：检查玩家身上是否带有吃喝 Buff
        /// </summary>
        public void GetEatDrinkStatus(IntPtr hProcess, int playerBase, out bool isEating, out bool isDrinking)
        {
            isEating = false;
            isDrinking = false;

            int descriptors = MemoryAPI.ReadInteger(hProcess, playerBase + 0x08);
            if (descriptors == 0) return;

            // 🌟 核心真理：1.12.1 Aura 数组绝对起点！
            int auraStartOffset = 0xBC;

            for (int i = 0; i < 48; i++)
            {
                int spellId = MemoryAPI.ReadInteger(hProcess, descriptors + auraStartOffset + (i * 4));
                if (spellId == 0) continue;

                // 饮水 Buff (430=通用, 1137=45级, 22734=55级)
                if (spellId == 430 || spellId == 1137 || spellId == 22734)
                {
                    isDrinking = true;
                }
                // 进食 Buff (433=通用, 1131=45级, 29073=55级)
                else if (spellId == 433 || spellId == 1131 || spellId == 29073)
                {
                    isEating = true;
                }

                if (isEating && isDrinking) break; // 两个都有了，提前下班
            }
        }


        /// <summary>
        /// 检查玩家身上是否拥有指定的 Buff / Debuff ID (1.12.1 精准版)
        /// </summary>
        public bool HasPlayerBuff(IntPtr hProcess, int playerBase, int targetBuffId)
        {
            if (playerBase == 0) return false;

            // 获取玩家的 Descriptor（属性指针基址）
            int pDesc = MemoryAPI.ReadInteger(hProcess, playerBase + 0x08);

            // 1.12.1 确认的真实光环数组起点
            int auraStartOffset = 0xBC;
            int maxSlots = 48; // 最多支持 48 个 Buff/Debuff

            // 遍历 48 个槽位
            for (int i = 0; i < maxSlots; i++)
            {
                int offset = auraStartOffset + (i * 4);
                int currentBuffId = MemoryAPI.ReadInteger(hProcess, pDesc + offset);

                if (currentBuffId == targetBuffId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 检查背包里指定的物品是否在冷却中
        /// </summary>
        public bool IsItemOnCooldown(IntPtr hProcess, int targetItemId)
        {
            int listHeadAddress = ModuleBaseAddress + 0x8ECAF0;
            int currentNode = MemoryAPI.ReadInteger(hProcess, listHeadAddress);
            int loopCount = 0;

            // 魔兽是循环链表，终点是指回 listHeadAddress
            while (currentNode != 0 && currentNode != listHeadAddress && loopCount < 200)
            {
                int itemId = MemoryAPI.ReadInteger(hProcess, currentNode + 0x0C);

                if (itemId == targetItemId)
                {
                    // StartTime 的真实偏移是 0x1C ！！！
                    uint startTime = (uint)MemoryAPI.ReadInteger(hProcess, currentNode + 0x1C);
                    uint duration = (uint)MemoryAPI.ReadInteger(hProcess, currentNode + 0x20);

                    if (duration > 0)
                    {
                        // 获取当前 Windows 内核启动毫秒数
                        uint currentTick = GetTickCount();

                        // 计算是否在 CD 中
                        if (currentTick - startTime < duration)
                        {
                            return true; // 还在 CD 中！
                        }
                    }
                    return false; // CD 已经转完
                }

                currentNode = MemoryAPI.ReadInteger(hProcess, currentNode);
                loopCount++;
            }

            return false;
        }

        /// <summary>
        /// 检查指定的技能(全等级)或背包物品是否在冷却中 
        /// </summary>
        public bool IsSpellOnCooldown(IntPtr hProcess, params int[] targetIds)
        {
            int listHeadAddress = ModuleBaseAddress + 0x8ECAF0;
            int currentNode = MemoryAPI.ReadInteger(hProcess, listHeadAddress);
            int loopCount = 0;

            bool isOnCd = false;
            bool isSharedPotionOnCd = false;

            while (currentNode != 0 && currentNode != listHeadAddress && loopCount < 200)
            {
                int spellId = MemoryAPI.ReadInteger(hProcess, currentNode + 0x08);
                int itemId = MemoryAPI.ReadInteger(hProcess, currentNode + 0x0C);

                uint startA = (uint)MemoryAPI.ReadInteger(hProcess, currentNode + 0x10);
                uint durA = (uint)MemoryAPI.ReadInteger(hProcess, currentNode + 0x14);
                uint startB = (uint)MemoryAPI.ReadInteger(hProcess, currentNode + 0x1C);
                uint durB = (uint)MemoryAPI.ReadInteger(hProcess, currentNode + 0x20);

                uint currentTick = GetTickCount();

                // 1. 🚨 核心修复：只要内存中的 ID 包含在我们传入的数组里，就命中！
                if (targetIds.Contains(spellId) || targetIds.Contains(itemId))
                {
                    if (durA > 0 && durA < 4000000000 && (currentTick - startA < durA)) isOnCd = true;
                    if (durB > 0 && durB < 4000000000 && (currentTick - startB < durB)) isOnCd = true;
                }

                if (durB == 120000)
                {
                    if (currentTick - startB < durB) isSharedPotionOnCd = true;
                }

                currentNode = MemoryAPI.ReadInteger(hProcess, currentNode);
                loopCount++;
            }

            // 3. 兼容药水
            if (targetIds.Contains(3823) || targetIds.Contains(9172))
            {
                return isOnCd || isSharedPotionOnCd;
            }

            return isOnCd;
        }

        /// <summary>
        /// 检查剩余冷却时间 (支持全等级多ID同时检测)
        /// </summary>
        public double IsSpellOnCooldownRemaining(IntPtr hProcess, params int[] targetIds)
        {
            int listHeadAddress = ModuleBaseAddress + 0x8ECAF0;
            int currentNode = MemoryAPI.ReadInteger(hProcess, listHeadAddress);
            int loopCount = 0;

            double exactCd = 0.0;
            double sharedCategoryCd = 0.0;

            while (currentNode != 0 && currentNode != listHeadAddress && loopCount < 200)
            {
                int spellId = MemoryAPI.ReadInteger(hProcess, currentNode + 0x08);
                int itemId = MemoryAPI.ReadInteger(hProcess, currentNode + 0x0C);

                uint startA = (uint)MemoryAPI.ReadInteger(hProcess, currentNode + 0x10);
                uint durA = (uint)MemoryAPI.ReadInteger(hProcess, currentNode + 0x14);
                uint startB = (uint)MemoryAPI.ReadInteger(hProcess, currentNode + 0x1C);
                uint durB = (uint)MemoryAPI.ReadInteger(hProcess, currentNode + 0x20);

                uint currentTick = GetTickCount();

                // 🚨 命中判断升级
                if (targetIds.Contains(spellId) || targetIds.Contains(itemId))
                {
                    if (durA > 0 && durA < 4000000000)
                    {
                        uint elapsedA = currentTick - startA;
                        if (elapsedA < durA) exactCd = Math.Max(exactCd, (durA - elapsedA) / 1000.0);
                    }
                    if (durB > 0 && durB < 4000000000)
                    {
                        uint elapsedB = currentTick - startB;
                        if (elapsedB < durB) exactCd = Math.Max(exactCd, (durB - elapsedB) / 1000.0);
                    }
                }

                if (durB == 120000)
                {
                    uint elapsedB = currentTick - startB;
                    if (elapsedB < durB) sharedCategoryCd = Math.Max(sharedCategoryCd, (durB - elapsedB) / 1000.0);
                }

                currentNode = MemoryAPI.ReadInteger(hProcess, currentNode);
                loopCount++;
            }

            if (targetIds.Contains(3823) || targetIds.Contains(9172))
            {
                return Math.Max(exactCd, sharedCategoryCd);
            }

            return exactCd;
        }

        /// <summary>
        /// 内存读取：获取当前玩家的角色名字
        /// </summary>
        public string GetPlayerName(IntPtr hProcess)
        {
            // 模块基址 + 偏移 = 存放角色名字的真实地址
            int nameAddress = ModuleBaseAddress + 0x827D88;

            // 魔兽世界1.12.1的角色名字通常不会超过20个字符，读取32字节足够了
            // 内部会遇到 \0 自动截断
            return MemoryAPI.ReadString(hProcess, nameAddress, 32);
        }


        /// <summary>
        /// 视觉神经：精准判断角色是否完全加载完毕并在线 
        /// </summary>
        public bool IsInWorld(IntPtr hProcess)
        {
            // 1 = 完全进入游戏并在线，0 = 读条中、角色界面或未上线
            int inWorldFlag = MemoryAPI.ReadInteger(hProcess, ModuleBaseAddress + 0x0074B424);
            return (inWorldFlag & 0xFF) == 1;
        }

        public bool CheckIfInventoryFull(IntPtr hProcess, int playerBase)
        {
            // 如果剩余空位小于等于 5，立刻返回 true！
            return GetFreeBagSlots(hProcess, playerBase) <= 5;
        }

        public float GetDistanceToCoords(IntPtr hProcess, int playerBase, float targetX, float targetY, float targetZ)
        {
            float myX = MemoryAPI.ReadFloat(hProcess, playerBase + 0x9B8);
            float myY = MemoryAPI.ReadFloat(hProcess, playerBase + 0x9BC);
            float myZ = MemoryAPI.ReadFloat(hProcess, playerBase + 0x9C0);
            return (float)Math.Sqrt(Math.Pow(myX - targetX, 2) + Math.Pow(myY - targetY, 2) + Math.Pow(myZ - targetZ, 2));
        }

        public float GetDistanceToCorpse(IntPtr hProcess, int playerBase)
        {
            float cx = MemoryAPI.ReadFloat(hProcess, ModuleBaseAddress + 0x74E284);
            float cy = MemoryAPI.ReadFloat(hProcess, ModuleBaseAddress + 0x74E288);
            float cz = MemoryAPI.ReadFloat(hProcess, ModuleBaseAddress + 0x74E28C);
            return GetDistanceToCoords(hProcess, playerBase, cx, cy, cz);
        }


        /// <summary>
        /// 尸体鉴定雷达：判断当前是否是“副本内死亡”的假尸体
        /// </summary>
        public bool IsCorpseInInstance(IntPtr hProcess, float dummyX, float dummyY)
        {
            float cx = MemoryAPI.ReadFloat(hProcess, ModuleBaseAddress + 0x74E284);
            float cy = MemoryAPI.ReadFloat(hProcess, ModuleBaseAddress + 0x74E288);

            // 只要尸体在这个假坐标的 15 码内，就 100% 确认是死在副本里了！
            float dist = (float)Math.Sqrt(Math.Pow(cx - dummyX, 2) + Math.Pow(cy - dummyY, 2));
            return dist < 15.0f;
        }

        /// <summary>
        /// 获取玩家生命值百分比 (0.0 ~ 100.0)
        /// </summary>
        public float GetHealthPercent(IntPtr hProcess, int playerBase)
        {
            int desc = MemoryAPI.ReadInteger(hProcess, playerBase + 0x08);
            int hp = MemoryAPI.ReadInteger(hProcess, desc + 0x58);
            int maxHp = MemoryAPI.ReadInteger(hProcess, desc + 0x70);
            if (maxHp == 0) return 0;
            return ((float)hp / maxHp) * 100f;
        }

        /// <summary>
        /// 获取玩家法力值百分比 (0.0 ~ 100.0)
        /// </summary>
        public float GetManaPercent(IntPtr hProcess, int playerBase)
        {
            int desc = MemoryAPI.ReadInteger(hProcess, playerBase + 0x08);
            int mp = MemoryAPI.ReadInteger(hProcess, desc + 0x5C);
            int maxMp = MemoryAPI.ReadInteger(hProcess, desc + 0x74);
            if (maxMp == 0) return 0;
            return ((float)mp / maxMp) * 100f;
        }

        /// <summary>
        /// 阵营判断控制器：带记忆功能，终生只查一次户口
        /// </summary>
        public bool IsHordePlayer(bool _hasCheckedFaction, bool _isHorde, IntPtr hProcess, int playerBase)
        {
            // 呼叫底层函数拿阵营字符串
            string faction = GetPlayerFaction(hProcess, playerBase);

            // 如果拿到的是未知，说明游戏还没准备好，先返回 false，并且【不盖章】
            if (faction == "未知") return false;

            // 根据拿到的字符串做判定
            if (faction == "部落")
            {
                _isHorde = true;
            }
            else
            {
                _isHorde = false;
            }

            _hasCheckedFaction = true; // 查过户口了，盖章！
            return _isHorde;
        }


        /// <summary>
        /// 遍历对象管理器，获取与玩家自己绑定的 Entry ID 基址 
        /// </summary>
        public uint GetMySpecificPetBase(IntPtr hProcess, int targetEntryId)
        {
            int objMgrPtr = 0x00B41414;
            int objMgr = MemoryAPI.ReadInteger(hProcess, objMgrPtr);
            if (objMgr == 0) return 0;

            ulong playerGuid = MemoryAPI.ReadQword(hProcess, objMgr + 0xC0);
            if (playerGuid == 0) return 0;

            int currentObj = MemoryAPI.ReadInteger(hProcess, objMgr + 0xAC);

            while (currentObj != 0 && currentObj % 2 == 0) // 确保指针有效对齐
            {
                int objType = MemoryAPI.ReadInteger(hProcess, currentObj + 0x14);

                // 类型 3 代表 Unit (宠物也是 Unit)
                if (objType == 3)
                {
                    int desc = MemoryAPI.ReadInteger(hProcess, currentObj + 0x08);
                    if (desc != 0)
                    {
                        // 读取归属权 GUID
                        ulong charmedBy = MemoryAPI.ReadQword(hProcess, desc + 0x28);
                        ulong summonedBy = MemoryAPI.ReadQword(hProcess, desc + 0x30);

                        // 判断：这只宠物是我的吗？
                        if (charmedBy == playerGuid || summonedBy == playerGuid)
                        {
                            // 核心修改：读取 OBJECT_FIELD_ENTRY (偏移 0x0C)
                            int entryId = MemoryAPI.ReadInteger(hProcess, desc + 0x0C);

                            // 判断：这只宠物是我要找的那只吗？
                            if (entryId == targetEntryId)
                            {
                                return (uint)currentObj; // 完美命中！返回基址
                            }
                        }
                    }
                }
                currentObj = MemoryAPI.ReadInteger(hProcess, currentObj + 0x3C);
            }

            return 0; // 没找到
        }

        /// <summary>
        /// 遍历对象管理器，获取当前玩家的宠物基址 (Pet Base)
        /// </summary>
        /// <returns>宠物的基址，返回 0 说明宠物未召唤、离得太远或已死亡消失</returns>
        public int GetPetBase(IntPtr hProcess)
        {
            // 1. 获取对象管理器 (Object Manager)
            int objMgrPtr = 0x00B41414;
            int objMgr = MemoryAPI.ReadInteger(hProcess, objMgrPtr);
            if (objMgr == 0) return 0;

            // 2. 读取当前玩家的 GUID
            ulong playerGuid = MemoryAPI.ReadQword(hProcess, objMgr + 0xC0);
            if (playerGuid == 0) return 0;

            // 3. 获取链表第一个对象
            int currentObj = MemoryAPI.ReadInteger(hProcess, objMgr + 0xAC);

            // 4. 遍历链表寻找宠物
            while (currentObj != 0)
            {
                // 获取对象类型
                int objType = MemoryAPI.ReadInteger(hProcess, currentObj + 0x14);

                // 类型 3 代表 Unit (单位/怪物/宠物)
                if (objType == 3)
                {
                    // 获取属性描述符
                    int desc = MemoryAPI.ReadInteger(hProcess, currentObj + 0x08);
                    if (desc != 0)
                    {
                        // 读取归属权 GUID：CharmedBy (被魅惑) 和 SummonedBy (被召唤)
                        ulong charmedBy = MemoryAPI.ReadQword(hProcess, desc + 0x28);
                        ulong summonedBy = MemoryAPI.ReadQword(hProcess, desc + 0x30);

                        // 如果这个 Unit 的主人是当前玩家，那它就是你的宠物！
                        if (charmedBy == playerGuid || summonedBy == playerGuid)
                        {
                            return currentObj;
                        }
                    }
                }

                // 读取 Next Object 指针，继续遍历
                currentObj = MemoryAPI.ReadInteger(hProcess, currentObj + 0x3C);
            }

            // 遍历到底没找到，说明没宠物
            return 0;
        }

        /// <summary>
        /// 快速判断当前是否有存活且激活的宠物
        /// </summary>
        public bool HasActivePet(IntPtr hProcess)
        {
            return GetPetBase(hProcess) != 0;
        }


        /// <summary>
        /// 判断宠物是否死亡 (血量 <= 0 或 拥有死亡Flag)
        /// </summary>
        public bool IsPetDead(IntPtr hProcess, int petBase)
        {
            // 如果完全读不到基址，为了安全起见，我们把它也当成“需要抢救(吹哨或复活)”的状态
            if (petBase == 0) return true;

            int desc = MemoryAPI.ReadInteger(hProcess, petBase + 0x08);
            int hp = MemoryAPI.ReadInteger(hProcess, desc + 0x58);
            int flags = MemoryAPI.ReadInteger(hProcess, desc + 0xB8);

            // 0x40000 是第 18 位 (昏迷/死亡)
            bool hasDeadFlag = (flags & 0x40000) != 0;

            return hp <= 0 || hasDeadFlag;
        }

        /// <summary>
        /// 获取宠物的快乐值 (上限一般为 1050000)
        /// </summary>
        public int GetPetHappiness(IntPtr hProcess, int petBase)
        {
            if (petBase == 0) return 0;
            int desc = MemoryAPI.ReadInteger(hProcess, petBase + 0x08);
            // 根据你之前提供的 LUA 偏移，快乐值在 0x6C
            return MemoryAPI.ReadInteger(hProcess, desc + 0x6C);
        }

        /// <summary>
        /// 检查宠物身上是否有指定的 Buff/Debuff (光环)
        /// </summary>
        public bool HasPetAura(IntPtr hProcess, int petBase, int targetSpellId)
        {
            if (petBase == 0) return false;
            int desc = MemoryAPI.ReadInteger(hProcess, petBase + 0x08);
            if (desc == 0) return false;

            int auraStartOffset = 0xBC; // 光环数组起点
            for (int i = 0; i < 48; i++)
            {
                int spellId = MemoryAPI.ReadInteger(hProcess, desc + auraStartOffset + (i * 4));
                if (spellId == targetSpellId) return true;
            }
            return false;
        }

        /// <summary>
        /// 获取宠物行为状态返回 bool 值
        /// </summary>
        public bool Pet_Behavioral_State(IntPtr hProcess, int petBase, int bitPosition)
        {
            if (petBase == 0) return false;

            int descriptors = MemoryAPI.ReadInteger(hProcess, petBase + 0x08);
            if (descriptors == 0) return false;

            int flags = MemoryAPI.ReadInteger(hProcess, descriptors + 0xB8);

            return (flags & (1 << bitPosition)) != 0;
        }



        /// <summary>
        /// 检测 GameObject (门/箱子) 是否处于开启状态
        /// </summary>
        /// <param name="hProcess">进程句柄</param>
        /// <param name="gameObjectBase">门的动态基址 (uint类型)</param>
        /// <returns>true表示已开，false表示依然关着</returns>
        public bool IsGameObjectOpened(IntPtr hProcess, uint gameObjectBase)
        {
            if (gameObjectBase == 0) return false;

            // 1. 读取描述符数组的首地址 (固定偏移 0x8)
            // 注意：将 uint 的相加结果强转为 int，以匹配你的 MemoryAPI 参数类型
            uint descriptorPtr = MemoryAPI.ReadUInt32(hProcess, (int)(gameObjectBase + 0x8));
            if (descriptorPtr == 0) return false;

            // 2. 读取 GAMEOBJECT_STATE (偏移 0x38)
            uint state = MemoryAPI.ReadUInt32(hProcess, (int)(descriptorPtr + 0x38));

            // 状态为 0 代表门已开启 (Active)，1 代表关闭 (Ready)
            return state == 0;
        }

        /// <summary>
        /// 检查指定实体 (玩家/宠物/怪物) 身上是否有指定的 Buff/Debuff (支持全等级多ID匹配)
        /// </summary>
        public bool HasUnitAura(IntPtr hProcess, int unitBase, params int[] targetSpellIds)
        {
            if (unitBase == 0) return false;

            // 获取实体的 Descriptor（属性指针基址）
            int pDesc = MemoryAPI.ReadInteger(hProcess, unitBase + 0x08);
            if (pDesc == 0) return false;

            // 1.12.1 确认的真实光环数组起点
            int auraStartOffset = 0xBC;
            int maxSlots = 48; // 最多支持 48 个 Buff/Debuff 槽位

            // 遍历 48 个槽位
            for (int i = 0; i < maxSlots; i++)
            {
                int offset = auraStartOffset + (i * 4);
                int currentBuffId = MemoryAPI.ReadInteger(hProcess, pDesc + offset);

                // 如果该槽位有光环，且属于我们传入的 ID 数组中，立刻返回 true！
                if (currentBuffId != 0 && targetSpellIds.Contains(currentBuffId))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 在内存中寻找指定法术 ID 的 Buff 槽位 (用于传给 Lua 的 CancelPlayerBuff)
        /// </summary>
        /// <returns>返回槽位索引 (0-47)，如果没找到返回 -1</returns>
        public int GetBuffSlotIndex(IntPtr hProcess, int playerBase, int targetSpellId)
        {
            if (playerBase == 0) return -1;

            // 1. 获取玩家的 Descriptors 基址
            int descriptors = MemoryAPI.ReadInteger(hProcess, playerBase + 0x08);
            if (descriptors == 0) return -1;

            int auraStartOffset = 0xBC; // 1.12.1 光环数组起点
            int maxSlots = 48;          // 最多支持 48 个槽位

            // 2. 遍历槽位寻找匹配的 Spell ID
            for (int i = 0; i < maxSlots; i++)
            {
                int offset = auraStartOffset + (i * 4);
                int spellId = MemoryAPI.ReadInteger(hProcess, descriptors + offset);

                // 只要法术 ID 匹配，说明找到了目标 Buff
                if (spellId == targetSpellId)
                {
                    return i; // 返回槽位索引，例如 1
                }
            }

            // 没找到返回 -1
            return -1;
        }


        /// <summary>
        /// 检查指定实体 Unit 是否正在读条或引导法术
        /// 只要是法术都会被抓到
        /// </summary>
        public bool IsUnitCasting(IntPtr hProcess, int unitBase)
        {
            if (unitBase == 0) return false;

            // 1. 检测常规读条法术 (基址 + 0xC8C)
            int castSpellId = MemoryAPI.ReadInteger(hProcess, unitBase + 0xC8C);
            if (castSpellId != 0)
            {
                return true;
            }

            // 2. 检测引导型法术 (Descriptors + 0x240)
            int descriptors = MemoryAPI.ReadInteger(hProcess, unitBase + 0x08);
            if (descriptors != 0)
            {
                int channelSpellId = MemoryAPI.ReadInteger(hProcess, descriptors + 0x240);
                if (channelSpellId != 0)
                {
                    return true;
                }
            }

            return false;
        }




        /// <summary>
        /// 遍历内存对象管理器，通过 Entry ID 获取当前怪物的动态 GUID
        /// </summary>
        public ulong GetGuidByEntryId(IntPtr hProcess, int targetEntryId)
        {
            // 1.12.1 Object Manager 基址
            int objMgrBase = MemoryAPI.ReadInteger(hProcess, 0x00B41414);
            if (objMgrBase == 0) return 0;

            // 获取第一个对象的指针
            int currentObj = MemoryAPI.ReadInteger(hProcess, objMgrBase + 0xAC);

            while (currentObj != 0 && currentObj % 2 == 0) // 确保指针有效
            {
                // 读取对象类型 (Type: 3 是 Unit/怪物, 4 是 Player)
                int objType = MemoryAPI.ReadInteger(hProcess, currentObj + 0x14);

                if (objType == 3 || objType == 4)
                {
                    // 读取 Descriptors 属性基址
                    int descriptors = MemoryAPI.ReadInteger(hProcess, currentObj + 0x08);
                    if (descriptors != 0)
                    {
                        // OBJECT_FIELD_ENTRY 的 Index 是 3，所以偏移是 3 * 4 = 0x0C
                        int entryId = MemoryAPI.ReadInteger(hProcess, descriptors + 0x0C);

                        if (entryId == targetEntryId)
                        {
                            // 找到了！读取它的 64 位 GUID (偏移 0x30)
                            ulong dynamicGuid = MemoryAPI.ReadQword(hProcess, currentObj + 0x30);
                            return dynamicGuid;
                        }
                    }
                }

                // 读取下一个对象 (偏移 0x3C)
                currentObj = MemoryAPI.ReadInteger(hProcess, currentObj + 0x3C);
            }

            return 0; // 没找到
        }





        /// <summary>
        /// 获取任何单位（怪物/玩家/宠物）当前正在攻击的目标的 GUID
        /// </summary>
        public ulong GetUnitTargetGuid(IntPtr hProcess, int unitBase)
        {
            if (unitBase == 0) return 0;

            // 拿到属性基址
            int pDesc = MemoryAPI.ReadInteger(hProcess, unitBase + 0x08);
            if (pDesc == 0) return 0;

            // 🌟 UNIT_FIELD_TARGET 偏移为 0x40 (读取 UInt64 因为 GUID 是 64 位的)
            return MemoryAPI.ReadQword(hProcess, pDesc + 0x40);
        }





        /// <summary>
        /// 遍历内存获取指定 Entry ID 的所有实体对象
        /// </summary>
        /// <param name="_hProcess">进程句柄 (Process Handle)</param>
        /// <param name="targetEntryId">你要寻找的怪物 Entry ID</param>
        /// <returns>包含所有符合条件怪物的列表</returns>
        public static List<Entry_ID_List> GetEntitiesByEntryId(IntPtr _hProcess, int targetEntryId)
        {
            List<Entry_ID_List> foundUnits = new List<Entry_ID_List>();

            // 1. 获取 Object Manager 基址
            int objMgrPtr = 0x00B41414;
            int objMgr = MemoryAPI.ReadInteger(_hProcess, objMgrPtr);

            // 如果没读到 ObjMgr（比如还没进游戏），直接返回空列表防崩溃
            if (objMgr == 0) return foundUnits;

            // 2. 获取链表中的第一个对象
            int currentObj = MemoryAPI.ReadInteger(_hProcess, objMgr + 0xAC);

            // 3. 遍历链表 ( currentObj & 1 == 0 是为了防止野指针死循环 )
            while (currentObj != 0 && (currentObj & 1) == 0)
            {
                // 读取对象类型 (Type: 3=NPC/怪物, 4=玩家)
                int objType = MemoryAPI.ReadInteger(_hProcess, currentObj + 0x14);

                if (objType == 3 || objType == 4)
                {
                    // 读取属性数组指针 (Descriptors)
                    int descriptors = MemoryAPI.ReadInteger(_hProcess, currentObj + 0x08);

                    if (descriptors != 0)
                    {
                        // 读取当前的 Entry ID
                        int entryId = MemoryAPI.ReadInteger(_hProcess, descriptors + 0x0C);

                        // 匹配目标！
                        if (entryId == targetEntryId)
                        {
                            Entry_ID_List unit = new Entry_ID_List
                            {
                                BaseAddress = currentObj,
                                EntryId = entryId,
                                Guid = MemoryAPI.ReadQword(_hProcess, currentObj + 0x30),
                                X = MemoryAPI.ReadFloat(_hProcess, currentObj + 0x9B8),
                                Y = MemoryAPI.ReadFloat(_hProcess, currentObj + 0x9BC),
                                Z = MemoryAPI.ReadFloat(_hProcess, currentObj + 0x9C0)
                            };

                            foundUnits.Add(unit);
                        }
                    }
                }

                // 顺藤摸瓜：读取下一个对象
                currentObj = MemoryAPI.ReadInteger(_hProcess, currentObj + 0x3C);
            }

            return foundUnits;
        }


        /// <summary>
        /// 检查角色是否已经学习了指定的技能（法术）
        /// </summary>
        /// <param name="hProcess">目标进程的句柄 (Handle)</param>
        /// <param name="targetSpellId">需要查询的目标技能 ID</param>
        /// <returns>如果已学会返回 true，否则返回 false</returns>
        public static bool Query_known_skills(IntPtr hProcess, uint targetSpellId)
        {
            // 法术书技能数组静态基址
            int spellBookBaseAddress = 0xB700F0;

            // 物理范围最大限制（1024个槽位），防止死循环
            int maxSpellSlots = 1024;

            for (int i = 0; i < maxSpellSlots; i++)
            {
                // 计算当前槽位的内存地址：基址 + 偏移量(索引 * 4)
                int currentAddress = spellBookBaseAddress + (i * 4);

                // 读取当前地址的技能 ID
                uint currentSpellId = MemoryAPI.ReadUInt32(hProcess, currentAddress);

                // 逻辑1：如果读取到的值为 0，说明已经遍历完所有已学习的技能，提前退出
                if (currentSpellId == 0)
                {
                    return false;
                }

                // 逻辑2：如果当前读取到的技能 ID 等于我们想要查询的 ID，说明已经学会
                if (currentSpellId == targetSpellId)
                {
                    return true;
                }
            }

            // 逻辑3：兜底机制，如果遍历了完整的 1024 个槽位（到达 0xB710F0）还没找到
            return false;
        }


        /// <summary>
        /// 扫描周围加载的玩家实体（排除自身）
        /// </summary>
        public int GetNearbyOtherPlayerCount(IntPtr hProcess, int playerBase)
        {
            int objMgrPtr = 0x00B41414;
            int objMgr = MemoryAPI.ReadInteger(hProcess, objMgrPtr);

            if (objMgr == 0) return 0;

            // 对象链表的第一个实体
            int currentObj = MemoryAPI.ReadInteger(hProcess, objMgr + 0xAC);
            int otherPlayerCount = 0;

            // 遍历链表 (加入 != 0 防死循环保护)
            while (currentObj != 0)
            {
                // 核心优化：排除当前机器人自己！我们只关心“外人”
                if (currentObj != playerBase)
                {
                    int objType = MemoryAPI.ReadInteger(hProcess, currentObj + 0x14);

                    // 4 代表 Player 对象 (包含真实玩家和 GM)
                    if (objType == 4)
                    {
                        otherPlayerCount++;

                        // 读取详细信息 (用于日志排查)
                        int desc = MemoryAPI.ReadInteger(hProcess, currentObj + 0x08);
                        if (desc != 0)
                        {
                            int level = MemoryAPI.ReadInteger(hProcess, desc + 0x88);
                            float x = MemoryAPI.ReadFloat(hProcess, currentObj + 0x9B8);
                            float y = MemoryAPI.ReadFloat(hProcess, currentObj + 0x9BC);
                            float z = MemoryAPI.ReadFloat(hProcess, currentObj + 0x9C0);

                            // 读取种族和职业 (偏移 0x90 处的两个字节)
                            byte[] raceAndClass = MemoryAPI.ReadBytes(hProcess, desc + 0x90, 2);
                            byte raceId = raceAndClass != null && raceAndClass.Length >= 2 ? raceAndClass[0] : (byte)0;
                            byte classId = raceAndClass != null && raceAndClass.Length >= 2 ? raceAndClass[1] : (byte)0;

                            // 打印威胁目标坐标，方便你核对是 GM 隐身贴脸，还是门口路过的玩家
                            //Logger.Write($"[{CurrentPlayerName}] [雷达系统] 发现可疑玩家实体! 基址: 0x{currentObj:X}, 种族:{raceId} 职业:{classId}, {level}级, 坐标: {x:F1}, {y:F1}, {z:F1}");
                        }
                    }
                }

                // 指向链表中的下一个对象
                currentObj = MemoryAPI.ReadInteger(hProcess, currentObj + 0x3C);
            }

            return otherPlayerCount;
        }

        public bool IsCorpseInWest(IntPtr hProcess)
        {
            // 从盲扑那段代码借用一下尸体坐标基址
            float cx = MemoryAPI.ReadFloat(hProcess, ModuleBaseAddress + 0x74E284);
            float cy = MemoryAPI.ReadFloat(hProcess, ModuleBaseAddress + 0x74E288);

            // 如果没读到尸体坐标，默认让它跑东线（因为大部分Farm都在厄运东）
            if (cx == 0 && cy == 0) return false;

            // 厄运西门口点位约：X = -3822, Y = 1252
            // 厄运东门口点位约：X = -3741, Y = 934
            double distToWest = Math.Sqrt(Math.Pow(cx - (-3822f), 2) + Math.Pow(cy - 1252f, 2));
            double distToEast = Math.Sqrt(Math.Pow(cx - (-3741f), 2) + Math.Pow(cy - 934f, 2));

            // 如果距离西门更近，则返回 true
            return distToWest < distToEast;
        }


        // 判断尸体是否在“厄运之槌建筑群/走廊”范围内
        public bool IsCorpseInDireMaulArea(IntPtr hProcess)
        {
            float cx = MemoryAPI.ReadFloat(hProcess, ModuleBaseAddress + 0x74E284);
            float cy = MemoryAPI.ReadFloat(hProcess, ModuleBaseAddress + 0x74E288);

            // 没读到尸体坐标，保守起见当做在野外处理
            if (cx == 0 && cy == 0) return false;

            // 厄运枢纽中心大致坐标 (-3877, 1124)
            // 半径 300 码基本完美涵盖了厄运东、西、北的入口广场和长走廊
            double distToHub = Math.Sqrt(Math.Pow(cx - (-3877f), 2) + Math.Pow(cy - 1124f, 2));

            // 如果尸体距离枢纽中心小于 300 码，说明已经进大门了
            return distToHub < 300.0f;
        }



        /// <summary>
        /// 根据玩家坐标，在对象链表中查找最近的 Unit（非玩家、非宠物），可指定 Entry ID 进行筛选
        /// </summary>
        /// <param name="hProcess">进程句柄</param>
        /// <param name="playerBase">玩家动态基址</param>
        /// <param name="entryId">要查找的怪物 Entry ID（如果为 0 则不过滤，搜索所有 Unit）</param>
        /// <returns>最近匹配的 Unit 的 64 位 GUID，未找到则返回 0</returns>
        public static ulong FindNearestUnitGuid(IntPtr hProcess, int playerBase, int entryId)
        {
            try
            {
                // 1. 获取对象管理器地址
                int objMgr = MemoryAPI.ReadInteger(hProcess, 0x00B41414);
                if (objMgr == 0) return 0;

                // 2. 读取玩家自身的 GUID
                ulong playerGuid = MemoryAPI.ReadQword(hProcess, objMgr + 0xC0);
                if (playerGuid == 0) return 0;

                // 3. 读取玩家坐标
                float px = MemoryAPI.ReadFloat(hProcess, playerBase + 0x9B8);
                float py = MemoryAPI.ReadFloat(hProcess, playerBase + 0x9BC);
                float pz = MemoryAPI.ReadFloat(hProcess, playerBase + 0x9C0);

                // 4. 遍历对象链表
                int currentObj = MemoryAPI.ReadInteger(hProcess, objMgr + 0xAC);
                ulong nearestGuid = 0;
                double nearestDistSq = double.MaxValue;

                while (currentObj != 0)
                {
                    // 读取对象类型 (4=Player, 3=Unit)
                    int objType = MemoryAPI.ReadInteger(hProcess, currentObj + 0x14);
                    if (objType == 3) // 是 Unit
                    {
                        ulong guid = MemoryAPI.ReadQword(hProcess, currentObj + 0x30);
                        if (guid == playerGuid) goto next; // 跳过玩家自己（虽然类型不同，但以防万一）

                        int desc = MemoryAPI.ReadInteger(hProcess, currentObj + 0x08);
                        if (desc != 0)
                        {
                            // 检查是否为宠物（通过描述符中的召唤者/控制者）
                            ulong charmed = MemoryAPI.ReadQword(hProcess, desc + 0x28);
                            ulong summoned = MemoryAPI.ReadQword(hProcess, desc + 0x30);
                            if (charmed == playerGuid || summoned == playerGuid)
                                goto next;

                            // 跳过尸体（当前血量 <= 0）
                            int currentHp = MemoryAPI.ReadInteger(hProcess, desc + 0x58);
                            if (currentHp <= 0)
                                goto next;

                            // 读取该单位的 Entry ID
                            int currentEntryId = MemoryAPI.ReadInteger(hProcess, desc + 0x0C);

                            // 如果指定了 entryId 且不匹配，则跳过
                            if (entryId != 0 && currentEntryId != entryId)
                                goto next;

                            // 读取单位坐标
                            float x = MemoryAPI.ReadFloat(hProcess, currentObj + 0x9B8);
                            float y = MemoryAPI.ReadFloat(hProcess, currentObj + 0x9BC);
                            float z = MemoryAPI.ReadFloat(hProcess, currentObj + 0x9C0);

                            // 计算距离平方
                            double dx = x - px;
                            double dy = y - py;
                            double dz = z - pz;
                            double distSq = dx * dx + dy * dy + dz * dz;

                            if (distSq < nearestDistSq)
                            {
                                nearestDistSq = distSq;
                                nearestGuid = guid;
                            }
                        }
                    }

                next:
                    // 移动到下一个对象
                    currentObj = MemoryAPI.ReadInteger(hProcess, currentObj + 0x3C);
                }

                return nearestGuid;
            }
            catch
            {
                // 读取异常则返回 0
                return 0;
            }
        }


        /// <summary>
        /// 查找最近且已进入战斗的 Unit（非玩家、非宠物），可指定 Entry ID 进行筛选
        /// </summary>
        /// <param name="hProcess">进程句柄</param>
        /// <param name="playerBase">玩家动态基址</param>
        /// <param name="entryId">要查找的怪物 Entry ID（0 表示不过滤）</param>
        /// <returns>最近且进战斗的 Unit 的 64 位 GUID，未找到则返回 0</returns>
        public static ulong FindNearestCombatUnitGuid(IntPtr hProcess, int playerBase, int entryId)
        {
            try
            {
                // 1. 获取对象管理器地址
                int objMgr = MemoryAPI.ReadInteger(hProcess, 0x00B41414);
                if (objMgr == 0) return 0;

                // 2. 读取玩家自身的 GUID
                ulong playerGuid = MemoryAPI.ReadQword(hProcess, objMgr + 0xC0);
                if (playerGuid == 0) return 0;

                // 3. 读取玩家坐标
                float px = MemoryAPI.ReadFloat(hProcess, playerBase + 0x9B8);
                float py = MemoryAPI.ReadFloat(hProcess, playerBase + 0x9BC);
                float pz = MemoryAPI.ReadFloat(hProcess, playerBase + 0x9C0);

                // 4. 遍历对象链表
                int currentObj = MemoryAPI.ReadInteger(hProcess, objMgr + 0xAC);
                ulong nearestGuid = 0;
                double nearestDistSq = double.MaxValue;

                while (currentObj != 0)
                {
                    int objType = MemoryAPI.ReadInteger(hProcess, currentObj + 0x14);
                    if (objType == 3) // 是 Unit
                    {
                        ulong guid = MemoryAPI.ReadQword(hProcess, currentObj + 0x30);
                        if (guid == playerGuid) goto next;

                        int desc = MemoryAPI.ReadInteger(hProcess, currentObj + 0x08);
                        if (desc != 0)
                        {
                            // 排除宠物
                            ulong charmed = MemoryAPI.ReadQword(hProcess, desc + 0x28);
                            ulong summoned = MemoryAPI.ReadQword(hProcess, desc + 0x30);
                            if (charmed == playerGuid || summoned == playerGuid)
                                goto next;

                            // 跳过尸体（当前血量 <= 0）
                            int currentHp = MemoryAPI.ReadInteger(hProcess, desc + 0x58);
                            if (currentHp <= 0)
                                goto next;

                            // EntryID 筛选
                            int currentEntryId = MemoryAPI.ReadInteger(hProcess, desc + 0x0C);
                            if (entryId != 0 && currentEntryId != entryId)
                                goto next;

                            // 读取战斗状态（bit 19）
                            int flags = MemoryAPI.ReadInteger(hProcess, desc + 0xB8);
                            bool inCombat = (flags & (1 << 19)) != 0;
                            if (!inCombat)
                                goto next; // 未进战斗则跳过

                            // 读取坐标并计算距离
                            float x = MemoryAPI.ReadFloat(hProcess, currentObj + 0x9B8);
                            float y = MemoryAPI.ReadFloat(hProcess, currentObj + 0x9BC);
                            float z = MemoryAPI.ReadFloat(hProcess, currentObj + 0x9C0);

                            double dx = x - px;
                            double dy = y - py;
                            double dz = z - pz;
                            double distSq = dx * dx + dy * dy + dz * dz;

                            if (distSq < nearestDistSq)
                            {
                                nearestDistSq = distSq;
                                nearestGuid = guid;
                            }
                        }
                    }

                next:
                    currentObj = MemoryAPI.ReadInteger(hProcess, currentObj + 0x3C);
                }

                return nearestGuid;
            }
            catch
            {
                return 0;
            }
        }


        /// <summary>
        /// 在指定坐标半径范围内，查找最接近中心点的 Unit（非玩家、非宠物）的 GUID
        /// </summary>
        /// <param name="hProcess">进程句柄</param>
        /// <param name="entryId">要查找的怪物 Entry ID</param>
        /// <param name="centerX">中心点 X 坐标</param>
        /// <param name="centerY">中心点 Y 坐标</param>
        /// <param name="centerZ">中心点 Z 坐标</param>
        /// <param name="radius">搜索半径（码）</param>
        /// <returns>距离中心点最近的匹配 Unit 的 GUID，未找到则返回 0</returns>
        public static ulong FindUnitGuidByEntryIdAndPosition(IntPtr hProcess, int entryId, float centerX, float centerY, float centerZ, float radius)
        {
            try
            {
                // 1. 获取对象管理器地址
                int objMgr = MemoryAPI.ReadInteger(hProcess, 0x00B41414);
                if (objMgr == 0) return 0;

                // 2. 读取玩家自身的 GUID（用于排除宠物）
                ulong playerGuid = MemoryAPI.ReadQword(hProcess, objMgr + 0xC0);
                if (playerGuid == 0) return 0;

                // 3. 遍历对象链表
                int currentObj = MemoryAPI.ReadInteger(hProcess, objMgr + 0xAC);
                ulong bestGuid = 0;
                double bestDistSq = double.MaxValue;

                while (currentObj != 0)
                {
                    int objType = MemoryAPI.ReadInteger(hProcess, currentObj + 0x14);
                    if (objType == 3) // 是 Unit
                    {
                        ulong guid = MemoryAPI.ReadQword(hProcess, currentObj + 0x30);
                        if (guid == playerGuid) goto next; // 跳过玩家自己

                        int desc = MemoryAPI.ReadInteger(hProcess, currentObj + 0x08);
                        if (desc != 0)
                        {
                            // 排除宠物（被玩家魅惑或召唤）
                            ulong charmed = MemoryAPI.ReadQword(hProcess, desc + 0x28);
                            ulong summoned = MemoryAPI.ReadQword(hProcess, desc + 0x30);
                            if (charmed == playerGuid || summoned == playerGuid)
                                goto next;

                            // 跳过尸体（当前血量 <= 0）
                            int currentHp = MemoryAPI.ReadInteger(hProcess, desc + 0x58);
                            if (currentHp <= 0)
                                goto next;

                            // 匹配 Entry ID
                            int currentEntryId = MemoryAPI.ReadInteger(hProcess, desc + 0x0C);
                            if (currentEntryId != entryId)
                                goto next;

                            // 读取单位坐标
                            float x = MemoryAPI.ReadFloat(hProcess, currentObj + 0x9B8);
                            float y = MemoryAPI.ReadFloat(hProcess, currentObj + 0x9BC);
                            float z = MemoryAPI.ReadFloat(hProcess, currentObj + 0x9C0);

                            // 计算到中心点的距离平方
                            double dx = x - centerX;
                            double dy = y - centerY;
                            double dz = z - centerZ;
                            double distSq = dx * dx + dy * dy + dz * dz;

                            // 检查是否在半径内（比较平方，避免开方）
                            double radiusSq = radius * radius;
                            if (distSq <= radiusSq && distSq < bestDistSq)
                            {
                                bestDistSq = distSq;
                                bestGuid = guid;
                            }
                        }
                    }

                next:
                    currentObj = MemoryAPI.ReadInteger(hProcess, currentObj + 0x3C);
                }

                return bestGuid;
            }
            catch
            {
                return 0;
            }
        }



        /// <summary>
        /// 遍历内存链表，寻找属于当前玩家的专属鱼漂，并返回其对象基址。
        /// </summary>
        /// <param name="hProcess">进程句柄</param>
        /// <param name="playerBase">当前角色的实体基址</param>
        /// <returns>找到的鱼漂基址，若未找到则返回 0</returns>
        public static uint GetPlayerBobberBase(IntPtr hProcess, int playerBase)
        {
            // 防御性检查
            if (playerBase == 0) return 0;

            // 根据传入的角色基址，直接读取其 64位 GUID (偏移 0x30)
            ulong playerGuid = MemoryAPI.ReadQword(hProcess, playerBase + 0x30);
            if (playerGuid == 0) return 0;

            int objMgrPtr = 0x00B41414;
            int objMgr = MemoryAPI.ReadInteger(hProcess, objMgrPtr);
            if (objMgr == 0) return 0;

            // 获取 Object Manager 链表里的第一个对象 (偏移 0xAC)
            int currentObj = MemoryAPI.ReadInteger(hProcess, objMgr + 0xAC);

            while (currentObj != 0)
            {
                // 读取对象类型 (5 = GameObject)
                int objType = MemoryAPI.ReadInteger(hProcess, currentObj + 0x14);

                if (objType == 5)
                {
                    // 读取属性数组 (Descriptors) 基址 (偏移 0x08)
                    int descriptor = MemoryAPI.ReadInteger(hProcess, currentObj + 0x08);

                    if (descriptor != 0)
                    {
                        // 验证 CreatedBy GUID 是否为当前玩家 (偏移 0x18)
                        ulong createdByGuid = MemoryAPI.ReadQword(hProcess, descriptor + 0x18);

                        if (createdByGuid == playerGuid)
                        {
                            return (uint)currentObj; // 成功匹配，直接返回鱼漂基址
                        }
                    }
                }

                // 指向下一个对象指针 (偏移 0x3C)
                currentObj = MemoryAPI.ReadInteger(hProcess, currentObj + 0x3C);
            }

            return 0; // 遍历结束未找到
        }


        /// <summary>
        /// 传入鱼漂的对象基址，读取 0x38 状态字节。0 为咬钩 (true)，其余为 false。
        /// </summary>
        public static bool IsBobberBiting(IntPtr hProcess, uint bobberBase)
        {
            if (bobberBase == 0) return false;

            int descriptor = MemoryAPI.ReadInteger(hProcess, (int)bobberBase + 0x08);

            if (descriptor == 0) return false;

            // 读取 0x38 (GAMEOBJECT_STATE) 的状态字节
            int state = MemoryAPI.ReadInteger(hProcess, descriptor + 0x38);

            // 状态为 0 代表水花溅起（已咬钩）
            return state == 0;
        }


        public static int GetFishingSkillLevel(IntPtr hProcess, int playerBase)
        {
            if (playerBase == 0) return 0;

            int descriptor = MemoryAPI.ReadInteger(hProcess, playerBase + 0x08);
            if (descriptor == 0) return 0;

            int skillBaseOffset = 0xB38;

            // 遍历 128 个技能槽[cite: 5]
            for (int i = 0; i < 128; i++)
            {
                int slotOffset = skillBaseOffset + (i * 12);
                int rawSkill = MemoryAPI.ReadInteger(hProcess, descriptor + slotOffset);

                // 过滤高 16 位，取低 16 位真实技能 ID[cite: 5]
                int skillId = rawSkill % 65536;

                if (skillId == 356) // 356 代表钓鱼技能[cite: 5]
                {
                    // 真实技能等级在槽位偏移 +4 的位置[cite: 5]
                    int levelData = MemoryAPI.ReadInteger(hProcess, descriptor + slotOffset + 4);
                    return levelData % 65536;
                }
            }
            return 0;
        }

        public static bool HasMainHandLure(IntPtr hProcess, int playerBase)
        {
            int playerDesc = MemoryAPI.ReadInteger(hProcess, playerBase + 0x08);
            if (playerDesc == 0) return false;

            // 1. 读取主手武器的 64 位 GUID (偏移 0x810)
            ulong mainHandGuid = MemoryAPI.ReadQword(hProcess, playerDesc + 0x810);
            if (mainHandGuid == 0) return false;

            // 2. 从 ObjectManager 遍历寻找到这把武器的实体基址 
            int weaponBase = GetTargetBaseByGuid(hProcess, mainHandGuid);
            if (weaponBase == 0) return false;

            // 3. 读取武器属性基址
            int weaponDesc = MemoryAPI.ReadInteger(hProcess, weaponBase + 0x08);
            if (weaponDesc == 0) return false;

            // 4. 读取附魔槽位 0 (偏移 0x58)
            int ench0_Id = MemoryAPI.ReadInteger(hProcess, weaponDesc + 0x58);
            int ench0_Duration = MemoryAPI.ReadInteger(hProcess, weaponDesc + 0x5C);

            // 5. 读取附魔槽位 1 (偏移 0x64)[cite: 7]
            int ench1_Id = MemoryAPI.ReadInteger(hProcess, weaponDesc + 0x64);
            int ench1_Duration = MemoryAPI.ReadInteger(hProcess, weaponDesc + 0x68);

            // 6. 严谨判断：只要有任意一个槽位的 ID 大于 0 且 剩余时间大于 0，即判定为已上饵[cite: 7]
            if ((ench0_Id > 0 && ench0_Duration > 0) || (ench1_Id > 0 && ench1_Duration > 0))
            {
                return true;
            }

            return false;
        }

        // ==========================================
        // 系统级内置不售卖白名单
        // ==========================================
        public static readonly HashSet<int> BuiltInWhitelist = new HashSet<int>
        {
            // -- 法师施法材料 --
            17031,  // 传送符文
            17032,  // 传送门符文
            17020,  // 魔粉 (奥术光辉材料)
            // -- 治疗药水 --
            13446,  // 特效治疗药水 (最高级)
            3928,   // 超强治疗药水
            1710,   // 强效治疗药水
            929,    // 治疗药水
            // -- 法力药水 --
            13444,  // 特效法力药水 (最高级)
            13443,  // 超强法力药水
            6149,   // 强效法力药水
            3827,   // 法力药水
            // -- 背包 --
            4500,   //旅行者的背包
            3914,   //旅者背包
            1725,   //大背包
            // -- 吃喝 --
            8079,   //魔法晶水
            8078,   //魔法苏打水
            22895,  //魔法肉桂面包
            8076,   //魔法甜面包
        };

        // ========================================================
        //        生命周期及虚函数管理
        // ========================================================
        public abstract string OnTick(IntPtr hProcess, int playerBase);

        // 变量声明区
        protected DateTime _lastLoginActionTime = DateTime.MinValue;

        public string AutoLoginTick(IntPtr hProcess, string account, string password, string realmName, string playerName)
        {
            // 3. 高频雷达节流阀：每 3 秒脉冲一次
            if ((DateTime.Now - _lastLoginActionTime).TotalSeconds < 3)
            {
                return "正在登录中";
            }

            // ==========================================
            // 欺骗客户端：发射虚拟鼠标移动，无限重置 AFK 计时器！
            // 只要有这个，游戏就绝对不会以为你在挂机！
            // ==========================================
            if (_gameWindowHandle != IntPtr.Zero)
            {
                // 传入 IntPtr.Zero 相当于告诉游戏：鼠标在坐标 (0,0) 动了一下，纯静默，没有任何副作用
                PostMessage(_gameWindowHandle, WM_MOUSEMOVE, IntPtr.Zero, IntPtr.Zero);
            }

            // ==========================================
            //  4. 极简版 Lua 全自动登录 
            // ==========================================
            string luaPayload = $@"
    if (ZeroBot_IsEnteringWorld) then return; end
    if (ZeroBot_HasRequestedLogin) then return; end
    if (GlueDialog and GlueDialog:IsVisible()) then
        local txt = GlueDialogText:GetText() or '';
        if string.find(txt, '冻结') or string.find(txt, 'suspended') or string.find(txt, '无效') or string.find(txt, '身份验证失败') or string.find(txt, '连接目前受限') then
            AccountLoginAccountEdit:SetText('FATAL_ERROR');
            return; 
        elseif string.find(txt, '正在连接')  or string.find(txt, '获取') or string.find(txt, '成功') or string.find(txt, '刷新') then
            return; 
        elseif string.find(txt, '断开') or string.find(txt, '错误') or string.find(txt, '有误')  or string.find(txt, '连接失败') then
            GlueDialog:Hide(); 
            return;
        end
        return; 
    end
    if (CharacterSelectUI and CharacterSelectUI:IsVisible()) then
        for i=1, GetNumCharacters() do
            if GetCharacterInfo(i) == '{playerName}' then
                CharacterSelect_SelectCharacter(i);
                ZeroBot_HasRequestedLogin = true;
                EnterWorld();
                ZeroBot_IsEnteringWorld = true; 
                return;
            end
        end
        return;
    end
    if (AccountLoginUI and AccountLoginUI:IsVisible()) then
        DefaultServerLogin('{account}', '{password}');
        return;
    end
";

            luaPayload = luaPayload.Replace("\r", " ").Replace("\n", " ");
            ExecuteDynamicLua(luaPayload);
            _lastLoginActionTime = DateTime.Now;
            Logger.Write($"[UI控制台] [自动登录] 正在登录中");
            return "正在登录中";
        }

        // ==========================================
        // 别忘了在 Dispose 或者重置的时候把计时器清空
        public void Dispose()
        {
            if (_memAccessor != null) { _memAccessor.Dispose(); _memAccessor = null; }
            if (_sharedMem != null) { _sharedMem.Dispose(); _sharedMem = null; }
        }
    }
}