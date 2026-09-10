using System;
using System.Collections.Generic;
using System.Diagnostics;
using Zero.Core;

namespace Zero.Core
{
    // 1. 定义灵魂三状态
    public enum NodeState
    {
        Running,
        Success,
        Failure
    }

    // 2. 所有节点的抽象基类
    public abstract class Node
    {
        public NodeState State { get; protected set; }
        public abstract NodeState Evaluate();

        // 所有节点通用的重置接口（灌孟婆汤）
        public virtual void Reset()
        {
            State = NodeState.Running;
        }
    }

    // ==========================================
    // 3. 复合节点：顺序执行器 (Sequence) 
    // ==========================================
    public class Sequence : Node
    {
        private List<Node> _nodes = new List<Node>();
        private int _currentNodeIndex = 0;

        public Sequence(params Node[] nodes) { _nodes.AddRange(nodes); }

        public override NodeState Evaluate()
        {
            while (_currentNodeIndex < _nodes.Count)
            {
                NodeState childState = _nodes[_currentNodeIndex].Evaluate();

                switch (childState)
                {
                    case NodeState.Running:
                        State = NodeState.Running;
                        return State;

                    case NodeState.Failure:
                        _currentNodeIndex = 0;
                        State = NodeState.Failure;
                        return State;

                    case NodeState.Success:
                        _currentNodeIndex++;
                        break;
                }
            }

            _currentNodeIndex = 0;
            State = NodeState.Success;
            return State;
        }

        // Sequence专属的重置逻辑（清空进度，并重置所有子节点）
        public override void Reset()
        {
            base.Reset();
            _currentNodeIndex = 0;
            foreach (var node in _nodes)
            {
                node.Reset();
            }
        }
    }

    // ==========================================
    // 4. 动作节点：执行具体代码 (ActionNode)
    // ==========================================
    public class ActionNode : Node
    {
        private Func<NodeState> _action;
        public ActionNode(Func<NodeState> action) { _action = action; }

        public override NodeState Evaluate() => _action();
    }

    // ==========================================
    // 5. 神级节点：等待器 (WaitNode)
    // ==========================================
    public class WaitNode : Node
    {
        private int _ms;
        private DateTime _endTime;
        private bool _started = false;

        public WaitNode(int ms) { _ms = ms; }

        public override NodeState Evaluate()
        {
            if (!_started)
            {
                _endTime = DateTime.Now.AddMilliseconds(_ms);
                _started = true;
            }

            if (DateTime.Now < _endTime)
            {
                return NodeState.Running;
            }

            _started = false;
            return NodeState.Success;
        }

        // WaitNode专属重置逻辑（打断闹钟）
        public override void Reset()
        {
            base.Reset();
            _started = false;
        }
    }

    // ==========================================
    // 6. 专属定制节点：全自动打扫战场 (AutoLootNode)
    // ==========================================
    public class AutoLootNode : Node
    {
        private DLLManager _core;
        private Func<IntPtr> _getProcess;
        private Func<int> _getPlayer;

        // 动态获取拾取半径的委托
        private Func<float> _getRadius;

        private HashSet<ulong> _lootedCorpses = new HashSet<ulong>();
        private int _lootState = 0;
        private DateTime _waitTimer;

        private int _targetBase;
        private ulong _targetGuid;
        private float _tX, _tY, _tZ;

        // 构造函数中增加 getRadius 参数
        public AutoLootNode(DLLManager Core, Func<IntPtr> getProcess, Func<int> getPlayer, Func<float> getRadius)
        {
            _core = Core;
            _getProcess = getProcess;
            _getPlayer = getPlayer;
            _getRadius = getRadius; // 赋值
        }

        public override NodeState Evaluate()
        {
            IntPtr hProcess = _getProcess();
            int playerBase = _getPlayer();

            // 每帧动态获取当前的拾取范围
            float currentRadius = _getRadius();

            switch (_lootState)
            {
                case 0:
                    // 将原本写死的 15.0f 替换为 currentRadius 参数
                    if (_core.FindNearestCorpse(hProcess, playerBase, currentRadius, _lootedCorpses, out _targetBase, out _targetGuid, out _tX, out _tY, out _tZ))
                    {
                        _lootState = 1;
                        return NodeState.Running;
                    }
                    else
                    {
                        _lootedCorpses.Clear();
                        _lootState = 0;
                        return NodeState.Success;
                    }

                case 1:
                    float myX = MemoryAPI.ReadFloat(hProcess, playerBase + 0x9B8);
                    float myY = MemoryAPI.ReadFloat(hProcess, playerBase + 0x9BC);
                    float dist = (float)Math.Sqrt(Math.Pow(myX - _tX, 2) + Math.Pow(myY - _tY, 2));

                    if (dist <= 2.0f)
                    {
                        _core.StopMovement(hProcess, playerBase);
                        _waitTimer = DateTime.Now.AddMilliseconds(100);
                        _lootState = 2;
                    }
                    else
                    {
                        _core.MoveTo(hProcess, playerBase, _tX, _tY, _tZ, 2.0f);
                    }
                    return NodeState.Running;

                case 2:
                    if (DateTime.Now >= _waitTimer)
                    {
                        _core.RightClickUnit((uint)_targetBase);
                        _lootedCorpses.Add(_targetGuid);
                        _waitTimer = DateTime.Now.AddMilliseconds(1000);
                        _lootState = 3;
                    }
                    return NodeState.Running;

                case 3:
                    if (DateTime.Now >= _waitTimer)
                    {
                        _lootState = 0;
                    }
                    return NodeState.Running;
            }

            return NodeState.Failure;
        }

        public override void Reset()
        {
            base.Reset();
            _lootState = 0;
            _lootedCorpses.Clear();
        }
    }

    // ==========================================
    // 7. 复合节点：选择器 / 备用方案 (Selector)
    // ==========================================
    public class Selector : Node
    {
        private List<Node> _nodes = new List<Node>();
        public Selector(params Node[] nodes) { _nodes.AddRange(nodes); }

        public override NodeState Evaluate()
        {
            foreach (var node in _nodes)
            {
                switch (node.Evaluate())
                {
                    case NodeState.Failure:
                        continue;
                    case NodeState.Success:
                        State = NodeState.Success;
                        return State;
                    case NodeState.Running:
                        State = NodeState.Running;
                        return State;
                }
            }
            State = NodeState.Failure;
            return State;
        }

        // Selector专属的重置逻辑（向所有子节点广播重置指令）
        public override void Reset()
        {
            base.Reset();
            foreach (var node in _nodes)
            {
                node.Reset();
            }
        }
    }

    // ==========================================
    // 8. 条件节点：判断真假 (ConditionNode)
    // ==========================================
    public class ConditionNode : Node
    {
        private Func<bool> _condition;
        public ConditionNode(Func<bool> condition) { _condition = condition; }

        public override NodeState Evaluate()
        {
            return _condition() ? NodeState.Success : NodeState.Failure;
        }
    }


    // ========================================================
    // 9. 三参数双向智能断点续传寻路节点 (SmartPathNode)
    // ========================================================
    public class SmartPathNode : Node
    {
        private DLLManager _script;
        private Func<IntPtr> _getProcess;
        private Func<int> _getPlayer;
        private List<Waypoint> _waypoints;

        // 核心控制状态锁
        private bool _isCorpseRun;  // 是否为跑尸模式（激活智能雷达）
        private bool _forceReverse; // 是否强制反转路径（用于活着回城清包）

        private int _currentIndex = 0;
        private bool _isInitialized = false;
        private bool _isReversed = false;
        private int _lastLoggedIndex = -1; // 防高频刷屏日志锁

        // 🚀 构造函数：新增 forceReverse 参数，默认不强制反转
        public SmartPathNode(DLLManager script, Func<IntPtr> getProcess, Func<int> getPlayer, List<Waypoint> waypoints, bool isCorpseRun = false, bool forceReverse = false)
        {
            _script = script;
            _getProcess = getProcess;
            _getPlayer = getPlayer;
            _waypoints = waypoints;
            _isCorpseRun = isCorpseRun;
            _forceReverse = forceReverse; // 接收强制反转信号
        }

        public override NodeState Evaluate()
        {
            IntPtr hProcess = _getProcess();
            int playerBase = _getPlayer();

            if (_waypoints == null || _waypoints.Count == 0) return NodeState.Success;

            // =========================================================
            // 航点断点续传与智能方向初始化
            // =========================================================
            if (!_isInitialized)
            {
                float myX = MemoryAPI.ReadFloat(hProcess, playerBase + 0x9B8);
                float myY = MemoryAPI.ReadFloat(hProcess, playerBase + 0x9BC);

                // 1. 扫描离当前灵魂/活人最近的断点索引
                float minDistance = float.MaxValue;
                int closestIndex = 0;

                for (int i = 0; i < _waypoints.Count; i++)
                {
                    float dist = (float)Math.Sqrt(Math.Pow(myX - _waypoints[i].X, 2) + Math.Pow(myY - _waypoints[i].Y, 2));
                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        closestIndex = i;
                    }
                }

                _currentIndex = closestIndex;

                // 2. 🚀 根据三大业务场景，精准分流寻路行驶方向
                if (_forceReverse)
                {
                    // 场景 3：活着回城清包，无条件锁死为反向行驶模式
                    _isReversed = true;
                }
                else if (_isCorpseRun && _waypoints.Count > 1)
                {
                    // 场景 2：智能跑尸模式，启用【全局宏观端点对撞雷达】
                    _isReversed = false; // 默认正向冲锋

                    float cx = MemoryAPI.ReadFloat(hProcess, _script.ModuleBaseAddress + 0x74E284);
                    float cy = MemoryAPI.ReadFloat(hProcess, _script.ModuleBaseAddress + 0x74E288);

                    // 只有成功读到真尸体坐标时才进行宏观测算，否则默认正向去副本门口
                    if (cx != 0 && cy != 0)
                    {
                        // 计算真尸体距离当前全长路线【绝对起点(0)】与【绝对终点(Count-1)】的直线跨度
                        float distToStart = (float)Math.Sqrt(Math.Pow(_waypoints[0].X - cx, 2) + Math.Pow(_waypoints[0].Y - cy, 2));
                        float distToEnd = (float)Math.Sqrt(Math.Pow(_waypoints[_waypoints.Count - 1].X - cx, 2) + Math.Pow(_waypoints[_waypoints.Count - 1].Y - cy, 2));

                        if (distToStart < distToEnd)
                        {
                            // 尸体离旅店更近 -> 确诊为旅店内被对立阵营暗杀，开启室内倒车模式
                            _isReversed = true;
                        }
                    }
                }
                else
                {
                    // 场景 1：活着去副本上班，常规正向行驶
                    _isReversed = false;
                }

                _isInitialized = true;
                _lastLoggedIndex = -1; // 激活日志触发器

                string dirStr = _isReversed ? "反向行驶中" : "正向行驶中";
                Logger.Write($"[{_script.CurrentPlayerName}] [智能寻路] 触发断点续传！节点: {_currentIndex + 1}/{_waypoints.Count} | 模式: {dirStr}");
            }

            // =========================================================
            // 边界触达判定：是否跑完全程
            // =========================================================
            if ((!_isReversed && _currentIndex >= _waypoints.Count) ||
                (_isReversed && _currentIndex < 0))
            {
                _isInitialized = false;
                _currentIndex = 0;
                _lastLoggedIndex = -1;
                return NodeState.Success;
            }

            // =========================================================
            // 动态精度自适应控制域
            // =========================================================
            var target = _waypoints[_currentIndex];
            float stopDist = (target.Type == PathType.Precise) ? 1.0f : 3.0f;

            // 无论何种模式，当前行驶路线的终点那一脚，必须实施精准 (1.0f) 刹车
            bool isLastPoint = (!_isReversed && _currentIndex == _waypoints.Count - 1) ||
                               (_isReversed && _currentIndex == 0);
            if (isLastPoint) stopDist = 1.0f;

            // 🚀 动态高精度单帧航点变更日志
            if (_currentIndex != _lastLoggedIndex)
            {
                string typeStr = (target.Type == PathType.Precise) ? "精确移动" : "模糊移动";
                if (isLastPoint) typeStr = "已到达终点";

                Logger.Write($"[{_script.CurrentPlayerName}] [智能寻路] 正在前往节点: {_currentIndex + 1}/{_waypoints.Count} | 坐标: ({target.X:F2}, {target.Y:F2}, {target.Z:F2}) | 判定精度: {stopDist:F1}码 ({typeStr})");
                _lastLoggedIndex = _currentIndex;
            }

            // 执行内存 CTM 物理移动发包
            bool reached = _script.MoveTo(hProcess, playerBase, target.X, target.Y, target.Z, stopDist);

            if (reached)
            {
                if (_isReversed)
                    _currentIndex--;
                else
                    _currentIndex++;

                // 预加载发包逻辑：实现拐弯处满速不减速流畅衔接
                if ((!_isReversed && _currentIndex < _waypoints.Count) ||
                    (_isReversed && _currentIndex >= 0))
                {
                    var nextTarget = _waypoints[_currentIndex];
                    float nextStopDist = (nextTarget.Type == PathType.Precise) ? 1.0f : 3.0f;

                    bool isNextLast = (!_isReversed && _currentIndex == _waypoints.Count - 1) ||
                                      (_isReversed && _currentIndex == 0);
                    if (isNextLast) nextStopDist = 1.0f;

                    _script.MoveTo(hProcess, playerBase, nextTarget.X, nextTarget.Y, nextTarget.Z, nextStopDist);
                }
            }

            return NodeState.Running;
        }

        // 重置黑板数据
        public override void Reset()
        {
            base.Reset();
            _isInitialized = false;
            _currentIndex = 0;
            _lastLoggedIndex = -1;
        }
    }

    // ==========================================
    // 10. 限时引导监控节点 (ChannelLimitNode)
    // 逻辑：既监控内存状态，又带有超时强制放行功能
    // ==========================================
    public class ChannelLimitNode : Node
    {
        private Func<bool> _isChanneling;
        private int _maxMs;
        private DateTime _endTime;
        private bool _started = false;

        public ChannelLimitNode(Func<bool> isChanneling, int maxMs)
        {
            _isChanneling = isChanneling;
            _maxMs = maxMs;
        }

        public override NodeState Evaluate()
        {
            if (!_started)
            {
                // 第一次进入时，上好最大引导时间的闹钟
                _endTime = DateTime.Now.AddMilliseconds(_maxMs);
                _started = true;
            }

            // 情况 1：内存显示引导已经结束了（下完了，或者被怪拍晕打断了），提前放行！
            if (!_isChanneling())
            {
                _started = false;
                return NodeState.Success;
            }

            // 情况 2：内存显示还在引导，但是你规定的时间（比如 6 秒）已经到了，强制放行！
            if (DateTime.Now >= _endTime)
            {
                _started = false;
                return NodeState.Success;
            }

            // 情况 3：正在引导，且时间还没到，继续死卡在这里！
            return NodeState.Running;
        }
        public override void Reset()
        {
            base.Reset();
            _started = false;
        }
    }

    // ==========================================
    // 11. 强制起手确认节点 (EnsureCastNode)
    // 逻辑：不断尝试发包，直到内存确认技能真正放出来了才放行！
    // ==========================================
    public class EnsureCastNode : Node
    {
        private Action _castAction;
        private Func<bool> _checkAction;
        private int _timeoutMs;
        private DateTime _startTime;
        private DateTime _lastCastTime;
        private bool _started = false;

        public EnsureCastNode(Action castAction, Func<bool> checkAction, int timeoutMs = 3000)
        {
            _castAction = castAction;
            _checkAction = checkAction;
            _timeoutMs = timeoutMs; // 超时保护，防止没蓝了死循环
        }

        public override NodeState Evaluate()
        {
            if (!_started)
            {
                _startTime = DateTime.Now;
                _lastCastTime = DateTime.MinValue;
                _started = true;
            }

            // 1. 【完美起手】：如果内存显示已经在施法/引导了，说明包发成功了！立刻放行！
            if (_checkAction())
            {
                _started = false;
                return NodeState.Success;
            }

            // 2. 【超时保护】：如果重试了 N 秒都没放出来（可能被沉默、没蓝、卡视野），必须打断，防止死循环！
            if ((DateTime.Now - _startTime).TotalMilliseconds > _timeoutMs)
            {
                _started = false;
                return NodeState.Failure; // 起手彻底失败
            }

            // 3. 【节流重发】：如果还没起手，我们就补发指令！
            // 但是绝对不能一秒钟发 60 次（会掉线），我们设置每 500 毫秒补发一次！
            if ((DateTime.Now - _lastCastTime).TotalMilliseconds > 500)
            {
                _castAction();
                _lastCastTime = DateTime.Now;
            }

            // 告诉行为树：还没成功，继续卡在这里！
            return NodeState.Running;
        }

        public override void Reset()
        {
            base.Reset();
            _started = false;
        }
    }


    /// <summary>
    /// 智能摧毁垃圾节点：动态读取 Json 配置文件，结合离线数据库自动粉碎垃圾 (支持热更新)
    /// </summary>
    public class DestroyGarbageNode : Node
    {
        private DLLManager _script;
        private Func<IntPtr> _hProcessFunc;
        private Func<int> _playerBaseFunc;

        // 【内部状态独立封装】
        private bool isDestroyingInitialized = false;
        private int destroyIndex = 0;
        private DateTime nextDestroyTime = DateTime.MinValue;
        private List<InventoryItem> itemsToDestroy = null;

        // 构造函数不再需要传入死板的 List<int> garbageIds
        public DestroyGarbageNode(DLLManager script, Func<IntPtr> hProcessFunc, Func<int> playerBaseFunc)
        {
            _script = script;
            _hProcessFunc = hProcessFunc;
            _playerBaseFunc = playerBaseFunc;
        }

        public override NodeState Evaluate()
        {
            IntPtr hProcess = _hProcessFunc();

            if (!isDestroyingInitialized)
            {
                // 1. 动态获取当前角色名字
                //string playerName = _script.GetPlayerName(hProcess);

                // 2. 读取对应配置 (热更新：每次触发都会重新读取 json)
                //AccountConfig config = ConfigManager.GetConfig(playerName);
                // 🌟 核心修改：用基类保存的 AccountName 去 JSON 里精准拉取配置！
                AccountConfig config = ConfigManager.GetConfig(_script.AccountName) ?? new AccountConfig();

                // 3. 将 json 中的 Destruction 字符串 (如"蛮荒之叶,破损的短剑") 转化为物品 ID 集合
                // 利用你写好的神级辅助方法，效率极高！
                HashSet<int> garbageIds = ItemDbManager.ParseNamesToIds(config.Destruction);

                /*
                // ================== 👇 插入调试日志 👇 ==================
                Debug.WriteLine($"[清包排查] 1. 当前请求的 AccountName: '{_script.AccountName}'");
                Debug.WriteLine($"[清包排查] 2. 读取到的原始配置字符串: '{config.Destruction}'");
                Debug.WriteLine($"[清包排查] 3. 解析出的黑名单ID数量: {garbageIds.Count}");
                if (garbageIds.Count > 0)
                {
                    Debug.WriteLine($"[清包排查] 3.1 具体的ID列表: {string.Join(", ", garbageIds)}");
                }
                // ========================================================
                */

                // 4. 读取背包全部物品
                List<InventoryItem> allItems = _script.GetAllInventoryItems(hProcess);

                // 5. 核心筛选：如果物品 ID 在我们的垃圾黑名单里，就加入摧毁队列
                itemsToDestroy = allItems.FindAll(item => garbageIds.Contains(item.ItemId));

                destroyIndex = 0;
                isDestroyingInitialized = true;
                nextDestroyTime = DateTime.Now;

                if (itemsToDestroy.Count == 0)
                {
                    isDestroyingInitialized = false;
                    // 包里没垃圾，直接返回 Success 放行
                    Logger.Write($"[{_script.CurrentPlayerName}] [清包系统] 扫描完毕，未发现需要摧毁的垃圾，直接放行。");
                    return NodeState.Success;
                }

                Logger.Write($"[{_script.CurrentPlayerName}] [清包系统] 根据账号 [{_script.AccountName}] 的配置，扫描到 {itemsToDestroy.Count} 件垃圾，启动原地摧毁！");
            }

            // 垃圾摧毁完毕
            if (destroyIndex >= itemsToDestroy.Count)
            {
                isDestroyingInitialized = false;
                Logger.Write($"[{_script.CurrentPlayerName}] [清包系统] 垃圾已成功摧毁完毕，空间已腾出！");
                return NodeState.Success;
            }

            // 节流阀：控制摧毁频率 (0.5秒摧毁一个，防止与服务器交互过快)
            if (DateTime.Now < nextDestroyTime) return NodeState.Running;

            var currentItem = itemsToDestroy[destroyIndex];

            // 友好的日志输出：不仅输出 ID，如果数据库里有名字，就把名字也打出来
            string itemName = ItemDbManager.IdMap.ContainsKey(currentItem.ItemId)
                              ? ItemDbManager.IdMap[currentItem.ItemId].Chinese_Name
                              : "未知物品";

            Logger.Write($"[{_script.CurrentPlayerName}] [清包系统] 正在摧毁: {itemName} (ID: {currentItem.ItemId}, Bag: {currentItem.BagId}, Slot: {currentItem.SlotId})");

            // 调用底层无视确认框的摧毁指令
            //_script.DestroyItem(currentItem.BagId, currentItem.SlotId);
            _script.ExecuteDynamicLua($"PickupContainerItem({currentItem.BagId}, {currentItem.SlotId}); DeleteCursorItem();");

            destroyIndex++;
            nextDestroyTime = DateTime.Now.AddMilliseconds(500);

            // 活没干完，化身叹息之墙霸占执行流
            return NodeState.Running;
        }

        public override void Reset()
        {
            isDestroyingInitialized = false;
            destroyIndex = 0;
            itemsToDestroy = null;
            base.Reset();
        }
    }

    /// <summary>
    /// 副本前置双盾准备节点：智能下马 -> 施放法术23 -> 间隔1.6s -> 施放法术32 -> 重新上马
    /// </summary>
    public class PreDungeonShieldNode : Node
    {
        private DLLManager _script;
        private Func<IntPtr> _hProcessFunc;
        private Func<int> _playerBaseFunc;

        // 状态机核心控制变量
        private int _state = 0;
        private DateTime _waitTimer = DateTime.MinValue;

        public PreDungeonShieldNode(DLLManager script, Func<IntPtr> hProcessFunc, Func<int> playerBaseFunc)
        {
            _script = script;
            _hProcessFunc = hProcessFunc;
            _playerBaseFunc = playerBaseFunc;
        }

        public override NodeState Evaluate()
        {
            IntPtr hProcess = _hProcessFunc();
            int playerBase = _playerBaseFunc();

            // 如果当前阶段的等待时间还没到，直接继续保持 Running，绝不卡死主线程
            if (DateTime.Now < _waitTimer) return NodeState.Running;

            switch (_state)
            {
                case 0: // 【阶段 0：检查并下马】
                        // 动态调用你亲手挖出来的 0x214 坐骑检测逻辑
                    if (_script.IsMounted(hProcess, playerBase))
                    {
                        Logger.Write($"[{_script.CurrentPlayerName}] [自动坐骑] 检测到处于骑乘状态，执行下马操作...");
                        _ = _script.Use_Mount(50); // 骑乘状态下按 0 键会自动下马
                        _waitTimer = DateTime.Now.AddMilliseconds(800); // 留出 0.8 秒给客户端动画与内存同步
                        return NodeState.Running;
                    }

                    // 如果已经不在马上（比如提前被怪打下来了），直接无缝进入套盾阶段
                    _state = 1;
                    return NodeState.Running;

                case 1: // 【阶段 1：套第一个盾】
                    Logger.Write($"[{_script.CurrentPlayerName}] [自动坐骑] 正在施放第一个盾 (寒冰护体)...");
                    _script.ExecuteDynamicLua("CastSpellByName('寒冰护体');");

                    // 核心需求：间隔 1.6 秒
                    _waitTimer = DateTime.Now.AddMilliseconds(1600);
                    _state = 2; // 推进到下一个盾
                    return NodeState.Running;

                case 2: // 【阶段 2：套第二个盾】
                    Logger.Write($"[{_script.CurrentPlayerName}] [自动坐骑] 正在施放第二个盾 (法力护盾)...");
                    _script.ExecuteDynamicLua("CastSpellByName('法力护盾');");

                    // 施放完第二个盾，也需要等 1.6 秒公共CD转完才能读条上马
                    _waitTimer = DateTime.Now.AddMilliseconds(1600);
                    _state = 3; // 推进到上马阶段
                    return NodeState.Running;

                case 3: // 【阶段 3：重新上马】
                    Logger.Write($"[{_script.CurrentPlayerName}] [自动坐骑] 双盾套好，正在重新召唤坐骑...");
                    // [新增] 施法前，给防检测系统套上4秒的“保护罩”，防止平移打断技能
                    _script._pauseAntiBotUntil = DateTime.Now.AddMilliseconds(4000);
                    Logger.Write($"[{_script.CurrentPlayerName}] [战斗系统] 准备引怪，暂时屏蔽防检测系统 4 秒...");
                    _ = _script.Use_Mount(50); // 再次使用坐骑物品上马

                    //  坐骑召唤读条是 3 秒，留 3.5 秒确保读条完毕
                    _waitTimer = DateTime.Now.AddMilliseconds(3500);
                    _state = 4; // 推进到最后的确认阶段
                    return NodeState.Running;

                case 4: // 【阶段 4：确认放行】
                        // 移除强制骑乘校验。若被怪打断（已进战斗），系统本来就无法上马，
                        // 直接放行让其步行冲门，完美避开死循环陷阱！
                    Logger.Write($"[{_script.CurrentPlayerName}] [自动坐骑] 准备流程结束（无论是否上马成功），放行进本！");
                    _state = 0;
                    return NodeState.Success;
            }

            return NodeState.Failure;
        }

        // 当行为树被外部紧急打断或重置时调用
        public override void Reset()
        {
            _state = 0;
            _waitTimer = DateTime.MinValue;
            base.Reset();
        }

        /// <summary>
        /// 状态拦截器 (秘书节点)：专门防止战斗循环中的 Failure 导致主引擎崩溃重启
        /// </summary>
        public class CombatLoopNode : Node
        {
            private Node _combatLogic;

            public CombatLoopNode(Node combatLogic)
            {
                _combatLogic = combatLogic;
            }

            public override NodeState Evaluate()
            {
                NodeState state = _combatLogic.Evaluate();

                // 只有真正通关（大王死亡），才向主引擎放行！
                if (state == NodeState.Success)
                    return NodeState.Success;

                // 内部的任何循环、失败、重置，统统拦截，告诉主引擎“还在执行中”！
                return NodeState.Running;
            }
        }

    }

}