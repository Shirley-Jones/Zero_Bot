using Newtonsoft.Json; // 引入 Json 库
using System;
using Zero.Core;


namespace Zero.Script
{
    public class AutoFishing : DLLManager
    {
        public override string ScriptName => "自动钓鱼";
        private IntPtr _hProcess;
        private int _playerBase;
        private bool _wasInWorld = true; // 记录上一帧是否在正常游戏世界中（用于捕获蓝条结束瞬间）
        public static string LastAction = "";
        public static float LastX = 0;
        public static float LastY = 0;


        // 用于记录出蓝条后 5 秒倒计时的终点
        private DateTime _resumeTimeAfterLoad = DateTime.MinValue;
        // 用于标记当前是否正处于“刚出蓝条的等待保护期”
        private bool _isWaitingForWorldLoad = false;
        private int _Fishing_attempts_Count = 0;
        // 在节点外部（或类中）声明这两个时间变量
        private DateTime _castBufferTime = DateTime.MinValue;
        private DateTime _nextCastTime = DateTime.MinValue;
        // 在节点外部（或你的钓鱼行为类中）声明这两个状态变量
        private uint _currentBobberBase = 0;
        // 在外部声明，用于控制上饵 Lua 指令的网络缓冲期
        private DateTime _lureBufferTime = DateTime.MinValue;
        private bool _isApplyingLure = false;
        private DateTime _nextLureTime = DateTime.MinValue;
        // 随机数生成器与收杆时间戳
        private Random _rnd = new Random();
        private DateTime _hookTime = DateTime.MinValue;

        // 防暂离跳跃时间戳（初始化为当前时间加上 10~20 分钟的随机值）
        private DateTime _nextAntiAfkJumpTime = DateTime.Now.AddMinutes(new Random().Next(10, 21));

        // 用于记录上一次打印的日志，防止行为树每帧 Tick 疯狂刷屏
        private string _lastLogMessage = "";

        // 专用的行为树单次打印函数
        private void LogOnce(string message)
        {
            if (_lastLogMessage != message)
            {
                Logger.Write($"[{CurrentPlayerName}] [自动钓鱼] {message}");
                _lastLogMessage = message; // 记录当前状态
            }
        }



        private Node _rootTree; // 最高指挥官

        public AutoFishing()
        {
            BuildTree();
        }

        private void BuildTree()
        {

            _rootTree = new Sequence(

                // 摧毁逻辑
                new DestroyGarbageNode(this, () => _hProcess, () => _playerBase),   //自动摧毁名单内的物品

                new WaitNode(100),

                // 防暂离跳跃逻辑 (Anti-AFK)
                new ActionNode(() =>
                {
                    // 如果还没到设定的跳跃时间，直接静默放行，继续去钓鱼
                    if (DateTime.Now < _nextAntiAfkJumpTime)
                    {
                        return NodeState.Success;
                    }

                    // 时间到了，执行跳跃
                    LogOnce($"触发防暂离机制 (已挂机一段时间)，执行跳跃...");
                    _ = Jump(50);

                    // 重置下一次跳跃的时间（未来 10 到 20 分钟内的随机时间）
                    int nextJumpMinutes = _rnd.Next(10, 21);
                    _nextAntiAfkJumpTime = DateTime.Now.AddMinutes(nextJumpMinutes);

                    // 打印提示，方便你在后台观测挂机状态
                    Logger.Write($"[{CurrentPlayerName}] [自动钓鱼] 下一次防暂离跳跃将在 {nextJumpMinutes} 分钟后执行。");

                    return NodeState.Success; // 跳跃指令发出后，放行进入后续的上饵/抛竿流程
                }),

                new WaitNode(2000), // 跳跃后给 2000 毫秒的动作缓冲时间，防止落地前直接抛竿导致报错

                // 鱼饵检测
                new ActionNode(() =>
                {
                    if (HasMainHandLure(_hProcess, _playerBase))
                    {
                        _isApplyingLure = false;
                        return NodeState.Success; // 已有鱼饵，静默放行
                    }

                    if (_isApplyingLure)
                    {
                        if (DateTime.Now < _lureBufferTime || IsUnitCasting(_hProcess, _playerBase))
                        {
                            return NodeState.Running;
                        }
                        else
                        {
                            _isApplyingLure = false;
                            _nextLureTime = DateTime.Now.AddMilliseconds(3000);
                            LogOnce("涂抹鱼饵失败，进入 3 秒防卡死冷却...");
                            return NodeState.Running;
                        }
                    }

                    if (IsUnitCasting(_hProcess, _playerBase)) return NodeState.Success;
                    if (DateTime.Now < _nextLureTime) return NodeState.Running;

                    int fishingLevel = GetFishingSkillLevel(_hProcess, _playerBase);
                    int targetLureId = 0;

                    if (fishingLevel >= 100 && GetItemCount(_hProcess, 6533) > 0) targetLureId = 6533;
                    else if (fishingLevel >= 100 && GetItemCount(_hProcess, 6532) > 0) targetLureId = 6532;
                    else if (fishingLevel >= 50 && GetItemCount(_hProcess, 6530) > 0) targetLureId = 6530;
                    else if (fishingLevel >= 0 && GetItemCount(_hProcess, 6529) > 0) targetLureId = 6529;

                    if (targetLureId == 0) return NodeState.Success;

                    var allItems = GetAllInventoryItems(_hProcess);
                    var lureItem = allItems.Find(i => i.ItemId == targetLureId);

                    if (lureItem != null)
                    {
                        LogOnce($"发现可用鱼饵 (ID:{targetLureId})，正在涂抹...");
                        string luaCmd = $"UseContainerItem({lureItem.BagId}, {lureItem.SlotId}); PickupInventoryItem(16); ReplaceEnchant();";
                        ExecuteDynamicLua(luaCmd);

                        _isApplyingLure = true;
                        _lureBufferTime = DateTime.Now.AddMilliseconds(500);
                    }

                    return NodeState.Running;
                }),

                new WaitNode(100),

                // 抛竿逻辑
                new ActionNode(() =>
                {
                    if (IsUnitCasting(_hProcess, _playerBase)) return NodeState.Success;
                    if (DateTime.Now < _castBufferTime) return NodeState.Running;
                    if (DateTime.Now < _nextCastTime) return NodeState.Running;

                    LogOnce("执行抛竿动作...");
                    ExecuteDynamicLua("CastSpellByName('钓鱼');");

                    _castBufferTime = DateTime.Now.AddMilliseconds(300);
                    _nextCastTime = DateTime.Now.AddMilliseconds(3000);

                    return NodeState.Running;
                }),

                new WaitNode(100),

                // 收杆逻辑
                new ActionNode(() =>
                {
                    if (!IsUnitCasting(_hProcess, _playerBase))
                    {
                        _currentBobberBase = 0;
                        _hookTime = DateTime.MinValue;

                        // 脱钩熔断时，手动打印并清空日志缓存，让下一竿的日志能正常触发
                        if (_lastLogMessage != "钓鱼结束 (超时/脱钩/打断)，准备重新抛竿。")
                        {
                            Logger.Write($"[{CurrentPlayerName}] [自动钓鱼] 钓鱼结束 (超时/脱钩/打断)，准备重新抛竿。");
                            _lastLogMessage = "";
                        }
                        return NodeState.Failure;
                    }

                    if (_currentBobberBase == 0)
                    {
                        _currentBobberBase = GetPlayerBobberBase(_hProcess, _playerBase);
                        if (_currentBobberBase == 0)
                        {
                            LogOnce("等待鱼漂浮出水面...");
                            return NodeState.Running;
                        }
                        else
                        {
                            LogOnce($"成功锁定鱼漂 (基址: 0x{_currentBobberBase:X})，盯防咬钩中...");
                        }
                    }

                    if (_hookTime != DateTime.MinValue)
                    {
                        if (DateTime.Now >= _hookTime)
                        {
                            LogOnce("触发右键提竿！");
                            RightClickGameObject(_currentBobberBase);
                            _currentBobberBase = 0;
                            _hookTime = DateTime.MinValue;
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }

                    if (IsBobberBiting(_hProcess, _currentBobberBase))
                    {
                        int delay = _rnd.Next(100, 501); // 获取随机延迟
                        LogOnce($"鱼咬钩了！模拟人类反应，延迟 {delay} 毫秒后提竿...");
                        _hookTime = DateTime.Now.AddMilliseconds(delay);
                        return NodeState.Running;
                    }

                    return NodeState.Running;
                }),

                new WaitNode(100),

                // 结算抛竿次数
                new ActionNode(() => {
                    _Fishing_attempts_Count++;
                    Logger.Write($"[{CurrentPlayerName}] [自动钓鱼] 成功提竿，当前累计次数: {_Fishing_attempts_Count}");

                    // 核心：成功结算后，清空日志缓存，为下一轮循环重置拦截器
                    _lastLogMessage = "";
                    return NodeState.Success;
                }),

                // 动态等待节点：死盯内存状态，直到引导条彻底消失
                new ActionNode(() =>
                {
                    if (IsUnitCasting(_hProcess, _playerBase))
                    {
                        LogOnce("等待引导条彻底消失与网络同步...");
                        return NodeState.Running;
                    }
                    return NodeState.Success;
                }),

                new WaitNode(800)
            );
        }


        public override string OnTick(IntPtr hProcess, int playerBase)
        {
            // 1. 更新句柄
            _hProcess = hProcess;
            _playerBase = playerBase;

            // 1. IsInWorld 绝对物理锁（防刷屏动态拦截版）
            if (!IsInWorld(_hProcess) || _playerBase == 0)
            {
                if (_wasInWorld)
                {
                    Logger.Write($"[{CurrentPlayerName}] [智能检测] 蓝条加载中...");
                    _wasInWorld = false; // 瞬间闭锁，后续几百帧蓝条期间绝不再打印这行日志
                }

                return "蓝条加载中";
            }

            // 2. 捕捉从蓝条出来，落地站稳的第一个瞬间（上升沿触发）
            if (!_wasInWorld)
            {
                Logger.Write($"[{CurrentPlayerName}] [智能检测] 蓝条加载完毕，启动 5 秒场景缓冲等待...");

                // 设定 5 秒倒计时
                _resumeTimeAfterLoad = DateTime.Now.AddMilliseconds(5000);
                _isWaitingForWorldLoad = true; // 进入缓冲等待状态
                _wasInWorld = true;
            }

            // 3. 5 秒非阻塞保护期拦截
            if (_isWaitingForWorldLoad)
            {
                // 如果当前时间还没到我们设定的 5 秒后
                if (DateTime.Now < _resumeTimeAfterLoad)
                {
                    return "场景缓冲中";
                }
                else
                {
                    // 5 秒时间到了，执行真正的清空与重置动作
                    Logger.Write($"[{CurrentPlayerName}] [智能检测] 缓冲完毕，已自动清空行为树历史记录防错乱！");
                    this.PauseGlobalAutoLogin = false;
                    bool Comatose_state = Unit_Behavioral_State(_hProcess, _playerBase, 18);

                    if (Comatose_state)
                    {
                        // 蓝条加载后昏迷，说明又触发了小退，此时解除小退
                        ExecuteDynamicLua("ZeroBot_IsEnteringWorld = nil; ZeroBot_HasRequestedLogin = nil; StaticPopup_Hide('CAMP');");
                    }
                    else
                    {
                        // 蓝条加载后没有昏迷
                        ExecuteDynamicLua("ZeroBot_IsEnteringWorld = nil; ZeroBot_HasRequestedLogin = nil;");
                    }

                    // 缓冲结束，关闭保护锁，放行后续逻辑
                    _isWaitingForWorldLoad = false;
                }
            }

            // 每次心跳看一眼名字有没有拿到，没拿到就去内存读一次，读到了就永远缓存
            if (CurrentPlayerName == "未知角色" && _hProcess != IntPtr.Zero)
            {
                CurrentPlayerName = GetPlayerName(_hProcess);
            }


            // 获取当前背包空位数
            int freeSlots = GetFreeBagSlots(_hProcess, _playerBase);

            // 当剩余空位 ≤ 1 时，认为背包空间不足，停止脚本
            if (freeSlots <= 1)
            {
                return "背包已满！";  // 根据你实际返回类型调整
            }

            // 2. 采集当前环境数据
            int descriptors = MemoryAPI.ReadInteger(_hProcess, _playerBase + 0x08);
            int currentHp = MemoryAPI.ReadInteger(_hProcess, descriptors + 0x58);
            bool isInCombat = Unit_Behavioral_State(_hProcess, _playerBase, 19);

            // 1. 鬼魂状态检测（无视血量）
            if (HasGhostDebuff(_hProcess, _playerBase))
            {
                // 可以在这里加个限流日志，避免疯狂刷屏
                // Logger.Write("角色处于跑尸状态，脚本挂起中...");
                return "角色已死亡";
            }

            if (currentHp == 0)
            {
                // Logger.Write("角色已死亡，脚本挂起中...");
                return "角色已死亡";
            }

            // 3. 战斗状态检测
            if (isInCombat)
            {
                // Logger.Write("角色处于战斗状态，脚本挂起中...");
                return "角色处于战斗状态";
            }


            // 3. 将执行权交给最高指挥官！
            if (_rootTree != null)
            {
                NodeState state = _rootTree.Evaluate();
                if (state == NodeState.Running) return "运行中";
            }

            return "空闲";
        }


    }


}
