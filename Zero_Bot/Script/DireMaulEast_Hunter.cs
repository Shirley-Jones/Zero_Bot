using Newtonsoft.Json; // 引入 Json 库
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Zero.Core;
using static System.Windows.Forms.AxHost;
using static Zero.Core.PathType;

namespace Zero.Script
{
    public class DireMaulEast_Hunter : DLLManager
    {
        public override string ScriptName => "乌龟服厄运东猎人刷花(这个是没有完善的版本不能用！)";

        private IntPtr _hProcess;
        private int _playerBase;
        private string _teamName;
        private int sellIndex = 0;
        private int _mapId;
        private int _lastMapId = 0;
        private int _activeGyRoute = 0;
        private int _bestWaterItemId = 0;
        private int _bestFoodItemId = 0;
        private bool _isDead;
        private bool _needGoHome;
        private bool _wasDead = false;
        private bool _isFirstStartup = true;
        private bool _needResetInstance = false;
        private bool _isTransitioning = false;
        private bool _hasReleasedSpirit = false;
        private bool isSellingInitialized = false;
        private bool _hasAttemptedSell = false;
        private bool _wasInWorld = true; // 记录上一帧是否在正常游戏世界中（用于捕获蓝条结束瞬间）
        public static string LastAction = "";
        public static float LastX = 0;
        public static float LastY = 0;
        // 新增全局状态标识，用于驱动乌龟服行为树分支
        private bool _attemptingTurtleBot = false;
        private DateTime lastEnterWorldTime = DateTime.MinValue;
        private DateTime _lastCombatItemUseTime = DateTime.MinValue;   // 战斗急救专用防抖锁，防止一帧内把内存发包塞满
        private DateTime _lastCastTime = DateTime.MinValue;     // 防施法 GCD 僵直
        private DateTime _lastItemUseTime = DateTime.MinValue;  // 防网络延迟连吃连喝
        private DateTime _lastAcceptTime = DateTime.MinValue;
        private DateTime _hearthstoneTimer = DateTime.MinValue;
        private DateTime _resetTimer = DateTime.MinValue;
        private DateTime WaitTimer = DateTime.MinValue;
        private DateTime _lastDrinkTime = DateTime.MinValue;
        private DateTime _lastFoodTime = DateTime.MinValue;
        private DateTime _lastZijiuPrintTime = DateTime.MinValue;
        private DateTime _transitionFailsafe = DateTime.MinValue;
        private DateTime nextSellTime = DateTime.MinValue;
        private DateTime _lastClassWarnTime = DateTime.MinValue;
        private DateTime _nextCtmTime = DateTime.MinValue;
        private DateTime _lastLevelWarnTime = DateTime.MinValue;
        private DateTime _lastSkillWarnTime = DateTime.MinValue;
        private DateTime _releaseSpiritTime = DateTime.MinValue; // 记录释放灵魂的确切时间
        private bool _needStartupPrep = false;       // 首次启动野外补给/上马总开关
        private DateTime _startupPrepTimer = DateTime.MinValue; // 首次上马防死锁安全沙盒倒计时
        private string _cachedBuildVersion = null;  // 声明一个私有变量用来缓存版本号
        private int _Dungeon_attempts_Count = 0;
        // 用于记录上一次往悬崖底部发 CTM 的时间
        private DateTime _lastCliffCtmTime = DateTime.MinValue;

        //邮寄相关变量
        private bool _hasClickedMailbox = false; // 新增：记录是否已经点过邮箱
        private bool isMailingInitialized = false;
        private List<InventoryItem> itemsToMail = new List<InventoryItem>();
        private int mailIndex = 0;
        private int mailSubState = 0; // 0=放物品, 1=点发送
        private DateTime nextMailStepTime = DateTime.MinValue;
        private DateTime _robotWaitTimeout = DateTime.MinValue; // 用于机器人召唤的超时控制
        private bool needToMailGold = false;
        private int copperToMail = 0;
        private bool _isWaitingToCloseMerchant = false; // 新增：用于修装备后的延迟收尾

        // ==========================================
        // --- 乘船系统：全局变量区 ---
        // ==========================================
        private const double BOAT_DOCK_TOTAL_TIME = 60000; // 船停靠的总时间 (60秒)
        private const double BOAT_SAFE_BOARD_TIME = 6000;  // 极限安全上船时间 (剩余不足6秒坚决不上)
        private bool _lastTickOnTransport = false; // 用于记录上一帧是否在交通工具上，防止上下船瞬间坐标系切换导致误报

        // 雷达区域状态（用于判断刚进范围时的未知情况）
        private bool _wasInFeralasRadarZone = false;

        // 雷达A：羽月要塞（岛上）专属
        private bool _isFeathermoonDocked = false;
        private DateTime _feathermoonDockTime = DateTime.MinValue;
        private bool _isFeathermoonTimeKnown = false; // 是否精准知道船停了多久

        // 雷达B：遗忘海岸（对岸/回城）专属
        private bool _isMainlandDocked = false;
        private DateTime _mainlandDockTime = DateTime.MinValue;
        private bool _isMainlandTimeKnown = false; // 是否精准知道船停了多久


        // 用于记录出蓝条后 5 秒倒计时的终点
        private DateTime _resumeTimeAfterLoad = DateTime.MinValue;
        // 用于标记当前是否正处于“刚出蓝条的等待保护期”
        private bool _isWaitingForWorldLoad = false;

        // 用于记录上一次跳跃起立的时间，防止卡 UI 和重复跳跃
        private DateTime _lastStandUpTime = DateTime.MinValue;
        

        // 在构建行为树、声明这个 ActionNode 的上方，先定义两个闭包变量（记忆体）
        int _boardState = 0;
        DateTime _boardWaitTimer = DateTime.MinValue;

        // 在构建该节点的外部作用域，声明下船专用的记忆体
        int _disembarkState = 0;
        DateTime _disembarkWaitTimer = DateTime.MinValue;

        // 声明一个随机数生成器
        private Random _antiBotRnd = new Random();

        // --- 备战系统全局变量 ---
        private bool _wasEatingOrDrinking = false; // 记录上一帧是否在吃喝

        // 记录下一次需要执行防封平移的时间，初始值为机器人启动后的 3~4 分钟
        private DateTime _nextAntiBotTime = DateTime.Now.AddMilliseconds(new Random().Next(180000, 240000));
        private DateTime _debugThrottle_110A = DateTime.MinValue;

        // 用于记录上一帧的坐标和地图，进行防 GM 瞬移检测
        private float _lastTickX = 0f;
        private float _lastTickY = 0f;
        private int _lastTickMapId = 0;

        // ==========================================
        // --- GM 防护系统日志防刷屏锁 ---
        // ==========================================
        private bool _hasLoggedFreeze = false;
        private bool _hasLoggedRadar = false;
        private bool _hasLoggedTeleport = false;

        public bool IsTurtleWoW
        {
            get
            {
                // 只有在第一次被调用时（也就是跑到第一波怪面前时），_cachedBuildVersion 才是 null
                if (_cachedBuildVersion == null)
                {
                    // 这时候去读内存，因为是跑到跟前才触发，绝对能读到！
                    _cachedBuildVersion = MemoryAPI.ReadString(_hProcess, ModuleBaseAddress + 0x437BFC, 16).Trim();
                    Logger.Write($"[{CurrentPlayerName}] [系统] 成功缓存服务器版本号: {_cachedBuildVersion}");
                }

                // 以后再调用，直接拿缓存的结果比对，不再读内存！
                return _cachedBuildVersion == "7272";
            }
        }

        /// <summary>
        /// 根据服务器版本动态等待不同的时间
        /// </summary>
        /// <param name="turtleWaitMs">乌龟服需要等待的毫秒数</param>
        /// <param name="vanillaWaitMs">原版5875需要等待的毫秒数</param>
        public Node WaitByServer(int turtleWaitMs, int vanillaWaitMs)
        {
            return new Selector(
                new Sequence(
                    new ActionNode(() => IsTurtleWoW ? NodeState.Success : NodeState.Failure),
                    new WaitNode(turtleWaitMs) // 如果是乌龟服，等这个时间
                ),
                new Sequence(
                    new WaitNode(vanillaWaitMs) // 如果是原版，等这个时间
                )
            );
        }

        List<InventoryItem> itemsToSell = null;

        
        public DireMaulEast_Hunter()
        {
            BuildTree();
        }

        private Node _rootTree; // 最高指挥官
        private Node _farmTree; // 副本内战斗逻辑
        private Node _runToInnTree_Horde;  //跑回旅店 部落路径
        private Node _runToInnTree_Alliance;  //跑回旅店 联盟路径
        private Node _runToInstanceTree_Horde;  //跑回副本 部落路径
        private Node _runToInstanceTree_Alliance;  //跑回副本 联盟路径


        /// <summary>
        /// 行为树节点工厂：生成【魔爆收尾 + 极限急救 + 智能补盾】节点
        /// </summary>
        public Node ArcaneExplosionNode()
        {
            return new ActionNode(() =>
            {
                if ((DateTime.Now - _lastCombatItemUseTime).TotalSeconds > 1.0)
                {
                    float hp = GetHealthPercent(_hProcess, _playerBase);
                    float mp = GetManaPercent(_hProcess, _playerBase);

                    bool itemUsed = false;

                    // ==========================================
                    // 1. 救命优先级：血量低于或等于 20%
                    // ==========================================
                    if (hp <= 20f)
                    {
                        int potionId = DetectHealingPotionGem(_hProcess);
                        if (potionId != 0 && !IsSpellOnCooldown(_hProcess, potionId))
                        {
                            Logger.Write($"[{CurrentPlayerName}] [战斗急救] 血量告急({hp}%)，且药水CD已好，自动使用治疗药水 (ID: {potionId})！");
                            UseItemByItemId(_hProcess, potionId);
                            _lastCombatItemUseTime = DateTime.Now;
                            itemUsed = true;
                        }
                    }

                    // ==========================================
                    // 2. 续航优先级：蓝量低于或等于 15% (宝石优先，药水兜底)
                    // ==========================================
                    if (!itemUsed && mp <= 15f)
                    {
                        bool manaRestored = false;

                        // 2.1 优先尝试使用【法力宝石】(白嫖回蓝，不共公共药水CD)
                        int gemId = DetectBestManaGem(_hProcess);
                        if (gemId != 0 && !IsSpellOnCooldown(_hProcess, gemId))
                        {
                            Logger.Write($"[{CurrentPlayerName}] [战斗急救] 蓝量告急({mp}%)，自动使用法力宝石 (ID: {gemId})！");
                            UseItemByItemId(_hProcess, gemId);
                            _lastCombatItemUseTime = DateTime.Now;
                            manaRestored = true; // 标记已经回过蓝了
                        }

                        // 2.2 如果宝石用完了，或者宝石正在CD中，则兜底使用【法力药水】
                        if (!manaRestored)
                        {
                            int manaPotionId = DetectManaPotionGem(_hProcess);
                            if (manaPotionId != 0 && !IsSpellOnCooldown(_hProcess, manaPotionId))
                            {
                                Logger.Write($"[{CurrentPlayerName}] [战斗急救] 蓝量告急({mp}%) 且宝石不可用，自动使用法力药水 (ID: {manaPotionId})！");
                                UseItemByItemId(_hProcess, manaPotionId);
                                _lastCombatItemUseTime = DateTime.Now;
                            }
                        }
                    }
                }

                // ==========================================
                // 3. 战斗输出与寻敌逻辑
                // ==========================================

                // 3.1 如果闹钟还没响（GCD或施法后摇还没转完），直接返回 Running 
                if (DateTime.Now < WaitTimer)
                {
                    return NodeState.Running;
                }

                // 3.2 启动双圈雷达！(25码警戒，10码杀伤)
                int detectCount, strikeCount;
                GetAliveMobsStatus(_hProcess, _playerBase, 25.0f, 10.0f, out detectCount, out strikeCount);

                // 3.3 核心决策树：战斗彻底结束判断
                if (detectCount == 0)
                {
                    return NodeState.Success; // 战斗彻底结束，放行
                }

                // 3.4 怪还没进内圈，继续死锁等待它们靠拢
                if (strikeCount == 0)
                {
                    return NodeState.Running;
                }

                // ==========================================
                // 3.5 战斗防护：智能补盾 (优先级高于魔爆输出)
                // ==========================================
                bool hasIceBarrier = HasUnitAura(_hProcess, _playerBase, 11426, 13031, 13032, 13033);
                if (!hasIceBarrier)
                {
                    bool isIceBarrierOnCd = IsSpellOnCooldown(_hProcess, 11426, 13031, 13032, 13033);
                    if (!isIceBarrierOnCd)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [战斗防护] 寒冰护体已破且冷却完毕，优先重新套盾！");
                        ExecuteDynamicLua("CastSpellByName('寒冰护体');");

                        // 套盾也会触发GCD，留出 1.6 秒公共冷却时间，下一帧进来再继续魔爆
                        WaitTimer = DateTime.Now.AddMilliseconds(1600);
                        return NodeState.Running;
                    }
                }

                // ==========================================
                // 3.6 范围内有怪，且不需要/无法套盾，疯狂魔爆！
                // ==========================================
                ExecuteDynamicLua("CastSpellByName('魔爆术');");
                WaitTimer = DateTime.Now.AddMilliseconds(1600); // 预留 GCD 等待时间

                return NodeState.Running;
            });
        }


        // 将第一波战斗封装成一个独立的方法，返回一个行为树节点
        public Node FirstWaveNode()
        {
            // 使用 Selector 作为分流器
            return new Selector(

                // ===============================================
                // 分支 A：Turtle WoW 专属逻辑
                // ===============================================
                new Sequence(
                    // 1. 守门员节点：运行时读取版本号！
                    new ActionNode(() =>
                    {
                        // 注意：由于这是放在 Lambda 表达式 ()=> 里面，所以它只会在真正打怪执行到这里时才去读内存！这时候绝对有句柄了！
                        string buildVersion = MemoryAPI.ReadString(_hProcess, ModuleBaseAddress + 0x437BFC, 16).Trim();

                        if (buildVersion == "7272")
                        {
                            // 验证通过，是Turtle WoW！返回 Success 放行，继续执行当前 Sequence 下面的动作
                            return NodeState.Success;
                        }

                        // 不是Turtle WoW！返回 Failure。
                        // 这样当前的这个 Sequence 就会被阻断，外层的 Selector 就会自动去尝试下一个分支（分支 B）
                        return NodeState.Failure;
                    }),

                    // 2. Turtle WoW 专属的战斗节点 (只有上面放行了才会执行到这里)
                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("DEFAULT_CHAT_FRAME:AddMessage('ZeroBot: 第一波开始，使用Turtle WoW 专属战斗逻辑');");
                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 第一波开始，使用Turtle WoW 专属战斗逻辑");
                        return NodeState.Success;
                    }),
                    new WaitNode(100),
                    new ActionNode(() =>
                    {
                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] Turtle WoW 跳过第一波的战斗！原因：战斗逻辑没写！");
                        return NodeState.Success;
                    })
                ),

                // ===============================================
                // 分支 B：通用原版逻辑（保底分支）
                // ===============================================
                new Sequence(
                    // 既然能走到这里，说明上面的乌龟服分支被阻断了（版本号不是 7272），直接执行通用逻辑
                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("DEFAULT_CHAT_FRAME:AddMessage('ZeroBot: 第一波开始，使用通用原版战斗逻辑');");
                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 第一波开始，使用通用原版战斗逻辑");
                        return NodeState.Success;
                    }),
                    new WaitNode(100),

                    new ActionNode(() =>
                    {
                        _ = Jump(50);
                        return NodeState.Success;
                    }),
                    new WaitNode(500),

                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("CastSpellByName('寒冰护体');");
                        return NodeState.Success;
                    }),

                    new WaitNode(1000),

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 129.31f, -229.66f, -56.89f, 0.5f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 129.19f, -225.80f, -56.86f, 0.5f))
                        {
                            StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new WaitNode(100),

                    new ActionNode(() =>
                    {
                        CastGroundAOE("暴风雪", 127.7943878f, -208.7475433f, -56.6986351f);  //暴风雪
                        return NodeState.Success;
                    }),

                    new WaitNode(7000),


                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("CastSpellByName('冰霜新星');");
                        return NodeState.Success;
                    }),

                    new WaitNode(1600),

                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("CastSpellByName('冰锥术');");
                        return NodeState.Success;
                    }),

                    new WaitNode(1600),

                    //执行奥爆+自动吃宝石+自动吃治疗药水
                    ArcaneExplosionNode(),

                    new WaitNode(100),

                    //拾取尸体
                    new AutoLootNode(this, () => _hProcess, () => _playerBase, () => 15.0f),
                    new WaitNode(100),

                    //先回到上次A怪点
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 130.28f, -234.59f, -56.89f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    })
                )
            );
        }


        /// <summary>
        /// 创建【智能动态吃喝】节点 (自动识别背包、自动优先级降级、人宠双层保护)
        /// </summary>
        public Node autoDrinkEatNode()
        {
            // 定义物资优先级矩阵 (ItemID 数组，按从高到低排序)
            // 数组前面的代表最高优先级，后面代表低级备用物资
            int[] foodPriorityList = new int[] { 22895, 8076, 8952 }; // 魔法肉桂面包 > 魔法甜面包 > 烤鹌鹑
            int[] waterPriorityList = new int[] { 8079, 8766, 8078 }; // 魔法晶水 > 晨露酒 > 魔法苏打水

            return new ActionNode(() =>
            {
                float hp = GetHealthPercent(_hProcess, _playerBase);
                float mp = GetManaPercent(_hProcess, _playerBase);

                // 1. 状态全满，亮绿灯安全放行
                if (hp > 99f && mp > 99f) return NodeState.Success;

                // 2. 状态未满，读取当前的吃喝状态和物品 GCD 状态
                GetEatDrinkStatus(_hProcess, _playerBase, out bool isEating, out bool isDrinking);
                bool isItemCooldown = (DateTime.Now - _lastItemUseTime).TotalSeconds < 1.6;

                if (!isItemCooldown)
                {
                    bool usedItem = false;

                    // ==========================================
                    // 🛑 【智能食物分配：动态降级扫描】
                    // ==========================================
                    if (hp <= 99f && !isEating)
                    {
                        int bestFoodIdToUse = 0;

                        // 顺着优先级链条从头往下扫描背包，揪出当前拥有的最高级食物
                        foreach (int itemId in foodPriorityList)
                        {
                            if (GetItemCount(_hProcess, itemId) > 0)
                            {
                                bestFoodIdToUse = itemId;
                                break; // 抓到了最高级的，立刻跳出循环！
                            }
                        }

                        if (bestFoodIdToUse != 0)
                        {
                            string foodName = bestFoodIdToUse == 22895 ? "魔法肉桂面包" :
                                              bestFoodIdToUse == 8076 ? "魔法甜面包" : "烤鹌鹑";

                            Logger.Write($"[{CurrentPlayerName}] [智能吃喝] 血量偏低 ({hp:F1}%)，智能检索背包，正在食用：【{foodName}】(ID: {bestFoodIdToUse})...");

                            UseItemByItemId(_hProcess, bestFoodIdToUse);
                            _lastItemUseTime = DateTime.Now;
                            usedItem = true;
                        }
                        else
                        {
                            // 背包里连一个食物都找不到了！
                            Logger.Write($"[{CurrentPlayerName}] [智能吃喝] 警告：血量不足，但在背包中没有找到任何有效的食物！");
                        }
                    }

                    // ==========================================
                    // 🛑 【智能法力水分配：动态降级扫描】
                    // ==========================================
                    // 注意：!usedItem 判定非常关键，1.12.1 中吃和喝不能在同一毫秒发包，否则必定被服务器吞掉一个
                    if (mp <= 99f && !isDrinking && !usedItem)
                    {
                        int bestWaterIdToUse = 0;

                        // 顺着优先级链条从头往下扫描背包，揪出当前拥有的最高级水
                        foreach (int itemId in waterPriorityList)
                        {
                            if (GetItemCount(_hProcess, itemId) > 0)
                            {
                                bestWaterIdToUse = itemId;
                                break; // 抓到了最高级的，立刻跳出循环！
                            }
                        }

                        if (bestWaterIdToUse != 0)
                        {
                            string waterName = bestWaterIdToUse == 8079 ? "魔法晶水" :
                                               bestWaterIdToUse == 8766 ? "晨露酒" : "魔法苏打水";

                            Logger.Write($"[{CurrentPlayerName}] [智能吃喝] 蓝量偏低 ({mp:F1}%)，智能检索背包，正在饮用：【{waterName}】(ID: {bestWaterIdToUse})...");

                            UseItemByItemId(_hProcess, bestWaterIdToUse);
                            _lastItemUseTime = DateTime.Now;
                        }
                        else
                        {
                            // 背包里一滴水都没了！
                            Logger.Write($"[{CurrentPlayerName}] [智能吃喝] 警告：蓝量不足，但在背包中没有找到任何有效的饮用水！");
                        }
                    }
                }

                // 只要有一项没补满，就让行为树保持 Running 阻塞，角色原地坐地板直至回满
                return NodeState.Running;
            });
        }

        /// <summary>
        /// 创建【全自动兽医】节点 (召唤、复活、喂食、喝水、加血、护法)
        /// </summary>
        public Node CreatePetVetNode()
        {
            // 闭包状态变量
            int petStateStep = 0;
            DateTime petProcessTimer = DateTime.MinValue;
            DateTime lastFeedTime = DateTime.MinValue;
            DateTime petLogTimer = DateTime.MinValue; // 护法日志防刷屏计时器
            int feedRetryCount = 0; // 喂食失败重试计数器

            return new ActionNode(() =>
            {
                int petBase = GetPetBase(_hProcess);
                bool isPetDead = IsPetDead(_hProcess, petBase);

                // ==========================================
                // 【阶段 0：智能路由分配中心】
                // ==========================================
                if (petStateStep == 0)
                {
                    // 1. 如果宠物丢失或死亡，优先级最高，立刻抢救
                    if (petBase == 0 || isPetDead)
                    {
                        StopMovement(_hProcess, _playerBase);
                        if (petBase == 0)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [宠物系统] 内存中无实体，施放【召唤宠物】...");
                            ExecuteDynamicLua("CastSpellByName('召唤宠物')");
                            petProcessTimer = DateTime.Now;
                            petStateStep = 1;
                        }
                        else
                        {
                            Logger.Write($"[{CurrentPlayerName}] [宠物系统] 发现尸体，施放【复活宠物】...");
                            ExecuteDynamicLua("CastSpellByName('复活宠物')");
                            petProcessTimer = DateTime.Now;
                            petStateStep = 2;
                        }
                        return NodeState.Running;
                    }

                    // 2. 宠物存活着，进行全方位体检
                    int happiness = GetPetHappiness(_hProcess, petBase);
                    int petDesc = MemoryAPI.ReadInteger(_hProcess, petBase + 0x08);
                    int curHp = MemoryAPI.ReadInteger(_hProcess, petDesc + 0x58);
                    int maxHp = MemoryAPI.ReadInteger(_hProcess, petDesc + 0x70);
                    float hpPercent = maxHp > 0 ? ((float)curHp / maxHp * 100f) : 100f;

                    bool hasFeedBuff = HasPetAura(_hProcess, petBase, 1539);  // 喂食Buff
                    bool hasHealBuff = HasPetAura(_hProcess, petBase, 13544); // 治疗Buff

                    // 路由 A：需要喂食 (未达绿脸，且没在吃东西，且脱离上次喂食10秒)
                    if (happiness <= 666666 && !hasFeedBuff && (DateTime.Now - lastFeedTime).TotalSeconds > 10)
                    {
                        petStateStep = 4;
                        return NodeState.Running;
                    }

                    // 路由 B：需要加血 (血量低于 85%，且身上没有治疗Buff)
                    if (hpPercent <= 85f && !hasHealBuff)
                    {
                        petStateStep = 7; // 切入加血/喝水模块
                        return NodeState.Running;
                    }

                    // 路由 C：正在加血中 (原地护法，防止移动打断通道法术)
                    if (hasHealBuff)
                    {
                        StopMovement(_hProcess, _playerBase);
                        if ((DateTime.Now - petLogTimer).TotalSeconds > 3)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [宠物系统] 正在【治疗宠物】(血量:{hpPercent:F1}%)...");
                            petLogTimer = DateTime.Now;
                        }
                        return NodeState.Running;
                    }

                    // 路由 D：正在吃东西，但还没到绿脸 (原地护法，等快乐值涨上去)
                    if (happiness <= 666666 && hasFeedBuff)
                    {
                        StopMovement(_hProcess, _playerBase);
                        if ((DateTime.Now - petLogTimer).TotalSeconds > 3)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [宠物系统] 宝宝正在进食(快乐值:{happiness})，等涨到绿脸再走...");
                            petLogTimer = DateTime.Now;
                        }
                        return NodeState.Running;
                    }

                    // 3. 所有体检通过 (满血、绿脸、没死)！放行去打工！
                    return NodeState.Success;
                }

                // ==========================================
                // 【阶段 1 ~ 3：复活相关逻辑】(保留原样)
                // ==========================================
                if (petStateStep == 1)
                {
                    if ((DateTime.Now - petProcessTimer).TotalSeconds > 1.5)
                    {
                        petBase = GetPetBase(_hProcess);
                        isPetDead = IsPetDead(_hProcess, petBase);
                        if (petBase != 0 && isPetDead)
                        {
                            ExecuteDynamicLua("CastSpellByName('复活宠物')");
                            petProcessTimer = DateTime.Now;
                            petStateStep = 2;
                        }
                        else petStateStep = 0;
                    }
                    return NodeState.Running;
                }
                if (petStateStep == 2)
                {
                    int currentSpell = MemoryAPI.ReadInteger(_hProcess, _playerBase + 0xC8C);
                    if (currentSpell == 982 || currentSpell == 715) return NodeState.Running;
                    // ====================================================
                    // 🚨 第一道保险：读条刚结束，立刻大喊一声“跟随”！
                    // ====================================================
                    Logger.Write($"[{CurrentPlayerName}] [宠物系统] 召唤/复活施法结束，下达【跟随】指令打断停留AI！");
                    ExecuteDynamicLua("PetFollow()");
                    petProcessTimer = DateTime.Now;
                    petStateStep = 3;
                    return NodeState.Running;
                }
                if (petStateStep == 3)
                {
                    if ((DateTime.Now - petProcessTimer).TotalSeconds > 1.5)
                    {
                        // ====================================================
                        // 🚨 第二道保险：1.5秒缓冲结束，宠物彻底站立，再补发一次“跟随”彻底洗掉残留状态！
                        // ====================================================
                        ExecuteDynamicLua("PetFollow()");
                        petStateStep = 0; // 重置状态机，退回体检路由
                    }
                    return NodeState.Running;
                }

                // ==========================================
                // 【阶段 4 ~ 5：喂食与Buff验证】
                // ==========================================
                if (petStateStep == 4)
                {
                    Logger.Write($"[{CurrentPlayerName}] [宠物系统] 正在投喂【烤鹌鹑】... (第 {feedRetryCount + 1} 次尝试)");
                    StopMovement(_hProcess, _playerBase);
                    string feedLua = @"
                CastSpellByName('喂养宠物');
                for b=0,4 do for s=1,18 do 
                    local l=GetContainerItemLink(b,s); 
                    if l and string.find(l,'烤鹌鹑') then PickupContainerItem(b,s); return; end 
                end end";
                    ExecuteDynamicLua(feedLua.Replace("\r", "").Replace("\n", " "));
                    lastFeedTime = DateTime.Now;
                    petProcessTimer = DateTime.Now;
                    petStateStep = 5;
                    return NodeState.Running;
                }
                if (petStateStep == 5)
                {
                    if ((DateTime.Now - petProcessTimer).TotalSeconds > 1.5)
                    {
                        if (HasPetAura(_hProcess, petBase, 1539))
                        {
                            Logger.Write($"[{CurrentPlayerName}] [宠物系统] 喂食成功！");
                            feedRetryCount = 0;
                            petStateStep = 0; // 重回路由，如果还需要加血，它会自动分配！
                        }
                        else
                        {
                            feedRetryCount++;
                            if (feedRetryCount >= 3)
                            {
                                Logger.Write($"[{CurrentPlayerName}] [致命错误] 连续 3 次喂食失败(可能是无食物)！强制终止脚本！");
                                return NodeState.Failure;
                            }
                            petStateStep = 4; // 重新喂
                        }
                    }
                    return NodeState.Running;
                }

                // ==========================================
                // 【阶段 7：加血与自动喝水判定】
                // ==========================================
                if (petStateStep == 7)
                {
                    // 猎人自身蓝量判定 (PowerType 0 的偏移为 0x5C)
                    int pDesc = MemoryAPI.ReadInteger(_hProcess, _playerBase + 0x08);
                    int myMana = MemoryAPI.ReadInteger(_hProcess, pDesc + 0x5C);

                    if (myMana < 1200)
                    {
                        // 如果在战斗中没蓝，绝不能坐地等死！必须跳过加血，直接切出去战斗！
                        if (Unit_Behavioral_State(_hProcess, _playerBase, 19))
                        {
                            Logger.Write($"[{CurrentPlayerName}] [宠物系统] OOM 没蓝加血但正在挨打！跳过急救，拔刀反击！");
                            petStateStep = 0;
                            return NodeState.Success;
                        }

                        // 没在战斗中，老老实实坐下喝水
                        GetEatDrinkStatus(_hProcess, _playerBase, out bool isEating, out bool isDrinking);
                        if (!isDrinking)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [宠物系统] 蓝量极低({myMana}/1200)，正在饮用魔法水...");
                            StopMovement(_hProcess, _playerBase);
                            //UseItemByItemId(_hProcess, 8079); // 调用你类里的最好水 ID
                            //使用智能吃喝
                            autoDrinkEatNode();
                        }
                        return NodeState.Running; // 只要蓝不够 1200 就一直阻塞，直到喝够
                    }

                    // 蓝量充足，直接搓治疗宠物！
                    Logger.Write($"[{CurrentPlayerName}] [宠物系统] 蓝量充足，正在施放【治疗宠物】...");
                    StopMovement(_hProcess, _playerBase);
                    ExecuteDynamicLua("CastSpellByName('治疗宠物')");
                    petProcessTimer = DateTime.Now;
                    petStateStep = 8;
                    return NodeState.Running;
                }

                // ==========================================
                // 【阶段 8：加血通道验证】
                // ==========================================
                if (petStateStep == 8)
                {
                    // 给网络 1.5 秒确认通道法术建立
                    if ((DateTime.Now - petProcessTimer).TotalSeconds > 1.5)
                    {
                        // 无论是否成功挂上 Buff，都退回阶段 0
                        // 因为如果是假失败（比如被打断），阶段 0 会再次发现血量不满，重新分配回来！
                        petStateStep = 0;
                    }
                    return NodeState.Running;
                }

                return NodeState.Running;
            });
        }

        private void BuildTree()
        {
            Node _smartPrepTree = new ActionNode(() =>
            {
                // 1. 获取最新物品 ID 和具体数量
                _bestWaterItemId = DetectBestWater(_hProcess);
                _bestFoodItemId = DetectBestFood(_hProcess);

                int waterCount = _bestWaterItemId != 0 ? GetItemCount(_hProcess, _bestWaterItemId) : 0;
                int foodCount = _bestFoodItemId != 0 ? GetItemCount(_hProcess, _bestFoodItemId) : 0;
                int rubyCount = GetItemCount(_hProcess, 8008);    // 魔法红宝石
                int citrineCount = GetItemCount(_hProcess, 8007); // 魔法黄水晶

                bool needsWater = waterCount < 20;
                bool needsFood = foodCount < 20;
                bool needsRuby = rubyCount == 0;
                bool needsCitrine = citrineCount == 0;

                // 2. 获取精准的生命和法力状态 (包含具体数值)
                float hp = GetHealthPercent(_hProcess, _playerBase);
                float mp = GetManaPercent(_hProcess, _playerBase);

                // 获取具体的法力值
                int descriptors = MemoryAPI.ReadInteger(_hProcess, _playerBase + 0x08);
                int currentMana = MemoryAPI.ReadInteger(_hProcess, descriptors + 0x5C);

                // 全部制造完毕且满状态，发车打怪！
                if (!needsWater && !needsFood && !needsRuby && !needsCitrine && hp > 99f && mp > 99f)
                {
                    Logger.Write($"[{CurrentPlayerName}] [备战系统] 补给与宝石全部就绪，满血满蓝，放行！");
                    return NodeState.Success;
                }

                GetEatDrinkStatus(_hProcess, _playerBase, out bool isEating, out bool isDrinking);
                bool isAnyBuffActive = isEating || isDrinking;
                bool isFull = hp > 99f && mp > 99f; // 是否血蓝双满

                // ==============================================================
                // 💡 3. 黄金三法则：吃喝控制与起立机制 (完美状态机)
                // ==============================================================

                // 【法则 0】：全局硬直拦截 (必须放在最前面，防止刚跳起来又去施法报错)
                if ((DateTime.Now - _lastStandUpTime).TotalMilliseconds < 2000)
                {
                    _wasEatingOrDrinking = isAnyBuffActive; // 保持历史状态同步
                    return NodeState.Running;
                }

                // 【法则 2 & 3】：判断是否需要强制跳跃起立
                bool needToInterrupt = isAnyBuffActive && isFull;             // 法则2：虽然还在吃喝，但已经全满了，主动打断！
                bool buffNaturallyExpired = _wasEatingOrDrinking && !isAnyBuffActive; // 法则3：上一帧有Buff，这帧没了，自然结束，站起来！

                _wasEatingOrDrinking = isAnyBuffActive; // 记录当前状态，留给下一帧用

                if (needToInterrupt)
                {
                    Logger.Write($"[{CurrentPlayerName}] [备战系统] 血蓝已 100% 满，主动打断吃喝，跳跃起立...");
                    _ = Jump(50);
                    _lastStandUpTime = DateTime.Now;
                    return NodeState.Running; // 返回等待，让法则0去处理硬直
                }
                else if (buffNaturallyExpired)
                {
                    Logger.Write($"[{CurrentPlayerName}] [备战系统] 补给Buff已结束，跳跃起立准备下一步...");
                    _ = Jump(50);
                    _lastStandUpTime = DateTime.Now;
                    return NodeState.Running; // 返回等待，让法则0去处理硬直
                }

                // 【法则 1】：死等法则 (只要还在吃喝，就卡住进度，一口气喝到底)
                if (isAnyBuffActive)
                {
                    return NodeState.Running;
                }
                // ==============================================================

                // 防施法动作僵直 (刚按完技能，等 3.5 秒)
                if ((DateTime.Now - _lastCastTime).TotalSeconds < 3.5) return NodeState.Running;

                // 基于具体技能消耗的动态目标蓝量
                int targetMana = 0;
                if (needsWater) targetMana = 820;           // 55级水 780 + 放宽40
                else if (needsFood) targetMana = 750;       // 55级面包 705 + 放宽45
                else if (needsRuby) targetMana = 1520;      // 魔法红宝石 1470 + 放宽50
                else if (needsCitrine) targetMana = 1180;   // 魔法黄水晶 1130 + 放宽50

                // 决定是否需要【开始】吃喝
                bool needToStartDrinking = (targetMana > 0) ? (currentMana <= targetMana) : (mp <= 99f);
                bool needToStartEating = hp <= 99f;

                // 💡【核心新增】：精确计算当前是否还缺少必要的吃喝 Buff？
                // 用于豁免“死等法则”，防止吃了一个被卡住吃不了另一个
                bool missingFoodBuff = needToStartEating && !isEating && (foodCount > 0);
                bool missingWaterBuff = needToStartDrinking && !isDrinking && (waterCount > 0);
                bool missingAnyRequiredBuff = missingFoodBuff || missingWaterBuff;

                // ==============================================================
                // 💡 3. 黄金三法则：吃喝控制与起立机制 (完美状态机)
                // ==============================================================

                // 【法则 0】：全局硬直拦截 (必须放在最前面，防止刚跳起来又去施法报错)
                if ((DateTime.Now - _lastStandUpTime).TotalMilliseconds < 2000)
                {
                    _wasEatingOrDrinking = isAnyBuffActive; // 保持历史状态同步
                    return NodeState.Running;
                }

                _wasEatingOrDrinking = isAnyBuffActive; // 记录当前状态，留给下一帧用

                if (needToInterrupt)
                {
                    Logger.Write($"[{CurrentPlayerName}] [备战系统] 血蓝已 100% 满，主动打断吃喝，跳跃起立...");
                    _ = Jump(50);
                    _lastStandUpTime = DateTime.Now;
                    return NodeState.Running; // 返回等待，让法则0去处理硬直
                }
                else if (buffNaturallyExpired)
                {
                    Logger.Write($"[{CurrentPlayerName}] [备战系统] 补给Buff已结束，跳跃起立准备下一步...");
                    _ = Jump(50);
                    _lastStandUpTime = DateTime.Now;
                    return NodeState.Running; // 返回等待，让法则0去处理硬直
                }

                // 💡【法则 1 完美升级】：死等法则
                // (只要还在吃喝，【并且不需要去补另外一个 Buff 了】，才卡住进度死等)
                if (isAnyBuffActive && !missingAnyRequiredBuff)
                {
                    return NodeState.Running;
                }
                // ==============================================================

                // 防施法动作僵直 (刚按完技能，等 3.5 秒)
                if ((DateTime.Now - _lastCastTime).TotalSeconds < 3.5) return NodeState.Running;

                // 4. 极限压榨吃喝逻辑 
                // 【修改点 1】：将原本的 1.5 秒放宽到 800 毫秒，刚好够服务器处理完上一个物品动作
                bool isItemCooldown = (DateTime.Now - _lastItemUseTime).TotalMilliseconds < 800;

                if (!isItemCooldown)
                {
                    // 💡 【修改点 2】：用互斥拦截。这一帧如果吃了面包，立刻 Return，绝不往下走！
                    if (missingFoodBuff)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [备战系统] 血量未达标，吃面包...");
                        UseItemByItemId(_hProcess, _bestFoodItemId);
                        _lastItemUseTime = DateTime.Now;
                        return NodeState.Running; // 强制截断，交给下一帧
                    }

                    // 💡 如果没吃面包（或者面包的 800ms 硬直已经过了），才会执行喝水
                    if (missingWaterBuff)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [备战系统] 蓝量不足放技能或未满，喝魔法水...");
                        UseItemByItemId(_hProcess, _bestWaterItemId);
                        _lastItemUseTime = DateTime.Now;
                        return NodeState.Running; // 强制截断，交给下一帧
                    }
                }
                else
                {
                    // 物品还在 800 毫秒的硬直中，且我们还需要吃/喝，卡住死等
                    if (missingAnyRequiredBuff)
                    {
                        return NodeState.Running;
                    }
                }

                // 5. 制造业火力全开
                if (needsWater)
                {
                    if (currentMana >= 820)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [备战系统] 缺水({waterCount}/20)，施放造水术...");
                        ExecuteDynamicLua("CastSpellByName('造水术');");
                        _lastCastTime = DateTime.Now;
                    }
                    return NodeState.Running;
                }

                if (needsFood)
                {
                    if (currentMana >= 750)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [备战系统] 缺面包({foodCount}/20)，施放造食术...");
                        ExecuteDynamicLua("CastSpellByName('造食术');");
                        _lastCastTime = DateTime.Now;
                    }
                    return NodeState.Running;
                }

                if (needsRuby)
                {
                    if (currentMana >= 1520)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [备战系统] 缺魔法红宝石(0/1)，施放制造魔法红宝石...");
                        ExecuteDynamicLua("CastSpellByName('制造魔法红宝石');");
                        _lastCastTime = DateTime.Now;
                    }
                    return NodeState.Running;
                }

                if (needsCitrine)
                {
                    if (currentMana >= 1180)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [备战系统] 缺魔法黄水晶(0/1)，施放制造魔法黄水晶...");
                        ExecuteDynamicLua("CastSpellByName('制造魔法黄水晶');");
                        _lastCastTime = DateTime.Now;
                    }
                    return NodeState.Running;
                }

                return NodeState.Running;
            });
            Node _autoDrinkEatNode = new ActionNode(() => {
                float hp = GetHealthPercent(_hProcess, _playerBase);
                float mp = GetManaPercent(_hProcess, _playerBase);

                // 1. 满状态瞬间放行！
                if (hp > 99f && mp > 99f)
                {
                    return NodeState.Success;
                }

                // 2. 获取实时吃喝 Buff 状态
                GetEatDrinkStatus(_hProcess, _playerBase, out bool isEating, out bool isDrinking);

                // 3. 防止一帧内狂点物品导致断线或卡GCD，留 1.6 秒给服务器反应
                bool isItemCooldown = (DateTime.Now - _lastItemUseTime).TotalSeconds < 1.6;

                if (!isItemCooldown)
                {
                    bool usedItem = false; // 同一帧防冲突锁

                    // 4. 需要回血 且 身上没有吃面包 Buff -> 果断吃！
                    if (hp <= 99f && !isEating)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [智能吃喝] 血量偏低 ({hp}%) 且未在进食，正在食用面包 (ID: {_bestFoodItemId})...");
                        UseItemByItemId(_hProcess, _bestFoodItemId);
                        _lastItemUseTime = DateTime.Now;
                        usedItem = true;
                    }

                    // 5. 需要回蓝 且 身上没有喝水 Buff -> 果断喝！
                    // (用 !usedItem 防止在同一帧内把吃面包和喝水一起按了导致宏卡壳)
                    if (mp <= 99f && !isDrinking && !usedItem)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [智能吃喝] 蓝量偏低 ({mp}%) 且未在喝水，正在饮用魔法水 (ID: {_bestWaterItemId})...");
                        UseItemByItemId(_hProcess, _bestWaterItemId);
                        _lastItemUseTime = DateTime.Now;
                    }
                }

                // 6. 只要没满血满蓝，不管是在吃喝途中，还是刚刚按下了物品，全部挂起等待！
                return NodeState.Running;
            });

            //调试代码
            //true = 开启调试
            //false = 关闭调试
            bool isDebug = true;
            if (isDebug)
            {
                _rootTree = new Sequence(
                    new ActionNode(() => {
                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 副本逻辑接管，准备开始刷本！");
                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 正在执行刷本前的准备");
                        return NodeState.Success;
                    }),
                    new WaitNode(100),
                    //跳跃
                    new ActionNode(() =>
                    {
                        _ = Jump(50);
                        return NodeState.Success;
                    }),
                    //等待一秒，等待角色跳跃稳定
                    new WaitNode(2000),

                    //自动吃喝
                    autoDrinkEatNode(),

                    new WaitNode(100),

                    CreatePetVetNode(),

                    new WaitNode(100),
                    //自动吃喝
                    autoDrinkEatNode(),

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 47.45f, -153.66f, -2.71f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 53.27f, -164.73f, -2.71f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 64.29f, -180.82f, -2.71f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 79.58f, -201.52f, -4.11f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 98.32f, -200.08f, -4.12f, 1.0f))
                        {
                            StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new WaitNode(500),
                    //已到位，准备开怪
                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("CastSpellByName('野兽之眼');");
                        return NodeState.Success;
                    }),

                    new WaitNode(100),

                    //野兽之眼两秒读条，这里直接读取猎人自身buff光环来检测是否正确进入到 野兽之眼！  id 1002
                    new ActionNode(() =>
                    {
                        if (HasUnitAura(_hProcess, _playerBase, 1002))
                        {
                            //已进入野兽之眼状态
                            return NodeState.Success;
                        }
                        else
                        {
                            //未能进入野兽之眼状态 继续等待
                            return NodeState.Running;
                        }
                    }),

                    new WaitNode(1000),

                    //开始拉怪
                    //控制宠物移动需要宠物的基址，不能使用玩家的基址
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, GetPetBase(_hProcess), 89.98f, -207.80f, -4.07f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    //跳下悬崖  宠物免疫落地伤害
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, GetPetBase(_hProcess), 96.02f, -236.75f, -56.41f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, GetPetBase(_hProcess), 78.31f, -247.52f, -55.68f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    //打开加速  突进
                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("CastSpellByName('突进');");
                        return NodeState.Success;
                    }),

                    new WaitNode(100),

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, GetPetBase(_hProcess), 63.37f, -263.28f, -53.76f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, GetPetBase(_hProcess), 39.92f, -287.76f, -52.88f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, GetPetBase(_hProcess), 22.90f, -316.25f, -51.46f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, GetPetBase(_hProcess), 19.92f, -337.16f, -52.44f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, GetPetBase(_hProcess), 13.31f, -361.07f, -55.03f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, GetPetBase(_hProcess), -28.09f, -359.00f, -54.35f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, GetPetBase(_hProcess), -51.87f, -351.77f, -54.21f, 1.0f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    //在此处停留300ms,确保引到怪了
                    new WaitNode(300),

                    new ActionNode(() =>
                    {
                        // 1. 去内存里找 1002 所在的槽位
                        int slotIndex = GetBuffSlotIndex(_hProcess, _playerBase, 1002);

                        // 2. 如果找到了 (不等于 -1)
                        if (slotIndex != -1)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 发现野兽之眼在槽位 {slotIndex}，正在安全取消...");

                            // 3. 将找出的槽位索引动态拼接给 Lua 即可！
                            ExecuteDynamicLua($"CancelPlayerBuff({slotIndex});");

                            return NodeState.Running; // 返回 Running，让下一帧再确认是否取消成功
                        }

                        // 没找到（或者已经取消成功了），直接放行
                        return NodeState.Success;
                    }),


                    new WaitNode(100),
                    //成功取消野兽之眼，准备第二波


                    new ActionNode(() =>
                    {
                        // 调用现有的方法获取宠物基址
                        int petBase = GetPetBase(_hProcess);

                        if (petBase == 0 )
                        {
                            Logger.Write($"[{CurrentPlayerName}] [宠物系统] 内存中无实体，施放【召唤宠物】...");
                            ExecuteDynamicLua("CastSpellByName('召唤宠物')");
                            return NodeState.Success;
                        }
                        return NodeState.Success;
                    }),

                    new WaitNode(500),

                    //让宝宝给我们上个buff，进一下仇恨
                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("CastSpellByName('狂怒之嚎');");
                        return NodeState.Success;
                    }),

                    new WaitNode(500),
                    //立即假死
                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("CastSpellByName('假死');");
                        return NodeState.Success;
                    }),
                    new WaitNode(500),

                    new ActionNode(() =>
                    {
                        // 1. 去内存里找 5384 所在的槽位
                        int slotIndex = GetBuffSlotIndex(_hProcess, _playerBase, 5384);

                        // 2. 如果找到了 (不等于 -1)
                        if (slotIndex != -1)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 发现假死在槽位 {slotIndex}，正在安全取消...");

                            // 3. 将找出的槽位索引动态拼接给 Lua 即可！
                            ExecuteDynamicLua($"CancelPlayerBuff({slotIndex});");

                            return NodeState.Running; // 返回 Running，让下一帧再确认是否取消成功
                        }

                        // 没找到（或者已经取消成功了），直接放行
                        return NodeState.Success;
                    }),

                    new WaitNode(1600),

                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("CastSpellByName('野兽之眼');");
                        return NodeState.Success;
                    }),
                    new WaitNode(100),
                    //野兽之眼两秒读条，这里直接读取猎人自身buff光环来检测是否正确进入到 野兽之眼！  id 1002
                    new ActionNode(() =>
                    {
                        if (HasUnitAura(_hProcess, _playerBase, 1002))
                        {
                            //已进入野兽之眼状态
                            return NodeState.Success;
                        }
                        else
                        {
                            //未能进入野兽之眼状态 继续等待
                            return NodeState.Running;
                        }
                    }),

                    new WaitNode(1000),

                    //开始拉怪
                    //控制宠物移动需要宠物的基址，不能使用玩家的基址
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, GetPetBase(_hProcess), 89.98f, -207.80f, -4.07f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, GetPetBase(_hProcess), 85.95f, -245.67f, -56.08f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, GetPetBase(_hProcess), 41.13f, -247.37f, -52.94f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, GetPetBase(_hProcess), 24.31f, -235.21f, -52.42f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    //打开加速  突进
                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("CastSpellByName('突进');");
                        return NodeState.Success;
                    }),

                    new WaitNode(100),


                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, GetPetBase(_hProcess), -15.73f, -242.40f, -56.75f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, GetPetBase(_hProcess), -49.70f, -248.72f, -58.37f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, GetPetBase(_hProcess), -54.64f, -235.12f, -57.57f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),


                    //在此处停留300ms,确保引到怪了
                    new WaitNode(300),

                    new ActionNode(() =>
                    {
                        // 1. 去内存里找 1002 所在的槽位
                        int slotIndex = GetBuffSlotIndex(_hProcess, _playerBase, 1002);

                        // 2. 如果找到了 (不等于 -1)
                        if (slotIndex != -1)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 发现野兽之眼在槽位 {slotIndex}，正在安全取消...");

                            // 3. 将找出的槽位索引动态拼接给 Lua 即可！
                            ExecuteDynamicLua($"CancelPlayerBuff({slotIndex});");

                            return NodeState.Running; // 返回 Running，让下一帧再确认是否取消成功
                        }

                        // 没找到（或者已经取消成功了），直接放行
                        return NodeState.Success;
                    }),


                    new WaitNode(100),
                    //成功取消野兽之眼，准备第三波

                    new ActionNode(() =>
                    {
                        // 调用现有的方法获取宠物基址
                        int petBase = GetPetBase(_hProcess);

                        if (petBase == 0 )
                        {
                            Logger.Write($"[{CurrentPlayerName}] [宠物系统] 内存中无实体，施放【召唤宠物】...");
                            ExecuteDynamicLua("CastSpellByName('召唤宠物')");
                            return NodeState.Success;
                        }
                        return NodeState.Success;
                    }),

                    new WaitNode(500),
                    //让宝宝给我们上个buff，进一下仇恨
                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("CastSpellByName('狂怒之嚎');");
                        return NodeState.Success;
                    }),

                    new WaitNode(500),
                    //立即假死
                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("CastSpellByName('假死');");
                        return NodeState.Success;
                    }),
                    new WaitNode(500),

                    new ActionNode(() =>
                    {
                        // 1. 去内存里找 5384 所在的槽位
                        int slotIndex = GetBuffSlotIndex(_hProcess, _playerBase, 5384);

                        // 2. 如果找到了 (不等于 -1)
                        if (slotIndex != -1)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 发现假死在槽位 {slotIndex}，正在安全取消...");

                            // 3. 将找出的槽位索引动态拼接给 Lua 即可！
                            ExecuteDynamicLua($"CancelPlayerBuff({slotIndex});");

                            return NodeState.Running; // 返回 Running，让下一帧再确认是否取消成功
                        }

                        // 没找到（或者已经取消成功了），直接放行
                        return NodeState.Success;
                    }),

                    new WaitNode(1600),

                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("CastSpellByName('野兽之眼');");
                        return NodeState.Success;
                    }),
                    new WaitNode(100),
                    //野兽之眼两秒读条，这里直接读取猎人自身buff光环来检测是否正确进入到 野兽之眼！  id 1002
                    new ActionNode(() =>
                    {
                        if (HasUnitAura(_hProcess, _playerBase, 1002))
                        {
                            //已进入野兽之眼状态
                            return NodeState.Success;
                        }
                        else
                        {
                            //未能进入野兽之眼状态 继续等待
                            return NodeState.Running;
                        }
                    }),

                    new WaitNode(1000),

                    //开始拉怪
                    //控制宠物移动需要宠物的基址，不能使用玩家的基址
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, GetPetBase(_hProcess), 89.98f, -207.80f, -4.07f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, GetPetBase(_hProcess), 91.58f, -248.42f, -56.07f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, GetPetBase(_hProcess), 55.65f, -270.31f, -53.38f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, GetPetBase(_hProcess), 22.14f, -281.74f, -52.53f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, GetPetBase(_hProcess), -3.84f, -280.72f, -53.10f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, GetPetBase(_hProcess), -38.46f, -272.66f, -56.74f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, GetPetBase(_hProcess), -69.73f, -277.58f, -57.89f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    //打开加速  突进
                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("CastSpellByName('突进');");
                        return NodeState.Success;
                    }),

                    new WaitNode(100),

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, GetPetBase(_hProcess), -69.73f, -277.58f, -57.89f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, GetPetBase(_hProcess), -106.28f, -285.93f, -57.86f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, GetPetBase(_hProcess), -129.48f, -267.85f, -54.01f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, GetPetBase(_hProcess), -154.72f, -246.17f, -52.75f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, GetPetBase(_hProcess), -154.99f, -220.40f, -55.03f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, GetPetBase(_hProcess), -149.15f, -205.87f, -53.09f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    //在此处停留300ms,确保引到怪了
                    new WaitNode(300),

                    new ActionNode(() =>
                    {
                        // 1. 去内存里找 1002 所在的槽位
                        int slotIndex = GetBuffSlotIndex(_hProcess, _playerBase, 1002);

                        // 2. 如果找到了 (不等于 -1)
                        if (slotIndex != -1)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 发现野兽之眼在槽位 {slotIndex}，正在安全取消...");

                            // 3. 将找出的槽位索引动态拼接给 Lua 即可！
                            ExecuteDynamicLua($"CancelPlayerBuff({slotIndex});");

                            return NodeState.Running; // 返回 Running，让下一帧再确认是否取消成功
                        }

                        // 没找到（或者已经取消成功了），直接放行
                        return NodeState.Success;
                    }),

                    new WaitNode(1000000) // 给门打开留点时间

                );  //Sequence结尾 不要复制
                return;
            }
            
            //副本内战斗逻辑
            _farmTree = new Sequence(
                new ActionNode(() => {
                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 副本逻辑接管，准备开始刷本！");
                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 正在执行刷本前的准备");
                    return NodeState.Success;
                }),
                new WaitNode(100),
                //跳跃
                new ActionNode(() =>
                {
                    _ = Jump(50);
                    return NodeState.Success;
                }),
                //等待一秒，等待角色跳跃稳定
                new WaitNode(2000),

                _smartPrepTree,


                new WaitNode(100),
                //跳跃
                new ActionNode(() =>
                {
                    _ = Jump(50);
                    return NodeState.Success;
                }),

                new WaitNode(500),

                new ActionNode(() => {
                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 正在上BUFF");
                    return NodeState.Success;
                }),

                //加BUFF
                new ActionNode(() =>
                {
                    ExecuteDynamicLua("CastSpellByName('冰甲术');");
                    return NodeState.Success;
                }),

                new WaitNode(1600),

                new ActionNode(() =>
                {
                    ExecuteDynamicLua("CastSpellByName('奥术智慧');");
                    return NodeState.Success;
                }),

                new WaitNode(500),

                // 清空吃喝闹钟
                new ActionNode(() =>
                {
                    _lastFoodTime = DateTime.MinValue;
                    _lastDrinkTime = DateTime.MinValue;
                    return NodeState.Success;
                }),

                new WaitNode(100),

                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, 48.42f, -160.41f, -2.71f))
                    {
                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 开始刷本");
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),
                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, 51.20f, -167.69f, -2.71f))
                    {
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, 55.68f, -180.13f, -2.71f))
                    {
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, 59.61f, -190.23f, -4.12f))
                    {
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, 63.41f, -202.15f, -4.10f))
                    {
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, 66.14f, -210.58f, -4.03f))
                    {
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, 67.89f, -215.97f, -2.80f))
                    {
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                // 1. 移动到起跑点
                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, 83.05f, -218.00f, -2.73f))
                    {
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                // 2 & 3 合并：发起冲锋并监控起跳点 (带 CTM 断点续传机制)
                new ActionNode(() =>
                {
                    float pX = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                    float pY = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                    float pZ = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9C0);

                    // [防卡死保险]：如果 Z 轴已经掉下去了，说明已经在空中，立刻放行去准备闪现
                    if (pZ < -4.0f)
                    {
                        return NodeState.Success;
                    }

                    // 计算当前位置与起跳点的 2D 距离平方
                    float dx = pX - 83.60f;
                    float dy = pY - -219.21f;
                    float distSq = dx * dx + dy * dy;

                    // 半径平方设置为 1.0f 左右比较安全
                    if (distSq <= 1.0f)
                    {
                        _ = Jump(50);
                        return NodeState.Success;
                    }

                    // 如果还没跑到起跳点，且距离上次发 CTM 已经过了 500 毫秒
                    // 我们就再强制发一次，专治各种寻路中断、黄豆消失、角色发呆！
                    if ((DateTime.Now - _lastCliffCtmTime).TotalMilliseconds > 500)
                    {
                        MoveTo(_hProcess, _playerBase, 97.49f, -246.32f, -56.22f);
                        _lastCliffCtmTime = DateTime.Now;
                    }

                    // 继续循环监控
                    return NodeState.Running;
                }),

                // 4. 空中下落，监控Z轴高度准备闪现
                new ActionNode(() =>
                {
                    float pZ = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9C0);

                    // 自由落体速度极快，必须使用 <= 进行判断
                    if (pZ <= -40.00f)
                    {
                        ExecuteDynamicLua("CastSpellByName('闪现术');");
                        return NodeState.Success;
                    }

                    return NodeState.Running;
                }),

                //给100让DLL缓冲，否则闪现术放不出来
                new WaitNode(100),

                // 5. 闪现后安全落地，微调坐标并停止
                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, 97.49f, -246.32f, -56.22f, 0.5f))
                    {
                        StopMovement(_hProcess, _playerBase);
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                //已经安全落地

                new WaitNode(100),

                //第一波开始

                new ActionNode(() => {
                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 第一波开始");
                    return NodeState.Success;
                }),

                //自动吃喝
                _autoDrinkEatNode,

                new WaitNode(100),

                //监控110
                new ActionNode(() =>
                {
                    // 110起始坐标
                    float pointA_X = 116.95f;
                    float pointA_Y = -256.78f;

                    // 获取当前内存中所有的“扭木践踏者” (Entry ID: 11465)
                    List<Entry_ID_List> stompers = GetEntitiesByEntryId(_hProcess, 11465);

                    if (stompers.Count == 0)
                        return NodeState.Success; // 没刷怪，直接放行

                    // 判断距离上次打印是否超过了 10 秒
                    bool shouldLog = false;
                    if ((DateTime.Now - _debugThrottle_110A).TotalSeconds >= 10)
                    {
                        shouldLog = true;
                        _debugThrottle_110A = DateTime.Now; // 更新时间戳
                    }

                    int validCount = 0;       // 记录Z轴合格的怪物数量
                    float closestDist = 9999f;// 记录最近的距离
                    float closestZ = 0f;      // 记录最近怪物的Z轴

                    foreach (Entry_ID_List stomper in stompers)
                    {
                        // 温室地面的Z轴大约是 -55 左右，而楼上/室外的可能在 -4。
                        // 通过CE扫出来的数据看，真正的目标Z轴都在 -52 到 -57 之间。
                        if (stomper.Z > -20.0f)
                            continue; // 忽略高层或外面的大树

                        validCount++;
                        // 计算这只大树到监控点的距离
                        float distToA = (float)Math.Sqrt(Math.Pow(stomper.X - pointA_X, 2) + Math.Pow(stomper.Y - pointA_Y, 2));

                        // 记录最近的数据用于排错打印
                        if (distToA < closestDist)
                        {
                            closestDist = distToA;
                            closestZ = stomper.Z;
                        }
                        // 只要有【任意一只】真正的大树距离目标点小于 3.0 码
                        if (distToA < 3.0f)
                        {
                            // 你甚至可以在这里把你动态抓到的 GUID 存下来给选目标用
                            // ulong currentStomperGuid = stomper.Guid; 
                            Logger.Write($"[{CurrentPlayerName}] [监控系统] 发现目标已到位！距离: {distToA:F2} 码，放行！");
                            _debugThrottle_110A = DateTime.MinValue; // 放行前重置
                            return NodeState.Success;
                        }
                    }

                    // 排错输出：每 10 秒告诉你一次它在内存里到底看到了什么
                    if (shouldLog)
                    {
                        string distStr = validCount > 0 ? closestDist.ToString("F2") : "无(全被Z轴过滤)";
                        Logger.Write($"[{CurrentPlayerName}] [监控系统] 目标点 ({pointA_X}, {pointA_Y}) 等待中... 当前 扭木践踏者 总数:{stompers.Count}个 | Z轴及格数:{validCount}个 | 当前最近距离:{distStr}码 | 它的Z轴:{closestZ:F2}");
                    }

                    return NodeState.Running; // 没到位，继续监控等待

                }),

                //此处等待3秒让110转头走
                new WaitNode(3000),

                //第一波战斗开始
                FirstWaveNode(),

                //第二波开始
                //移动到安全区
                new ActionNode(() => {
                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 第二波开始");
                    return NodeState.Success;
                }),

                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, 109.98f, -233.08f, -56.64f, 0.5f))
                    {
                        StopMovement(_hProcess, _playerBase);
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                new WaitNode(100),

                //自动吃喝
                _autoDrinkEatNode,

                new WaitNode(100),

                new ActionNode(() =>
                {
                    _ = Jump(50);
                    return NodeState.Success;
                }),
                new WaitNode(500),

                new ActionNode(() =>
                {
                    ExecuteDynamicLua("CastSpellByName('寒冰护体');"); // 套盾
                    return NodeState.Success;
                }),

                new WaitNode(1600),

                new ActionNode(() =>
                {
                    CastGroundAOE("暴风雪", 76.57637024f, -243.2717285f, -55.93470764f);  //暴风雪
                    return NodeState.Success;
                }),

                //等待时间分支
                WaitByServer(5000, 8000),

                new ActionNode(() =>
                {
                    CastGroundAOE("暴风雪", 95.91210938f, -237.4105072f, -56.39444733f);  //暴风雪
                    return NodeState.Success;
                }),

                //等待时间分支
                WaitByServer(5500, 8000),

                new ActionNode(() =>
                {
                    MoveTo(_hProcess, _playerBase, 101.59f, -235.28f, -56.54f);
                    return NodeState.Success;
                }),
                new WaitNode(100),

                //执行奥爆+自动吃宝石+自动吃治疗药水
                ArcaneExplosionNode(),

                new AutoLootNode(this, () => _hProcess, () => _playerBase, () => 15.0f),
                new WaitNode(100),

                //自动吃喝
                _autoDrinkEatNode,

                new WaitNode(100),
                //第三波
                new ActionNode(() => {
                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 第三波开始");
                    return NodeState.Success;
                }),
                new ActionNode(() =>
                {
                    _ = Jump(50);
                    return NodeState.Success;
                }),
                new WaitNode(500),

                new ActionNode(() =>
                {
                    ExecuteDynamicLua("CastSpellByName('寒冰护体');"); // 套盾
                    return NodeState.Success;
                }),

                new WaitNode(1600),

                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, 74.84f, -245.08f, -55.73f))
                    {
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, 51.43f, -255.23f, -52.94f))
                    {
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, 49.96f, -253.78f, -52.93f, 0.5f))
                    {
                        StopMovement(_hProcess, _playerBase);
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                new WaitNode(100),

                new ActionNode(() =>
                {
                    CastGroundAOE("暴风雪", 24.13565826f, -230.5329132f, -52.68796539f);  //暴风雪
                    return NodeState.Success;
                }),

                //等待时间分支
                WaitByServer(4000, 6000),

                new ActionNode(() =>
                {
                    CastGroundAOE("暴风雪", 36.77518845f, -240.8996124f, -53.40383148f);  //暴风雪
                    return NodeState.Success;
                }),

                //等待时间分支
                WaitByServer(5000, 8000),

                new ActionNode(() =>
                {
                    MoveTo(_hProcess, _playerBase, 45.34f, -249.40f, -52.94f);
                    return NodeState.Success;
                }),
                new WaitNode(100),

                //执行奥爆+自动吃宝石+自动吃治疗药水
                ArcaneExplosionNode(),

                new AutoLootNode(this, () => _hProcess, () => _playerBase, () => 10.0f),

                new WaitNode(100),

                //第四波开始
                new ActionNode(() => {
                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 第四波开始");
                    return NodeState.Success;
                }),
                //走到安全区
                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, 49.96f, -253.78f, -52.93f, 0.5f))
                    {
                        StopMovement(_hProcess, _playerBase);
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                new WaitNode(100),

                //自动吃喝
                _autoDrinkEatNode,
                new WaitNode(100),

                new ActionNode(() =>
                {
                    _ = Jump(50);
                    return NodeState.Success;
                }),
                new WaitNode(500),

                new ActionNode(() =>
                {
                    ExecuteDynamicLua("CastSpellByName('寒冰护体');"); // 套盾
                    return NodeState.Success;
                }),

                new WaitNode(1600),

                //在此处等待110 走到尽头
                new ActionNode(() =>
                {
                    // 110起始坐标
                    float pointA_X = 37.408f;
                    float pointA_Y = -312.281f;

                    // 获取当前内存中所有的“扭木践踏者” (Entry ID: 11465)
                    List<Entry_ID_List> stompers = GetEntitiesByEntryId(_hProcess, 11465);

                    if (stompers.Count == 0)
                        return NodeState.Success; // 没刷怪，直接放行

                    // 判断距离上次打印是否超过了 10 秒
                    bool shouldLog = false;
                    if ((DateTime.Now - _debugThrottle_110A).TotalSeconds >= 10)
                    {
                        shouldLog = true;
                        _debugThrottle_110A = DateTime.Now; // 更新时间戳
                    }

                    int validCount = 0;       // 记录Z轴合格的怪物数量
                    float closestDist = 9999f;// 记录最近的距离
                    float closestZ = 0f;      // 记录最近怪物的Z轴

                    foreach (Entry_ID_List stomper in stompers)
                    {
                        // 温室地面的Z轴大约是 -55 左右，而楼上/室外的可能在 -4。
                        // 通过CE扫出来的数据看，真正的目标Z轴都在 -52 到 -57 之间。
                        if (stomper.Z > -20.0f)
                            continue; // 忽略高层或外面的大树

                        validCount++;
                        // 计算这只大树到监控点的距离
                        float distToA = (float)Math.Sqrt(Math.Pow(stomper.X - pointA_X, 2) + Math.Pow(stomper.Y - pointA_Y, 2));

                        // 记录最近的数据用于排错打印
                        if (distToA < closestDist)
                        {
                            closestDist = distToA;
                            closestZ = stomper.Z;
                        }
                        // 只要有【任意一只】真正的大树距离目标点小于 3.0 码
                        if (distToA < 3.0f)
                        {
                            // 你甚至可以在这里把你动态抓到的 GUID 存下来给选目标用
                            // ulong currentStomperGuid = stomper.Guid; 
                            Logger.Write($"[{CurrentPlayerName}] [监控系统] 发现目标已到位！距离: {distToA:F2} 码，放行！");
                            _debugThrottle_110A = DateTime.MinValue; // 放行前重置
                            return NodeState.Success;
                        }
                    }

                    // 排错输出：每 10 秒告诉你一次它在内存里到底看到了什么
                    if (shouldLog)
                    {
                        string distStr = validCount > 0 ? closestDist.ToString("F2") : "无(全被Z轴过滤)";
                        Logger.Write($"[{CurrentPlayerName}] [监控系统] 目标点 ({pointA_X}, {pointA_Y}) 等待中... 当前 扭木践踏者 总数:{stompers.Count}个 | Z轴及格数:{validCount}个 | 当前最近距离:{distStr}码 | 它的Z轴:{closestZ:F2}");
                    }

                    return NodeState.Running; // 没到位，继续监控等待
                }),

                new WaitNode(100),

                //在此处等待110 走到目标点
                new ActionNode(() =>
                {
                    // 110起始坐标
                    float pointA_X = 8.448f;
                    float pointA_Y = -260.703f;

                    // 获取当前内存中所有的“扭木践踏者” (Entry ID: 11465)
                    List<Entry_ID_List> stompers = GetEntitiesByEntryId(_hProcess, 11465);

                    if (stompers.Count == 0)
                        return NodeState.Success; // 没刷怪，直接放行

                    // 判断距离上次打印是否超过了 10 秒
                    bool shouldLog = false;
                    if ((DateTime.Now - _debugThrottle_110A).TotalSeconds >= 10)
                    {
                        shouldLog = true;
                        _debugThrottle_110A = DateTime.Now; // 更新时间戳
                    }

                    int validCount = 0;       // 记录Z轴合格的怪物数量
                    float closestDist = 9999f;// 记录最近的距离
                    float closestZ = 0f;      // 记录最近怪物的Z轴

                    foreach (Entry_ID_List stomper in stompers)
                    {
                        // 温室地面的Z轴大约是 -55 左右，而楼上/室外的可能在 -4。
                        // 通过CE扫出来的数据看，真正的目标Z轴都在 -52 到 -57 之间。
                        if (stomper.Z > -20.0f)
                            continue; // 忽略高层或外面的大树

                        validCount++;
                        // 计算这只大树到监控点的距离
                        float distToA = (float)Math.Sqrt(Math.Pow(stomper.X - pointA_X, 2) + Math.Pow(stomper.Y - pointA_Y, 2));

                        // 记录最近的数据用于排错打印
                        if (distToA < closestDist)
                        {
                            closestDist = distToA;
                            closestZ = stomper.Z;
                        }
                        // 只要有【任意一只】真正的大树距离目标点小于 3.0 码
                        if (distToA < 3.0f)
                        {
                            // 你甚至可以在这里把你动态抓到的 GUID 存下来给选目标用
                            // ulong currentStomperGuid = stomper.Guid; 
                            Logger.Write($"[{CurrentPlayerName}] [监控系统] 发现目标已到位！距离: {distToA:F2} 码，放行！");
                            _debugThrottle_110A = DateTime.MinValue; // 放行前重置
                            return NodeState.Success;
                        }
                    }

                    // 排错输出：每 10 秒告诉你一次它在内存里到底看到了什么
                    if (shouldLog)
                    {
                        string distStr = validCount > 0 ? closestDist.ToString("F2") : "无(全被Z轴过滤)";
                        Logger.Write($"[{CurrentPlayerName}] [监控系统] 目标点 ({pointA_X}, {pointA_Y}) 等待中... 当前 扭木践踏者 总数:{stompers.Count}个 | Z轴及格数:{validCount}个 | 当前最近距离:{distStr}码 | 它的Z轴:{closestZ:F2}");
                    }

                    return NodeState.Running; // 没到位，继续监控等待
                }),

                new WaitNode(100),

                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, 38.41f, -275.50f, -52.78f, 0.5f))
                    {
                        StopMovement(_hProcess, _playerBase);
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                new WaitNode(100),

                new ActionNode(() =>
                {
                    CastGroundAOE("暴风雪(等级 1)", 16.37648773f, -302.8551636f, -52.23928452f);  //一级暴风雪
                    return NodeState.Success;
                }),

                new WaitNode(100),

                new ActionNode(() =>
                {
                    if(Unit_Behavioral_State(_hProcess, _playerBase, 19))
                    {
                        //战斗中
                        return NodeState.Success;
                    }
                    else 
                    {
                        //未战斗  卡住行为树
                        return NodeState.Running;
                    }
                }),

                new WaitNode(100),

                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, 42.31f, -271.87f, -52.68f))
                    {
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                new WaitNode(100),

                new ActionNode(() =>
                {
                    ExecuteDynamicLua("CastSpellByName('闪现术');");
                    return NodeState.Success;
                }),

                new WaitNode(100),

                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, 83.27f, -235.74f, -56.58f, 0.5f))
                    {
                        StopMovement(_hProcess, _playerBase);
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                new WaitNode(100),

                new ActionNode(() =>
                {
                    CastGroundAOE("暴风雪", 60.25006485f, -259.4793396f, -53.48717499f);  //暴风雪
                    return NodeState.Success;
                }),

                //等待时间分支
                WaitByServer(3500, 6000),

                new ActionNode(() =>
                {
                    CastGroundAOE("暴风雪", 69.94404602f, -248.7620392f, -55.29639435f);  //暴风雪
                    return NodeState.Success;
                }),

                //等待时间分支
                WaitByServer(4500, 8000),


                new ActionNode(() =>
                {
                    MoveTo(_hProcess, _playerBase, 78.49f, -240.98f, -56.17f);
                    return NodeState.Success;
                }),
                new WaitNode(100),

                //执行奥爆+自动吃宝石+自动吃治疗药水
                ArcaneExplosionNode(),

                new WaitNode(100),

                new AutoLootNode(this, () => _hProcess, () => _playerBase, () => 10.0f),

                new WaitNode(100),
                //第五波
                //走到等待区
                new ActionNode(() => {
                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 第五波开始");
                    return NodeState.Success;
                }),

                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, 49.96f, -253.78f, -52.93f))
                    {
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                new WaitNode(100),


                //在此处等待110 走到尽头
                new ActionNode(() =>
                {
                    // 110起始坐标
                    float pointA_X = 37.408f;
                    float pointA_Y = -312.281f;

                    // 获取当前内存中所有的“扭木践踏者” (Entry ID: 11465)
                    List<Entry_ID_List> stompers = GetEntitiesByEntryId(_hProcess, 11465);

                    if (stompers.Count == 0)
                        return NodeState.Success; // 没刷怪，直接放行

                    // 判断距离上次打印是否超过了 10 秒
                    bool shouldLog = false;
                    if ((DateTime.Now - _debugThrottle_110A).TotalSeconds >= 10)
                    {
                        shouldLog = true;
                        _debugThrottle_110A = DateTime.Now; // 更新时间戳
                    }

                    int validCount = 0;       // 记录Z轴合格的怪物数量
                    float closestDist = 9999f;// 记录最近的距离
                    float closestZ = 0f;      // 记录最近怪物的Z轴

                    foreach (Entry_ID_List stomper in stompers)
                    {
                        // 温室地面的Z轴大约是 -55 左右，而楼上/室外的可能在 -4。
                        // 通过CE扫出来的数据看，真正的目标Z轴都在 -52 到 -57 之间。
                        if (stomper.Z > -20.0f)
                            continue; // 忽略高层或外面的大树

                        validCount++;
                        // 计算这只大树到监控点的距离
                        float distToA = (float)Math.Sqrt(Math.Pow(stomper.X - pointA_X, 2) + Math.Pow(stomper.Y - pointA_Y, 2));

                        // 记录最近的数据用于排错打印
                        if (distToA < closestDist)
                        {
                            closestDist = distToA;
                            closestZ = stomper.Z;
                        }
                        // 只要有【任意一只】真正的大树距离目标点小于 3.0 码
                        if (distToA < 3.0f)
                        {
                            // 你甚至可以在这里把你动态抓到的 GUID 存下来给选目标用
                            // ulong currentStomperGuid = stomper.Guid; 
                            Logger.Write($"[{CurrentPlayerName}] [监控系统] 发现目标已到位！距离: {distToA:F2} 码，放行！");
                            _debugThrottle_110A = DateTime.MinValue; // 放行前重置
                            return NodeState.Success;
                        }
                    }

                    // 排错输出：每 10 秒告诉你一次它在内存里到底看到了什么
                    if (shouldLog)
                    {
                        string distStr = validCount > 0 ? closestDist.ToString("F2") : "无(全被Z轴过滤)";
                        Logger.Write($"[{CurrentPlayerName}] [监控系统] 目标点 ({pointA_X}, {pointA_Y}) 等待中... 当前 扭木践踏者 总数:{stompers.Count}个 | Z轴及格数:{validCount}个 | 当前最近距离:{distStr}码 | 它的Z轴:{closestZ:F2}");
                    }

                    return NodeState.Running; // 没到位，继续监控等待
                }),

                new WaitNode(100),

                //在此处等待110 走到目标点
                new ActionNode(() =>
                {
                    // 110起始坐标
                    float pointA_X = 8.448f;
                    float pointA_Y = -260.703f;

                    // 获取当前内存中所有的“扭木践踏者” (Entry ID: 11465)
                    List<Entry_ID_List> stompers = GetEntitiesByEntryId(_hProcess, 11465);

                    if (stompers.Count == 0)
                        return NodeState.Success; // 没刷怪，直接放行

                    // 判断距离上次打印是否超过了 10 秒
                    bool shouldLog = false;
                    if ((DateTime.Now - _debugThrottle_110A).TotalSeconds >= 10)
                    {
                        shouldLog = true;
                        _debugThrottle_110A = DateTime.Now; // 更新时间戳
                    }

                    int validCount = 0;       // 记录Z轴合格的怪物数量
                    float closestDist = 9999f;// 记录最近的距离
                    float closestZ = 0f;      // 记录最近怪物的Z轴

                    foreach (Entry_ID_List stomper in stompers)
                    {
                        // 温室地面的Z轴大约是 -55 左右，而楼上/室外的可能在 -4。
                        // 通过CE扫出来的数据看，真正的目标Z轴都在 -52 到 -57 之间。
                        if (stomper.Z > -20.0f)
                            continue; // 忽略高层或外面的大树

                        validCount++;
                        // 计算这只大树到监控点的距离
                        float distToA = (float)Math.Sqrt(Math.Pow(stomper.X - pointA_X, 2) + Math.Pow(stomper.Y - pointA_Y, 2));

                        // 记录最近的数据用于排错打印
                        if (distToA < closestDist)
                        {
                            closestDist = distToA;
                            closestZ = stomper.Z;
                        }
                        // 只要有【任意一只】真正的大树距离目标点小于 3.0 码
                        if (distToA < 3.0f)
                        {
                            // 你甚至可以在这里把你动态抓到的 GUID 存下来给选目标用
                            // ulong currentStomperGuid = stomper.Guid; 
                            Logger.Write($"[{CurrentPlayerName}] [监控系统] 发现目标已到位！距离: {distToA:F2} 码，放行！");
                            _debugThrottle_110A = DateTime.MinValue; // 放行前重置
                            return NodeState.Success;
                        }
                    }

                    // 排错输出：每 10 秒告诉你一次它在内存里到底看到了什么
                    if (shouldLog)
                    {
                        string distStr = validCount > 0 ? closestDist.ToString("F2") : "无(全被Z轴过滤)";
                        Logger.Write($"[{CurrentPlayerName}] [监控系统] 目标点 ({pointA_X}, {pointA_Y}) 等待中... 当前 扭木践踏者 总数:{stompers.Count}个 | Z轴及格数:{validCount}个 | 当前最近距离:{distStr}码 | 它的Z轴:{closestZ:F2}");
                    }

                    return NodeState.Running; // 没到位，继续监控等待
                }),


                new WaitNode(100),

                new ActionNode(() =>
                {
                    MoveTo(_hProcess, _playerBase, 26.77f, -325.47f, -51.33f);
                    return NodeState.Success;
                }),

                new WaitNode(500),

                new ActionNode(() =>
                {
                    ExecuteDynamicLua("CastSpellByName('闪现术');");  // 闪现术
                    return NodeState.Success;
                }),

                new WaitNode(100),

                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, 26.77f, -325.47f, -51.33f))
                    {
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),


                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, 10.52f, -369.96f, -54.35f))
                    {
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, -17.50f, -369.12f, -56.55f))
                    {
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                //走到等待区
                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, -27.11f, -366.92f, -56.07f, 0.5f))
                    {
                        StopMovement(_hProcess, _playerBase);
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                new WaitNode(100),

                //自动吃喝
                _autoDrinkEatNode,

                new WaitNode(100),

                new ActionNode(() =>
                {
                    _ = Jump(50);
                    return NodeState.Success;
                }),
                new WaitNode(500),

                new ActionNode(() =>
                {
                    ExecuteDynamicLua("CastSpellByName('寒冰护体');"); // 套盾
                    return NodeState.Success;
                }),

                new WaitNode(1600),

                //在此处等待110 走到尽头
                new ActionNode(() =>
                {
                    // 110起始坐标
                    float pointA_X = -65.031f;
                    float pointA_Y = -356.756f;

                    // 获取当前内存中所有的“扭木践踏者” (Entry ID: 11465)
                    List<Entry_ID_List> stompers = GetEntitiesByEntryId(_hProcess, 11465);

                    if (stompers.Count == 0)
                        return NodeState.Success; // 没刷怪，直接放行

                    // 判断距离上次打印是否超过了 10 秒
                    bool shouldLog = false;
                    if ((DateTime.Now - _debugThrottle_110A).TotalSeconds >= 10)
                    {
                        shouldLog = true;
                        _debugThrottle_110A = DateTime.Now; // 更新时间戳
                    }

                    int validCount = 0;       // 记录Z轴合格的怪物数量
                    float closestDist = 9999f;// 记录最近的距离
                    float closestZ = 0f;      // 记录最近怪物的Z轴

                    foreach (Entry_ID_List stomper in stompers)
                    {
                        // 温室地面的Z轴大约是 -55 左右，而楼上/室外的可能在 -4。
                        // 通过CE扫出来的数据看，真正的目标Z轴都在 -52 到 -57 之间。
                        if (stomper.Z > -20.0f)
                            continue; // 忽略高层或外面的大树

                        validCount++;
                        // 计算这只大树到监控点的距离
                        float distToA = (float)Math.Sqrt(Math.Pow(stomper.X - pointA_X, 2) + Math.Pow(stomper.Y - pointA_Y, 2));

                        // 记录最近的数据用于排错打印
                        if (distToA < closestDist)
                        {
                            closestDist = distToA;
                            closestZ = stomper.Z;
                        }
                        // 只要有【任意一只】真正的大树距离目标点小于 3.0 码
                        if (distToA < 3.0f)
                        {
                            // 你甚至可以在这里把你动态抓到的 GUID 存下来给选目标用
                            // ulong currentStomperGuid = stomper.Guid; 
                            Logger.Write($"[{CurrentPlayerName}] [监控系统] 发现目标已到位！距离: {distToA:F2} 码，放行！");
                            _debugThrottle_110A = DateTime.MinValue; // 放行前重置
                            return NodeState.Success;
                        }
                    }

                    // 排错输出：每 10 秒告诉你一次它在内存里到底看到了什么
                    if (shouldLog)
                    {
                        string distStr = validCount > 0 ? closestDist.ToString("F2") : "无(全被Z轴过滤)";
                        Logger.Write($"[{CurrentPlayerName}] [监控系统] 目标点 ({pointA_X}, {pointA_Y}) 等待中... 当前 扭木践踏者 总数:{stompers.Count}个 | Z轴及格数:{validCount}个 | 当前最近距离:{distStr}码 | 它的Z轴:{closestZ:F2}");
                    }

                    return NodeState.Running; // 没到位，继续监控等待
                }),

                new WaitNode(100),

                //在此处等待110 走到目标点
                new ActionNode(() =>
                {
                    // 110起始坐标
                    float pointA_X = -72.460f;
                    float pointA_Y = -332.710f;

                    // 获取当前内存中所有的“扭木践踏者” (Entry ID: 11465)
                    List<Entry_ID_List> stompers = GetEntitiesByEntryId(_hProcess, 11465);

                    if (stompers.Count == 0)
                        return NodeState.Success; // 没刷怪，直接放行

                    // 判断距离上次打印是否超过了 10 秒
                    bool shouldLog = false;
                    if ((DateTime.Now - _debugThrottle_110A).TotalSeconds >= 10)
                    {
                        shouldLog = true;
                        _debugThrottle_110A = DateTime.Now; // 更新时间戳
                    }

                    int validCount = 0;       // 记录Z轴合格的怪物数量
                    float closestDist = 9999f;// 记录最近的距离
                    float closestZ = 0f;      // 记录最近怪物的Z轴

                    foreach (Entry_ID_List stomper in stompers)
                    {
                        // 温室地面的Z轴大约是 -55 左右，而楼上/室外的可能在 -4。
                        // 通过CE扫出来的数据看，真正的目标Z轴都在 -52 到 -57 之间。
                        if (stomper.Z > -20.0f)
                            continue; // 忽略高层或外面的大树

                        validCount++;
                        // 计算这只大树到监控点的距离
                        float distToA = (float)Math.Sqrt(Math.Pow(stomper.X - pointA_X, 2) + Math.Pow(stomper.Y - pointA_Y, 2));

                        // 记录最近的数据用于排错打印
                        if (distToA < closestDist)
                        {
                            closestDist = distToA;
                            closestZ = stomper.Z;
                        }
                        // 只要有【任意一只】真正的大树距离目标点小于 3.0 码
                        if (distToA < 3.0f)
                        {
                            // 你甚至可以在这里把你动态抓到的 GUID 存下来给选目标用
                            // ulong currentStomperGuid = stomper.Guid; 
                            Logger.Write($"[{CurrentPlayerName}] [监控系统] 发现目标已到位！距离: {distToA:F2} 码，放行！");
                            _debugThrottle_110A = DateTime.MinValue; // 放行前重置
                            return NodeState.Success;
                        }
                    }

                    // 排错输出：每 10 秒告诉你一次它在内存里到底看到了什么
                    if (shouldLog)
                    {
                        string distStr = validCount > 0 ? closestDist.ToString("F2") : "无(全被Z轴过滤)";
                        Logger.Write($"[{CurrentPlayerName}] [监控系统] 目标点 ({pointA_X}, {pointA_Y}) 等待中... 当前 扭木践踏者 总数:{stompers.Count}个 | Z轴及格数:{validCount}个 | 当前最近距离:{distStr}码 | 它的Z轴:{closestZ:F2}");
                    }

                    return NodeState.Running; // 没到位，继续监控等待
                }),


                new WaitNode(100),

                new ActionNode(() =>
                {
                    CastGroundAOE("暴风雪", -49.69861984f, -352.8047485f, -54.10058975f);     //暴风雪
                    return NodeState.Success;
                }),
                //等待时间分支
                WaitByServer(4000, 5000),


                new ActionNode(() =>
                {
                    CastGroundAOE("暴风雪", -36.38663483f, -360.0899963f, -54.67075729f);     //暴风雪
                    return NodeState.Success;
                }),

                //等待时间分支
                WaitByServer(3500, 6000),

                new ActionNode(() =>
                {
                    MoveTo(_hProcess, _playerBase, -31.11f, -365.46f, -55.75f);
                    return NodeState.Success;
                }),
                new WaitNode(100),

                //执行奥爆+自动吃宝石+自动吃治疗药水
                ArcaneExplosionNode(),

                new AutoLootNode(this, () => _hProcess, () => _playerBase, () => 15.0f),

                new WaitNode(100),


                //第六波
                //走到等待区
                new ActionNode(() => {
                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 第六波开始");
                    return NodeState.Success;
                }),
                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, -34.14f, -353.52f, -53.25f, 0.5f))
                    {
                        StopMovement(_hProcess, _playerBase);
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                new WaitNode(100),

                //在此处等待110 走到尽头
                new ActionNode(() =>
                {
                    // 110起始坐标
                    float pointA_X = -65.031f;
                    float pointA_Y = -356.756f;

                    // 获取当前内存中所有的“扭木践踏者” (Entry ID: 11465)
                    List<Entry_ID_List> stompers = GetEntitiesByEntryId(_hProcess, 11465);

                    if (stompers.Count == 0)
                        return NodeState.Success; // 没刷怪，直接放行

                    // 判断距离上次打印是否超过了 10 秒
                    bool shouldLog = false;
                    if ((DateTime.Now - _debugThrottle_110A).TotalSeconds >= 10)
                    {
                        shouldLog = true;
                        _debugThrottle_110A = DateTime.Now; // 更新时间戳
                    }

                    int validCount = 0;       // 记录Z轴合格的怪物数量
                    float closestDist = 9999f;// 记录最近的距离
                    float closestZ = 0f;      // 记录最近怪物的Z轴

                    foreach (Entry_ID_List stomper in stompers)
                    {
                        // 温室地面的Z轴大约是 -55 左右，而楼上/室外的可能在 -4。
                        // 通过CE扫出来的数据看，真正的目标Z轴都在 -52 到 -57 之间。
                        if (stomper.Z > -20.0f)
                            continue; // 忽略高层或外面的大树

                        validCount++;
                        // 计算这只大树到监控点的距离
                        float distToA = (float)Math.Sqrt(Math.Pow(stomper.X - pointA_X, 2) + Math.Pow(stomper.Y - pointA_Y, 2));

                        // 记录最近的数据用于排错打印
                        if (distToA < closestDist)
                        {
                            closestDist = distToA;
                            closestZ = stomper.Z;
                        }
                        // 只要有【任意一只】真正的大树距离目标点小于 3.0 码
                        if (distToA < 3.0f)
                        {
                            // 你甚至可以在这里把你动态抓到的 GUID 存下来给选目标用
                            // ulong currentStomperGuid = stomper.Guid; 
                            Logger.Write($"[{CurrentPlayerName}] [监控系统] 发现目标已到位！距离: {distToA:F2} 码，放行！");
                            _debugThrottle_110A = DateTime.MinValue; // 放行前重置
                            return NodeState.Success;
                        }
                    }

                    // 排错输出：每 10 秒告诉你一次它在内存里到底看到了什么
                    if (shouldLog)
                    {
                        string distStr = validCount > 0 ? closestDist.ToString("F2") : "无(全被Z轴过滤)";
                        Logger.Write($"[{CurrentPlayerName}] [监控系统] 目标点 ({pointA_X}, {pointA_Y}) 等待中... 当前 扭木践踏者 总数:{stompers.Count}个 | Z轴及格数:{validCount}个 | 当前最近距离:{distStr}码 | 它的Z轴:{closestZ:F2}");
                    }

                    return NodeState.Running; // 没到位，继续监控等待
                }),

                new WaitNode(100),

                //在此处等待110 走到目标点
                new ActionNode(() =>
                {
                    // 110起始坐标
                    float pointA_X = -72.460f;
                    float pointA_Y = -332.710f;

                    // 获取当前内存中所有的“扭木践踏者” (Entry ID: 11465)
                    List<Entry_ID_List> stompers = GetEntitiesByEntryId(_hProcess, 11465);

                    if (stompers.Count == 0)
                        return NodeState.Success; // 没刷怪，直接放行

                    // 判断距离上次打印是否超过了 10 秒
                    bool shouldLog = false;
                    if ((DateTime.Now - _debugThrottle_110A).TotalSeconds >= 10)
                    {
                        shouldLog = true;
                        _debugThrottle_110A = DateTime.Now; // 更新时间戳
                    }

                    int validCount = 0;       // 记录Z轴合格的怪物数量
                    float closestDist = 9999f;// 记录最近的距离
                    float closestZ = 0f;      // 记录最近怪物的Z轴

                    foreach (Entry_ID_List stomper in stompers)
                    {
                        // 温室地面的Z轴大约是 -55 左右，而楼上/室外的可能在 -4。
                        // 通过CE扫出来的数据看，真正的目标Z轴都在 -52 到 -57 之间。
                        if (stomper.Z > -20.0f)
                            continue; // 忽略高层或外面的大树

                        validCount++;
                        // 计算这只大树到监控点的距离
                        float distToA = (float)Math.Sqrt(Math.Pow(stomper.X - pointA_X, 2) + Math.Pow(stomper.Y - pointA_Y, 2));

                        // 记录最近的数据用于排错打印
                        if (distToA < closestDist)
                        {
                            closestDist = distToA;
                            closestZ = stomper.Z;
                        }
                        // 只要有【任意一只】真正的大树距离目标点小于 3.0 码
                        if (distToA < 3.0f)
                        {
                            // 你甚至可以在这里把你动态抓到的 GUID 存下来给选目标用
                            // ulong currentStomperGuid = stomper.Guid; 
                            Logger.Write($"[{CurrentPlayerName}] [监控系统] 发现目标已到位！距离: {distToA:F2} 码，放行！");
                            _debugThrottle_110A = DateTime.MinValue; // 放行前重置
                            return NodeState.Success;
                        }
                    }

                    // 排错输出：每 10 秒告诉你一次它在内存里到底看到了什么
                    if (shouldLog)
                    {
                        string distStr = validCount > 0 ? closestDist.ToString("F2") : "无(全被Z轴过滤)";
                        Logger.Write($"[{CurrentPlayerName}] [监控系统] 目标点 ({pointA_X}, {pointA_Y}) 等待中... 当前 扭木践踏者 总数:{stompers.Count}个 | Z轴及格数:{validCount}个 | 当前最近距离:{distStr}码 | 它的Z轴:{closestZ:F2}");
                    }

                    return NodeState.Running; // 没到位，继续监控等待
                }),


                new WaitNode(100),

                //开始移动

                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, -47.47f, -353.41f, -53.97f))
                    {
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),


                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, -50.55f, -339.19f, -53.06f))
                    {
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                new ActionNode(() =>
                {
                    MoveTo(_hProcess, _playerBase, -68.78f, -285.00f, -58.00f);
                    return NodeState.Success;
                }),

                new WaitNode(300),

                new ActionNode(() =>
                {
                    ExecuteDynamicLua("CastSpellByName('闪现术');");  // 闪现术
                    return NodeState.Success;
                }),

                new WaitNode(100),

                //安全区
                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, -68.78f, -285.00f, -58.00f, 0.5f))
                    {
                        StopMovement(_hProcess, _playerBase);
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                new WaitNode(100),

                //自动吃喝
                _autoDrinkEatNode,

                new WaitNode(100),

                new ActionNode(() =>
                {
                    _ = Jump(50);
                    return NodeState.Success;
                }),
                new WaitNode(500),

                new ActionNode(() =>
                {
                    ExecuteDynamicLua("CastSpellByName('寒冰护体');"); // 套盾
                    return NodeState.Success;
                }),

                new WaitNode(1600),


                //在此处等待110 走到尽头
                new ActionNode(() =>
                {
                    // 110起始坐标
                    float pointA_X = -87.907f;
                    float pointA_Y = -233.252f;

                    // 获取当前内存中所有的“扭木践踏者” (Entry ID: 11465)
                    List<Entry_ID_List> stompers = GetEntitiesByEntryId(_hProcess, 11465);

                    if (stompers.Count == 0)
                        return NodeState.Success; // 没刷怪，直接放行

                    // 判断距离上次打印是否超过了 10 秒
                    bool shouldLog = false;
                    if ((DateTime.Now - _debugThrottle_110A).TotalSeconds >= 10)
                    {
                        shouldLog = true;
                        _debugThrottle_110A = DateTime.Now; // 更新时间戳
                    }

                    int validCount = 0;       // 记录Z轴合格的怪物数量
                    float closestDist = 9999f;// 记录最近的距离
                    float closestZ = 0f;      // 记录最近怪物的Z轴

                    foreach (Entry_ID_List stomper in stompers)
                    {
                        // 温室地面的Z轴大约是 -55 左右，而楼上/室外的可能在 -4。
                        // 通过CE扫出来的数据看，真正的目标Z轴都在 -52 到 -57 之间。
                        if (stomper.Z > -20.0f)
                            continue; // 忽略高层或外面的大树

                        validCount++;
                        // 计算这只大树到监控点的距离
                        float distToA = (float)Math.Sqrt(Math.Pow(stomper.X - pointA_X, 2) + Math.Pow(stomper.Y - pointA_Y, 2));

                        // 记录最近的数据用于排错打印
                        if (distToA < closestDist)
                        {
                            closestDist = distToA;
                            closestZ = stomper.Z;
                        }
                        // 只要有【任意一只】真正的大树距离目标点小于 3.0 码
                        if (distToA < 3.0f)
                        {
                            // 你甚至可以在这里把你动态抓到的 GUID 存下来给选目标用
                            // ulong currentStomperGuid = stomper.Guid; 
                            Logger.Write($"[{CurrentPlayerName}] [监控系统] 发现目标已到位！距离: {distToA:F2} 码，放行！");
                            _debugThrottle_110A = DateTime.MinValue; // 放行前重置
                            return NodeState.Success;
                        }
                    }

                    // 排错输出：每 10 秒告诉你一次它在内存里到底看到了什么
                    if (shouldLog)
                    {
                        string distStr = validCount > 0 ? closestDist.ToString("F2") : "无(全被Z轴过滤)";
                        Logger.Write($"[{CurrentPlayerName}] [监控系统] 目标点 ({pointA_X}, {pointA_Y}) 等待中... 当前 扭木践踏者 总数:{stompers.Count}个 | Z轴及格数:{validCount}个 | 当前最近距离:{distStr}码 | 它的Z轴:{closestZ:F2}");
                    }

                    return NodeState.Running; // 没到位，继续监控等待
                }),

                new WaitNode(100),

                //在此处等待110 走到目标点
                new ActionNode(() =>
                {
                    // 110起始坐标
                    float pointA_X = -42.746f;
                    float pointA_Y = -262.275f;

                    // 获取当前内存中所有的“扭木践踏者” (Entry ID: 11465)
                    List<Entry_ID_List> stompers = GetEntitiesByEntryId(_hProcess, 11465);

                    if (stompers.Count == 0)
                        return NodeState.Success; // 没刷怪，直接放行

                    // 判断距离上次打印是否超过了 10 秒
                    bool shouldLog = false;
                    if ((DateTime.Now - _debugThrottle_110A).TotalSeconds >= 10)
                    {
                        shouldLog = true;
                        _debugThrottle_110A = DateTime.Now; // 更新时间戳
                    }

                    int validCount = 0;       // 记录Z轴合格的怪物数量
                    float closestDist = 9999f;// 记录最近的距离
                    float closestZ = 0f;      // 记录最近怪物的Z轴

                    foreach (Entry_ID_List stomper in stompers)
                    {
                        // 温室地面的Z轴大约是 -55 左右，而楼上/室外的可能在 -4。
                        // 通过CE扫出来的数据看，真正的目标Z轴都在 -52 到 -57 之间。
                        if (stomper.Z > -20.0f)
                            continue; // 忽略高层或外面的大树

                        validCount++;
                        // 计算这只大树到监控点的距离
                        float distToA = (float)Math.Sqrt(Math.Pow(stomper.X - pointA_X, 2) + Math.Pow(stomper.Y - pointA_Y, 2));

                        // 记录最近的数据用于排错打印
                        if (distToA < closestDist)
                        {
                            closestDist = distToA;
                            closestZ = stomper.Z;
                        }
                        // 只要有【任意一只】真正的大树距离目标点小于 3.0 码
                        if (distToA < 3.0f)
                        {
                            // 你甚至可以在这里把你动态抓到的 GUID 存下来给选目标用
                            // ulong currentStomperGuid = stomper.Guid; 
                            Logger.Write($"[{CurrentPlayerName}] [监控系统] 发现目标已到位！距离: {distToA:F2} 码，放行！");
                            _debugThrottle_110A = DateTime.MinValue; // 放行前重置
                            return NodeState.Success;
                        }
                    }

                    // 排错输出：每 10 秒告诉你一次它在内存里到底看到了什么
                    if (shouldLog)
                    {
                        string distStr = validCount > 0 ? closestDist.ToString("F2") : "无(全被Z轴过滤)";
                        Logger.Write($"[{CurrentPlayerName}] [监控系统] 目标点 ({pointA_X}, {pointA_Y}) 等待中... 当前 扭木践踏者 总数:{stompers.Count}个 | Z轴及格数:{validCount}个 | 当前最近距离:{distStr}码 | 它的Z轴:{closestZ:F2}");
                    }

                    return NodeState.Running; // 没到位，继续监控等待
                }),


                new WaitNode(100),

                new ActionNode(() =>
                {
                    //if (MoveTo(_hProcess, _playerBase, -68.45f, -269.52f, -57.65f, 0.5f))
                    //修复旧坐标偶尔导致的无法引到怪，更新了新的坐标
                    if (MoveTo(_hProcess, _playerBase, -68.91f, -267.29f, -58.20f, 0.5f))
                    {
                        StopMovement(_hProcess, _playerBase);
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                new WaitNode(1000),

                new ActionNode(() =>
                {
                    //修复旧坐标偶尔导致的无法引到怪，更新了新的坐标
                    //CastGroundAOE("暴风雪(等级 1)", -58.37534332f, -236.5191803f, -57.54912567f);  //一级暴风雪
                    CastGroundAOE("暴风雪(等级 1)", -58.27764893f, -233.2396545f, -57.47127533f);  //一级暴风雪
                    return NodeState.Success;
                }),

                new WaitNode(100),

                new ActionNode(() =>
                {
                    if (Unit_Behavioral_State(_hProcess, _playerBase, 19))
                    {
                        //战斗中
                        return NodeState.Success;
                    }
                    else
                    {
                        //未战斗  卡住行为树
                        return NodeState.Running;
                    }
                }),

                new WaitNode(100),

                new ActionNode(() =>
                {
                    MoveTo(_hProcess, _playerBase, -67.60f, -277.39f, -58.05f);
                    return NodeState.Success;
                }),

                new WaitNode(500),

                new ActionNode(() =>
                {
                    ExecuteDynamicLua("CastSpellByName('闪现术');"); // 闪现
                    return NodeState.Success;
                }),

                new WaitNode(100),

                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, -63.41f, -304.37f, -56.21f, 0.5f))
                    {
                        StopMovement(_hProcess, _playerBase);
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                new WaitNode(1000),

                new ActionNode(() =>
                {
                    CastGroundAOE("暴风雪", -61.85374069f, -272.4850769f, -58.1432991f);  //暴风雪
                    return NodeState.Success;
                }),
                //等待时间分支
                WaitByServer(3000, 4000),


                new ActionNode(() =>
                {
                    CastGroundAOE("暴风雪", -62.29055786f, -287.2979431f, -58.57198334f);  //暴风雪
                    return NodeState.Success;
                }),

                //等待时间分支
                WaitByServer(5000, 7000),

                new ActionNode(() =>
                {
                    MoveTo(_hProcess, _playerBase, -62.69f, -295.14f, -57.48f);
                    return NodeState.Success;
                }),

                new WaitNode(100),

                //执行奥爆+自动吃宝石+自动吃治疗药水
                ArcaneExplosionNode(),
                //new WaitNode(100),
                //因为6和7波怪死亡尸体太近，所以暂时禁用第6波的拾取，如果先拾取第6波，一会拾取第7波的时候脚本还会去拾取第6波的尸体。
                //new AutoLootNode(this, () => _hProcess, () => _playerBase),

                new WaitNode(100),

                //第七波
                //走到等待区
                new ActionNode(() => {
                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 第七波开始");
                    return NodeState.Success;
                }),
                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, -68.66f, -288.05f, -58.10f, 0.5f))
                    {
                        StopMovement(_hProcess, _playerBase);
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                new WaitNode(100),

                //自动吃喝
                _autoDrinkEatNode,

                new WaitNode(100),

                new ActionNode(() =>
                {
                    _ = Jump(50);
                    return NodeState.Success;
                }),
                new WaitNode(500),

                new ActionNode(() =>
                {
                    ExecuteDynamicLua("CastSpellByName('寒冰护体');"); // 套盾
                    return NodeState.Success;
                }),

                new WaitNode(1600),

                //在此处等待110 走到尽头
                new ActionNode(() =>
                {
                    // 110起始坐标
                    float pointA_X = -118.173f;
                    float pointA_Y = -258.032f;

                    // 获取当前内存中所有的“扭木践踏者” (Entry ID: 11465)
                    List<Entry_ID_List> stompers = GetEntitiesByEntryId(_hProcess, 11465);

                    if (stompers.Count == 0)
                        return NodeState.Success; // 没刷怪，直接放行

                    // 判断距离上次打印是否超过了 10 秒
                    bool shouldLog = false;
                    if ((DateTime.Now - _debugThrottle_110A).TotalSeconds >= 10)
                    {
                        shouldLog = true;
                        _debugThrottle_110A = DateTime.Now; // 更新时间戳
                    }

                    int validCount = 0;       // 记录Z轴合格的怪物数量
                    float closestDist = 9999f;// 记录最近的距离
                    float closestZ = 0f;      // 记录最近怪物的Z轴

                    foreach (Entry_ID_List stomper in stompers)
                    {
                        // 温室地面的Z轴大约是 -55 左右，而楼上/室外的可能在 -4。
                        // 通过CE扫出来的数据看，真正的目标Z轴都在 -52 到 -57 之间。
                        if (stomper.Z > -20.0f)
                            continue; // 忽略高层或外面的大树

                        validCount++;
                        // 计算这只大树到监控点的距离
                        float distToA = (float)Math.Sqrt(Math.Pow(stomper.X - pointA_X, 2) + Math.Pow(stomper.Y - pointA_Y, 2));

                        // 记录最近的数据用于排错打印
                        if (distToA < closestDist)
                        {
                            closestDist = distToA;
                            closestZ = stomper.Z;
                        }
                        // 只要有【任意一只】真正的大树距离目标点小于 3.0 码
                        if (distToA < 3.0f)
                        {
                            // 你甚至可以在这里把你动态抓到的 GUID 存下来给选目标用
                            // ulong currentStomperGuid = stomper.Guid; 
                            Logger.Write($"[{CurrentPlayerName}] [监控系统] 发现目标已到位！距离: {distToA:F2} 码，放行！");
                            _debugThrottle_110A = DateTime.MinValue; // 放行前重置
                            return NodeState.Success;
                        }
                    }

                    // 排错输出：每 10 秒告诉你一次它在内存里到底看到了什么
                    if (shouldLog)
                    {
                        string distStr = validCount > 0 ? closestDist.ToString("F2") : "无(全被Z轴过滤)";
                        Logger.Write($"[{CurrentPlayerName}] [监控系统] 目标点 ({pointA_X}, {pointA_Y}) 等待中... 当前 扭木践踏者 总数:{stompers.Count}个 | Z轴及格数:{validCount}个 | 当前最近距离:{distStr}码 | 它的Z轴:{closestZ:F2}");
                    }

                    return NodeState.Running; // 没到位，继续监控等待
                }),

                new WaitNode(100),

                //在此处等待110 走到目标点
                new ActionNode(() =>
                {
                    // 110起始坐标
                    float pointA_X = -90.325f;
                    float pointA_Y = -317.286f;

                    // 获取当前内存中所有的“扭木践踏者” (Entry ID: 11465)
                    List<Entry_ID_List> stompers = GetEntitiesByEntryId(_hProcess, 11465);

                    if (stompers.Count == 0)
                        return NodeState.Success; // 没刷怪，直接放行

                    // 判断距离上次打印是否超过了 10 秒
                    bool shouldLog = false;
                    if ((DateTime.Now - _debugThrottle_110A).TotalSeconds >= 10)
                    {
                        shouldLog = true;
                        _debugThrottle_110A = DateTime.Now; // 更新时间戳
                    }

                    int validCount = 0;       // 记录Z轴合格的怪物数量
                    float closestDist = 9999f;// 记录最近的距离
                    float closestZ = 0f;      // 记录最近怪物的Z轴

                    foreach (Entry_ID_List stomper in stompers)
                    {
                        // 温室地面的Z轴大约是 -55 左右，而楼上/室外的可能在 -4。
                        // 通过CE扫出来的数据看，真正的目标Z轴都在 -52 到 -57 之间。
                        if (stomper.Z > -20.0f)
                            continue; // 忽略高层或外面的大树

                        validCount++;
                        // 计算这只大树到监控点的距离
                        float distToA = (float)Math.Sqrt(Math.Pow(stomper.X - pointA_X, 2) + Math.Pow(stomper.Y - pointA_Y, 2));

                        // 记录最近的数据用于排错打印
                        if (distToA < closestDist)
                        {
                            closestDist = distToA;
                            closestZ = stomper.Z;
                        }
                        // 只要有【任意一只】真正的大树距离目标点小于 3.0 码
                        if (distToA < 3.0f)
                        {
                            // 你甚至可以在这里把你动态抓到的 GUID 存下来给选目标用
                            // ulong currentStomperGuid = stomper.Guid; 
                            Logger.Write($"[{CurrentPlayerName}] [监控系统] 发现目标已到位！距离: {distToA:F2} 码，放行！");
                            _debugThrottle_110A = DateTime.MinValue; // 放行前重置
                            return NodeState.Success;
                        }
                    }

                    // 排错输出：每 10 秒告诉你一次它在内存里到底看到了什么
                    if (shouldLog)
                    {
                        string distStr = validCount > 0 ? closestDist.ToString("F2") : "无(全被Z轴过滤)";
                        Logger.Write($"[{CurrentPlayerName}] [监控系统] 目标点 ({pointA_X}, {pointA_Y}) 等待中... 当前 扭木践踏者 总数:{stompers.Count}个 | Z轴及格数:{validCount}个 | 当前最近距离:{distStr}码 | 它的Z轴:{closestZ:F2}");
                    }

                    return NodeState.Running; // 没到位，继续监控等待
                }),


                new WaitNode(100),

                new ActionNode(() =>
                {
                    CastGroundAOE("暴风雪", -102.9123383f, -292.1290894f, -57.89535904f);  //暴风雪
                    return NodeState.Success;
                }),
                //等待时间分支
                WaitByServer(4000, 6000),


                new ActionNode(() =>
                {
                    CastGroundAOE("暴风雪", -83.66368103f, -288.463562f, -57.72969055f);  //暴风雪
                    return NodeState.Success;
                }),

                //等待时间分支
                WaitByServer(4500, 7000),

                new ActionNode(() =>
                {
                    MoveTo(_hProcess, _playerBase, -73.96f, -288.39f, -57.97f);
                    return NodeState.Success;
                }),
                new WaitNode(100),

                //执行奥爆+自动吃宝石+自动吃治疗药水
                ArcaneExplosionNode(),

                new WaitNode(100),

                new AutoLootNode(this, () => _hProcess, () => _playerBase, () => 25.0f),

                new WaitNode(100),

                //第八波
                //走到安全区
                new ActionNode(() => {
                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 第八波开始");
                    return NodeState.Success;
                }),
                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, -68.66f, -288.05f, -58.104f, 0.5f))
                    {
                        StopMovement(_hProcess, _playerBase);
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                new WaitNode(100),

                //在此处等待110 走到尽头
                new ActionNode(() =>
                {
                    // 110起始坐标
                    float pointA_X = -118.173f;
                    float pointA_Y = -258.032f;

                    // 获取当前内存中所有的“扭木践踏者” (Entry ID: 11465)
                    List<Entry_ID_List> stompers = GetEntitiesByEntryId(_hProcess, 11465);

                    if (stompers.Count == 0)
                        return NodeState.Success; // 没刷怪，直接放行

                    // 判断距离上次打印是否超过了 10 秒
                    bool shouldLog = false;
                    if ((DateTime.Now - _debugThrottle_110A).TotalSeconds >= 10)
                    {
                        shouldLog = true;
                        _debugThrottle_110A = DateTime.Now; // 更新时间戳
                    }

                    int validCount = 0;       // 记录Z轴合格的怪物数量
                    float closestDist = 9999f;// 记录最近的距离
                    float closestZ = 0f;      // 记录最近怪物的Z轴

                    foreach (Entry_ID_List stomper in stompers)
                    {
                        // 温室地面的Z轴大约是 -55 左右，而楼上/室外的可能在 -4。
                        // 通过CE扫出来的数据看，真正的目标Z轴都在 -52 到 -57 之间。
                        if (stomper.Z > -20.0f)
                            continue; // 忽略高层或外面的大树

                        validCount++;
                        // 计算这只大树到监控点的距离
                        float distToA = (float)Math.Sqrt(Math.Pow(stomper.X - pointA_X, 2) + Math.Pow(stomper.Y - pointA_Y, 2));

                        // 记录最近的数据用于排错打印
                        if (distToA < closestDist)
                        {
                            closestDist = distToA;
                            closestZ = stomper.Z;
                        }
                        // 只要有【任意一只】真正的大树距离目标点小于 3.0 码
                        if (distToA < 3.0f)
                        {
                            // 你甚至可以在这里把你动态抓到的 GUID 存下来给选目标用
                            // ulong currentStomperGuid = stomper.Guid; 
                            Logger.Write($"[{CurrentPlayerName}] [监控系统] 发现目标已到位！距离: {distToA:F2} 码，放行！");
                            _debugThrottle_110A = DateTime.MinValue; // 放行前重置
                            return NodeState.Success;
                        }
                    }

                    // 排错输出：每 10 秒告诉你一次它在内存里到底看到了什么
                    if (shouldLog)
                    {
                        string distStr = validCount > 0 ? closestDist.ToString("F2") : "无(全被Z轴过滤)";
                        Logger.Write($"[{CurrentPlayerName}] [监控系统] 目标点 ({pointA_X}, {pointA_Y}) 等待中... 当前 扭木践踏者 总数:{stompers.Count}个 | Z轴及格数:{validCount}个 | 当前最近距离:{distStr}码 | 它的Z轴:{closestZ:F2}");
                    }

                    return NodeState.Running; // 没到位，继续监控等待
                }),

                new WaitNode(100),

                //在此处等待110 走到目标点
                new ActionNode(() =>
                {
                    // 110起始坐标
                    float pointA_X = -90.325f;
                    float pointA_Y = -317.286f;

                    // 获取当前内存中所有的“扭木践踏者” (Entry ID: 11465)
                    List<Entry_ID_List> stompers = GetEntitiesByEntryId(_hProcess, 11465);

                    if (stompers.Count == 0)
                        return NodeState.Success; // 没刷怪，直接放行

                    // 判断距离上次打印是否超过了 10 秒
                    bool shouldLog = false;
                    if ((DateTime.Now - _debugThrottle_110A).TotalSeconds >= 10)
                    {
                        shouldLog = true;
                        _debugThrottle_110A = DateTime.Now; // 更新时间戳
                    }

                    int validCount = 0;       // 记录Z轴合格的怪物数量
                    float closestDist = 9999f;// 记录最近的距离
                    float closestZ = 0f;      // 记录最近怪物的Z轴

                    foreach (Entry_ID_List stomper in stompers)
                    {
                        // 温室地面的Z轴大约是 -55 左右，而楼上/室外的可能在 -4。
                        // 通过CE扫出来的数据看，真正的目标Z轴都在 -52 到 -57 之间。
                        if (stomper.Z > -20.0f)
                            continue; // 忽略高层或外面的大树

                        validCount++;
                        // 计算这只大树到监控点的距离
                        float distToA = (float)Math.Sqrt(Math.Pow(stomper.X - pointA_X, 2) + Math.Pow(stomper.Y - pointA_Y, 2));

                        // 记录最近的数据用于排错打印
                        if (distToA < closestDist)
                        {
                            closestDist = distToA;
                            closestZ = stomper.Z;
                        }
                        // 只要有【任意一只】真正的大树距离目标点小于 3.0 码
                        if (distToA < 3.0f)
                        {
                            // 你甚至可以在这里把你动态抓到的 GUID 存下来给选目标用
                            // ulong currentStomperGuid = stomper.Guid; 
                            Logger.Write($"[{CurrentPlayerName}] [监控系统] 发现目标已到位！距离: {distToA:F2} 码，放行！");
                            _debugThrottle_110A = DateTime.MinValue; // 放行前重置
                            return NodeState.Success;
                        }
                    }

                    // 排错输出：每 10 秒告诉你一次它在内存里到底看到了什么
                    if (shouldLog)
                    {
                        string distStr = validCount > 0 ? closestDist.ToString("F2") : "无(全被Z轴过滤)";
                        Logger.Write($"[{CurrentPlayerName}] [监控系统] 目标点 ({pointA_X}, {pointA_Y}) 等待中... 当前 扭木践踏者 总数:{stompers.Count}个 | Z轴及格数:{validCount}个 | 当前最近距离:{distStr}码 | 它的Z轴:{closestZ:F2}");
                    }

                    return NodeState.Running; // 没到位，继续监控等待
                }),

                new WaitNode(100),

                //开始移动

                new ActionNode(() =>
                {
                    MoveTo(_hProcess, _playerBase, -110.53f, -282.36f, -57.63f);
                    return NodeState.Success;
                }),

                new WaitNode(300),

                new ActionNode(() =>
                {
                    ExecuteDynamicLua("CastSpellByName('闪现术');");  // 闪现术
                    return NodeState.Success;
                }),

                new WaitNode(100),

                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, -110.53f, -282.36f, -57.63f))
                    {
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),


                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, -157.04f, -266.82f, -50.49f))
                    {
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                //走到等待区
                new ActionNode(() =>
                {
                    if (MoveTo(_hProcess, _playerBase, -156.33f, -242.67f, -52.94f, 0.5f))
                    {
                        StopMovement(_hProcess, _playerBase);
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                new WaitNode(100),

                //自动吃喝
                _autoDrinkEatNode,

                new WaitNode(100),

                new ActionNode(() =>
                {
                    _ = Jump(50);
                    return NodeState.Success;
                }),

                new WaitNode(500),

                new ActionNode(() =>
                {
                    ExecuteDynamicLua("CastSpellByName('寒冰护体');"); // 套盾
                    return NodeState.Success;
                }),

                new WaitNode(1600),

                new ActionNode(() =>
                {
                    CastGroundAOE("暴风雪", -151.8065948f, -209.4206238f, -53.41404724f);  //暴风雪
                    return NodeState.Success;
                }),
                //等待时间分支
                WaitByServer(5000, 6000),


                new ActionNode(() =>
                {
                    CastGroundAOE("暴风雪", -153.3741302f, -225.2125092f, -54.96573639f);  //暴风雪
                    return NodeState.Success;
                }),

                //等待时间分支
                WaitByServer(4500, 7000),

                new ActionNode(() =>
                {
                    MoveTo(_hProcess, _playerBase, -155.21f, -236.37f, -53.67f);
                    return NodeState.Success;
                }),
                new WaitNode(100),

                //执行奥爆+自动吃宝石+自动吃治疗药水
                ArcaneExplosionNode(),

                //拾取
                new AutoLootNode(this, () => _hProcess, () => _playerBase, () => 15.0f),

                new WaitNode(100),
                //小退前进行吃喝
                _autoDrinkEatNode,

                new WaitNode(500),

                // 副本打完后的代码（或者重置副本的节点）
                new ActionNode(() => {
                    // 无脑记录刷本次数即可，判断留给进本前的节点去做，减少不必要的内存读取操作
                    _Dungeon_attempts_Count++;
                    Logger.Write($"[{CurrentPlayerName}] 成功完成一轮副本，当前累计次数: {_Dungeon_attempts_Count}");

                    return NodeState.Success;
                }),

                new WaitNode(100),
                // 小退，然后通知队长重置副本
                new ActionNode(() => {
                    // 拦截行为树循环
                    if (IPCManager.IsResetRequestPending(_teamName))
                    {
                        return NodeState.Running;
                    }

                    Logger.Write($"[{CurrentPlayerName}] [小退系统] 准备小退并通知队长 [{_teamName}] 离开队伍");

                    this.PauseGlobalAutoLogin = true;

                    // 1. 发送 IPC 信号，带上目标队长名字
                    IPCManager.RequestReset(_teamName);

                    // 2. 执行小退指令
                    ExecuteDynamicLua("Logout()");

                    Task.Run(async () => {
                        Logger.Write($"[{CurrentPlayerName}] [小退系统] 等待队长 [{_teamName}] 离队信号");

                        // 阶段 A：只等待专属队长的完成信号
                        while (!IPCManager.IsResetCompleted(_teamName))
                        {
                            await Task.Delay(500);
                        }

                        Logger.Write($"[{CurrentPlayerName}] [小退系统] 收到队长信号！正在确认角色是否已退出世界");

                        while (IsInWorld(_hProcess))
                        {
                            await Task.Delay(500);
                        }

                        while (true)
                        {
                            if (IsInWorld(_hProcess) || !IPCManager.IsResetRequestPending(_teamName) && !IPCManager.IsResetCompleted(_teamName) || !_needGoHome)
                            {
                                Logger.Write($"[{CurrentPlayerName}] [小退系统] 角色已上线，子线程任务完成！");
                                // 兜底擦除黑板
                                IPCManager.ClearSignals(_teamName);
                                _rootTree?.Reset();
                                this.PauseGlobalAutoLogin = false;
                                break;
                            }

                            if ((DateTime.Now - lastEnterWorldTime).TotalSeconds >= 8)
                            {
                                ExecuteDynamicLua("EnterWorld()");
                                lastEnterWorldTime = DateTime.Now;
                            }
                            await Task.Delay(100);
                        }
                    });

                    return NodeState.Running;
                })
            );

            // ========================================================
            // 联盟前往厄运东的全部航点坐标   _Alliance
            // ========================================================

            // 0、旅店到邮箱路线   联盟专属
            List<Waypoint> routeToMail_Alliance = new List<Waypoint> {
                // 确保不出错，精细坐标移动
                new Waypoint { X = -4366.23f, Y = 3301.95f, Z = 13.56f, Type = Precise }, // 贴脸 NPC  1
                new Waypoint { X = -4374.59f, Y = 3298.38f, Z = 13.57f, Type = Rough },
                new Waypoint { X = -4375.71f, Y = 3289.37f, Z = 13.56f, Type = Rough },
                new Waypoint { X = -4376.97f, Y = 3281.43f, Z = 13.56f, Type = Rough },
                new Waypoint { X = -4383.05f, Y = 3272.73f, Z = 13.54f, Type = Rough },
                new Waypoint { X = -4388.75f, Y = 3271.77f, Z = 13.71f, Type = Rough },
                new Waypoint { X = -4396.82f, Y = 3270.92f, Z = 12.15f, Type = Rough }, //已到邮箱附近

            };

            // 1、旅店到码头路线   联盟专属
            List<Waypoint> routeToHome_Alliance = new List<Waypoint> {
                // 确保不出错，精细坐标移动
                new Waypoint { X = -4366.23f, Y = 3301.95f, Z = 13.56f, Type = Precise }, // 贴脸 NPC  1
                new Waypoint { X = -4371.70f, Y = 3297.41f, Z = 13.57f, Type = Precise },
                new Waypoint { X = -4370.94f, Y = 3292.92f, Z = 13.57f, Type = Precise },
                new Waypoint { X = -4365.39f, Y = 3292.02f, Z = 16.24f, Type = Precise },
                new Waypoint { X = -4359.26f, Y = 3291.70f, Z = 18.31f, Type = Precise },
                new Waypoint { X = -4349.62f, Y = 3289.38f, Z = 18.67f, Type = Rough },
                new Waypoint { X = -4336.02f, Y = 3286.91f, Z = 18.25f, Type = Rough },
                new Waypoint { X = -4322.23f, Y = 3286.02f, Z = 18.40f, Type = Rough },
                new Waypoint { X = -4312.30f, Y = 3286.57f, Z = 18.08f, Type = Rough },
                new Waypoint { X = -4298.59f, Y = 3286.49f, Z = 13.61f, Type = Rough },
                new Waypoint { X = -4279.84f, Y = 3285.64f, Z = 10.81f, Type = Rough },
                new Waypoint { X = -4262.83f, Y = 3285.45f, Z = 10.70f, Type = Rough },
                new Waypoint { X = -4242.32f, Y = 3284.24f, Z = 10.81f, Type = Rough },
                new Waypoint { X = -4222.73f, Y = 3283.55f, Z = 8.16f, Type = Rough },
                new Waypoint { X = -4214.75f, Y = 3283.06f, Z = 6.31f, Type = Rough },  //已到达码头
            };

            // 2、岸边码头到副本门口的路线   联盟专属
            List<Waypoint> routeToInstanceDoor_Alliance = new List<Waypoint>
            {
                new Waypoint { X = -4348.90f, Y = 2426.92f, Z = 6.70f, Type = Rough },
                new Waypoint { X = -4347.54f, Y = 2401.79f, Z = 8.09f, Type = Rough },
                new Waypoint { X = -4347.63f, Y = 2379.94f, Z = 7.77f, Type = Rough },
                new Waypoint { X = -4347.63f, Y = 2348.05f, Z = 8.03f, Type = Rough },
                new Waypoint { X = -4346.94f, Y = 2331.29f, Z = 8.50f, Type = Rough },
                new Waypoint { X = -4346.90f, Y = 2315.94f, Z = 7.38f, Type = Rough },
                new Waypoint { X = -4349.27f, Y = 2301.93f, Z = 6.62f, Type = Rough },
                new Waypoint { X = -4395.00f, Y = 2243.01f, Z = 6.02f, Type = Rough },
                new Waypoint { X = -4432.30f, Y = 2154.08f, Z = 20.92f, Type = Rough },
                new Waypoint { X = -4447.47f, Y = 2088.23f, Z = 41.91f, Type = Rough },
                new Waypoint { X = -4457.74f, Y = 2054.63f, Z = 45.46f, Type = Rough },
                new Waypoint { X = -4493.03f, Y = 2043.68f, Z = 49.94f, Type = Rough },
                new Waypoint { X = -4544.82f, Y = 2032.73f, Z = 45.11f, Type = Rough },
                new Waypoint { X = -4571.64f, Y = 2024.01f, Z = 46.60f, Type = Rough },
                new Waypoint { X = -4597.96f, Y = 2007.93f, Z = 52.82f, Type = Rough },
                new Waypoint { X = -4619.65f, Y = 1989.20f, Z = 59.14f, Type = Rough },
                new Waypoint { X = -4650.67f, Y = 1962.21f, Z = 66.75f, Type = Rough },
                new Waypoint { X = -4660.20f, Y = 1959.11f, Z = 68.28f, Type = Rough },
                new Waypoint { X = -4669.02f, Y = 1952.96f, Z = 69.73f, Type = Rough },
                new Waypoint { X = -4676.52f, Y = 1946.12f, Z = 71.32f, Type = Rough },
                new Waypoint { X = -4678.75f, Y = 1937.83f, Z = 72.81f, Type = Rough },
                new Waypoint { X = -4677.96f, Y = 1928.53f, Z = 74.79f, Type = Rough },
                new Waypoint { X = -4675.72f, Y = 1918.15f, Z = 78.07f, Type = Rough },
                new Waypoint { X = -4671.42f, Y = 1900.63f, Z = 79.97f, Type = Rough },
                new Waypoint { X = -4669.68f, Y = 1886.74f, Z = 81.04f, Type = Rough },
                new Waypoint { X = -4669.41f, Y = 1872.53f, Z = 83.80f, Type = Rough },
                new Waypoint { X = -4670.80f, Y = 1855.52f, Z = 87.04f, Type = Rough },
                new Waypoint { X = -4682.26f, Y = 1827.39f, Z = 90.22f, Type = Rough },
                new Waypoint { X = -4698.42f, Y = 1798.57f, Z = 91.53f, Type = Rough },
                new Waypoint { X = -4711.48f, Y = 1768.94f, Z = 92.22f, Type = Rough },
                new Waypoint { X = -4726.73f, Y = 1738.46f, Z = 93.24f, Type = Rough },
                new Waypoint { X = -4736.04f, Y = 1715.57f, Z = 94.30f, Type = Rough },
                new Waypoint { X = -4742.27f, Y = 1692.11f, Z = 92.31f, Type = Rough },
                new Waypoint { X = -4744.22f, Y = 1653.66f, Z = 88.64f, Type = Rough },
                new Waypoint { X = -4745.25f, Y = 1602.47f, Z = 83.73f, Type = Rough },
                new Waypoint { X = -4745.57f, Y = 1567.03f, Z = 85.30f, Type = Rough },
                new Waypoint { X = -4747.04f, Y = 1528.11f, Z = 88.55f, Type = Rough },
                new Waypoint { X = -4755.79f, Y = 1460.56f, Z = 92.87f, Type = Rough },
                new Waypoint { X = -4779.91f, Y = 1442.93f, Z = 90.50f, Type = Rough },
                new Waypoint { X = -4814.19f, Y = 1408.53f, Z = 83.10f, Type = Rough },
                new Waypoint { X = -4835.87f, Y = 1381.90f, Z = 80.12f, Type = Rough },
                new Waypoint { X = -4841.61f, Y = 1347.82f, Z = 80.46f, Type = Rough },
                new Waypoint { X = -4811.14f, Y = 1322.21f, Z = 84.51f, Type = Rough },
                new Waypoint { X = -4764.35f, Y = 1309.88f, Z = 89.82f, Type = Rough },
                new Waypoint { X = -4639.63f, Y = 1333.08f, Z = 97.67f, Type = Rough },
                new Waypoint { X = -4588.10f, Y = 1330.21f, Z = 105.91f, Type = Rough },
                new Waypoint { X = -4561.39f, Y = 1299.77f, Z = 117.74f, Type = Precise },  //精准移动
                new Waypoint { X = -4551.33f, Y = 1304.45f, Z = 123.92f, Type = Precise },  //精准移动
                new Waypoint { X = -4542.19f, Y = 1305.52f, Z = 126.41f, Type = Precise },  //精准移动
                new Waypoint { X = -4516.79f, Y = 1311.82f, Z = 123.84f, Type = Precise },  //精准移动
                new Waypoint { X = -4482.22f, Y = 1314.97f, Z = 123.79f, Type = Rough },
                new Waypoint { X = -4470.36f, Y = 1330.66f, Z = 124.31f, Type = Rough },   //副本楼梯大门口
                new Waypoint { X = -4447.79f, Y = 1333.60f, Z = 126.00f, Type = Rough },
                new Waypoint { X = -4423.21f, Y = 1346.29f, Z = 131.42f, Type = Rough },
                new Waypoint { X = -4404.95f, Y = 1346.42f, Z = 139.88f, Type = Rough },
                new Waypoint { X = -4380.19f, Y = 1346.21f, Z = 151.55f, Type = Rough },     //在此处下马套盾上马
            };

            // 【墓地接驳线】   联盟专属
            // 从羽月要塞墓地跑到主干道的几个点     联盟专属
            List<Waypoint> route_GY_Feathermoon_Alliance = new List<Waypoint>
            {
                new Waypoint { X = -4574.11f, Y = 3228.86f, Z = 8.96f, Type = Rough },
                new Waypoint { X = -4531.37f, Y = 3234.14f, Z = 8.90f, Type = Rough },
                new Waypoint { X = -4493.90f, Y = 3243.67f, Z = 10.79f, Type = Rough },
                new Waypoint { X = -4462.92f, Y = 3254.90f, Z = 14.73f, Type = Rough },
                new Waypoint { X = -4417.06f, Y = 3238.20f, Z = 12.72f, Type = Rough },
                new Waypoint { X = -4397.40f, Y = 3232.88f, Z = 12.02f, Type = Rough },
                new Waypoint { X = -4367.63f, Y = 3228.98f, Z = 12.78f, Type = Rough },  // 已经到达旅店门口  跑图主路上了
            };

            // 从双塔山墓地跑到主干道的几个点       联盟专属
            List<Waypoint> route_GY_TwinColossals_Alliance = new List<Waypoint> {
                new Waypoint { X = -4576.17f, Y = 1642.00f, Z = 95.41f, Type = Rough },
                new Waypoint { X = -4569.93f, Y = 1651.87f, Z = 99.52f, Type = Rough },
                new Waypoint { X = -4568.52f, Y = 1665.61f, Z = 103.43f, Type = Rough },
                new Waypoint { X = -4583.81f, Y = 1671.20f, Z = 109.83f, Type = Rough },
                new Waypoint { X = -4594.57f, Y = 1676.61f, Z = 110.73f, Type = Rough },
                new Waypoint { X = -4601.40f, Y = 1680.36f, Z = 115.03f, Type = Rough },
                new Waypoint { X = -4619.10f, Y = 1680.59f, Z = 115.48f, Type = Rough },
                new Waypoint { X = -4637.25f, Y = 1669.34f, Z = 115.40f, Type = Rough },
                new Waypoint { X = -4650.06f, Y = 1662.25f, Z = 115.48f, Type = Rough },
                new Waypoint { X = -4666.05f, Y = 1657.16f, Z = 115.50f, Type = Rough },
                new Waypoint { X = -4683.93f, Y = 1655.54f, Z = 109.75f, Type = Rough },
                new Waypoint { X = -4708.45f, Y = 1653.94f, Z = 100.57f, Type = Rough },
                new Waypoint { X = -4734.53f, Y = 1652.94f, Z = 87.03f, Type = Rough },
                new Waypoint { X = -4743.54f, Y = 1651.66f, Z = 88.49f, Type = Rough },
            };

            // 从莫沙彻营地(外围)墓地，跑到主干道的几个点   联盟专属
            List<Waypoint> route_GY_Mojache_Alliance = new List<Waypoint> {
                new Waypoint { X = -4449.76f, Y = 388.63f, Z = 52.87f, Type = Rough },
                new Waypoint { X = -4464.85f, Y = 408.23f, Z = 54.61f, Type = Rough },
                new Waypoint { X = -4496.88f, Y = 408.42f, Z = 49.59f, Type = Rough },
                new Waypoint { X = -4573.61f, Y = 423.14f, Z = 41.51f, Type = Rough },
                new Waypoint { X = -4603.37f, Y = 448.68f, Z = 42.89f, Type = Rough },
                new Waypoint { X = -4622.56f, Y = 504.38f, Z = 37.39f, Type = Rough },
                new Waypoint { X = -4642.61f, Y = 563.83f, Z = 39.79f, Type = Rough },
                new Waypoint { X = -4654.49f, Y = 610.89f, Z = 49.30f, Type = Rough },
                new Waypoint { X = -4670.78f, Y = 668.07f, Z = 63.15f, Type = Rough },
                new Waypoint { X = -4681.91f, Y = 723.97f, Z = 76.58f, Type = Rough },
                new Waypoint { X = -4670.80f, Y = 752.47f, Z = 82.78f, Type = Rough },
                new Waypoint { X = -4660.36f, Y = 771.37f, Z = 83.83f, Type = Rough },
                new Waypoint { X = -4648.81f, Y = 829.83f, Z = 82.65f, Type = Rough },
                new Waypoint { X = -4643.68f, Y = 865.25f, Z = 85.39f, Type = Rough },
                new Waypoint { X = -4660.60f, Y = 886.45f, Z = 86.91f, Type = Rough },
                new Waypoint { X = -4693.47f, Y = 926.51f, Z = 95.56f, Type = Rough },
                new Waypoint { X = -4691.15f, Y = 945.15f, Z = 98.84f, Type = Rough },
                new Waypoint { X = -4691.92f, Y = 969.01f, Z = 99.64f, Type = Rough },
                new Waypoint { X = -4718.80f, Y = 1011.86f, Z = 108.63f, Type = Rough },
                new Waypoint { X = -4694.92f, Y = 1035.84f, Z = 113.32f, Type = Rough },
                new Waypoint { X = -4680.00f, Y = 1038.54f, Z = 120.00f, Type = Rough },
                new Waypoint { X = -4663.62f, Y = 1068.80f, Z = 109.39f, Type = Rough },
                new Waypoint { X = -4645.12f, Y = 1090.71f, Z = 92.09f, Type = Rough },
                new Waypoint { X = -4632.21f, Y = 1103.47f, Z = 96.25f, Type = Rough },
                new Waypoint { X = -4641.95f, Y = 1116.38f, Z = 85.76f, Type = Rough },
                new Waypoint { X = -4650.97f, Y = 1127.90f, Z = 85.76f, Type = Rough },
                new Waypoint { X = -4670.76f, Y = 1158.40f, Z = 88.31f, Type = Rough },
                new Waypoint { X = -4666.44f, Y = 1200.08f, Z = 94.79f, Type = Rough },
                new Waypoint { X = -4638.11f, Y = 1243.96f, Z = 101.48f, Type = Rough },
                new Waypoint { X = -4618.39f, Y = 1280.24f, Z = 105.84f, Type = Rough },
                new Waypoint { X = -4581.36f, Y = 1308.35f, Z = 109.25f, Type = Rough },
                new Waypoint { X = -4559.00f, Y = 1325.68f, Z = 113.62f, Type = Rough },
                new Waypoint { X = -4520.99f, Y = 1331.07f, Z = 117.87f, Type = Rough },
                new Waypoint { X = -4465.49f, Y = 1332.13f, Z = 124.91f, Type = Rough },
                new Waypoint { X = -4405.75f, Y = 1332.50f, Z = 139.56f, Type = Rough },
                new Waypoint { X = -4369.93f, Y = 1332.98f, Z = 156.44f, Type = Rough },
                new Waypoint { X = -4353.27f, Y = 1333.62f, Z = 159.23f, Type = Rough },
            };

            // ========================================================
            // 部落前往厄运东的全部航点坐标  _Horde
            // ========================================================

            // 【旅店到邮箱路线】   部落专属
            List<Waypoint> routeToMail_Horde = new List<Waypoint> {
                // 确保不出错，精细坐标移动
                new Waypoint { X = -4484.95f, Y = 233.53f, Z = 48.40f, Type = Precise }, // 贴脸 NPC  1
                new Waypoint { X = -4472.29f, Y = 240.17f, Z = 47.34f, Type = Precise }, // 2
                new Waypoint { X = -4467.55f, Y = 232.28f, Z = 47.34f, Type = Precise }, // 3
                new Waypoint { X = -4450.55f, Y = 242.04f, Z = 39.11f, Type = Precise }, // 4
                new Waypoint { X = -4453.34f, Y = 246.70f, Z = 39.11f, Type = Precise }, // 旅店中心过道   5
                new Waypoint { X = -4442.87f, Y = 251.40f, Z = 39.11f, Type = Precise }, // 6
                new Waypoint { X = -4432.91f, Y = 258.55f, Z = 37.97f, Type = Rough },
                new Waypoint { X = -4418.82f, Y = 243.25f, Z = 30.78f, Type = Rough },
                new Waypoint { X = -4412.80f, Y = 240.29f, Z = 28.54f, Type = Rough },
                new Waypoint { X = -4406.09f, Y = 235.96f, Z = 26.80f, Type = Rough },   //已经在邮箱附近
            };

            // 【旅店室内路线】   部落专属
            List<Waypoint> routeToHome_Horde = new List<Waypoint> {
                // 确保不出错，精细坐标移动
                new Waypoint { X = -4484.95f, Y = 233.53f, Z = 48.40f, Type = Precise }, // 贴脸 NPC  1
                new Waypoint { X = -4472.29f, Y = 240.17f, Z = 47.34f, Type = Precise }, // 2
                new Waypoint { X = -4467.55f, Y = 232.28f, Z = 47.34f, Type = Precise }, // 3
                new Waypoint { X = -4450.55f, Y = 242.04f, Z = 39.11f, Type = Precise }, // 4
                new Waypoint { X = -4453.34f, Y = 246.70f, Z = 39.11f, Type = Precise }, // 旅店中心过道   5
                new Waypoint { X = -4442.87f, Y = 251.40f, Z = 39.11f, Type = Precise }, // 6

                //旅店老板门口坐标 new Waypoint { X = -4432.91f, Y = 258.55f, Z = 37.97f, Type = Precise }, // 到达此坐标后上马
            };

            // 【旅店大门口到副本门口路线】      部落专属
            List<Waypoint> routeToInstanceDoor_Horde = new List<Waypoint>
            {
                new Waypoint { X = -4418.82f, Y = 243.25f, Z = 30.78f, Type = Rough },
                new Waypoint { X = -4403.49f, Y = 229.91f, Z = 25.64f, Type = Rough },
                new Waypoint { X = -4398.39f, Y = 238.98f, Z = 25.47f, Type = Rough },
                new Waypoint { X = -4402.43f, Y = 262.17f, Z = 25.27f, Type = Rough },
                new Waypoint { X = -4405.85f, Y = 272.49f, Z = 25.15f, Type = Rough },
                new Waypoint { X = -4426.07f, Y = 277.45f, Z = 27.52f, Type = Rough },
                new Waypoint { X = -4448.31f, Y = 291.72f, Z = 34.13f, Type = Rough },
                new Waypoint { X = -4463.92f, Y = 302.38f, Z = 38.73f, Type = Rough }, // 营地门口坐标 
                new Waypoint { X = -4478.32f, Y = 309.88f, Z = 39.68f, Type = Rough },
                new Waypoint { X = -4493.83f, Y = 316.58f, Z = 38.10f, Type = Rough },
                new Waypoint { X = -4513.27f, Y = 324.09f, Z = 35.64f, Type = Rough },
                new Waypoint { X = -4522.81f, Y = 329.02f, Z = 33.96f, Type = Rough },
                new Waypoint { X = -4532.67f, Y = 336.16f, Z = 33.20f, Type = Rough },
                new Waypoint { X = -4545.27f, Y = 345.14f, Z = 31.98f, Type = Rough },
                new Waypoint { X = -4556.50f, Y = 353.97f, Z = 31.94f, Type = Rough },
                new Waypoint { X = -4569.78f, Y = 364.22f, Z = 33.19f, Type = Rough },
                new Waypoint { X = -4586.85f, Y = 379.50f, Z = 34.52f, Type = Rough },
                new Waypoint { X = -4612.06f, Y = 404.62f, Z = 35.72f, Type = Rough },
                new Waypoint { X = -4619.28f, Y = 438.82f, Z = 36.57f, Type = Rough },
                new Waypoint { X = -4626.48f, Y = 510.28f, Z = 37.39f, Type = Rough },
                new Waypoint { X = -4642.24f, Y = 560.30f, Z = 38.83f, Type = Rough },
                new Waypoint { X = -4664.06f, Y = 635.87f, Z = 55.08f, Type = Rough },
                new Waypoint { X = -4680.55f, Y = 704.76f, Z = 72.54f, Type = Rough },
                new Waypoint { X = -4683.45f, Y = 730.35f, Z = 77.79f, Type = Rough },
                new Waypoint { X = -4658.85f, Y = 771.41f, Z = 83.78f, Type = Rough },
                new Waypoint { X = -4646.59f, Y = 846.71f, Z = 82.47f, Type = Rough },
                new Waypoint { X = -4641.81f, Y = 864.97f, Z = 85.43f, Type = Rough },
                new Waypoint { X = -4676.54f, Y = 905.89f, Z = 87.43f, Type = Rough },
                new Waypoint { X = -4692.45f, Y = 921.38f, Z = 93.88f, Type = Rough },
                new Waypoint { X = -4695.45f, Y = 929.31f, Z = 96.72f, Type = Rough },
                new Waypoint { X = -4688.00f, Y = 961.47f, Z = 98.87f, Type = Rough },
                new Waypoint { X = -4709.67f, Y = 995.14f, Z = 104.24f, Type = Rough },
                new Waypoint { X = -4714.20f, Y = 1013.99f, Z = 109.10f, Type = Rough },
                new Waypoint { X = -4736.84f, Y = 1012.81f, Z = 109.40f, Type = Rough },
                new Waypoint { X = -4779.08f, Y = 1028.71f, Z = 113.19f, Type = Rough },
                new Waypoint { X = -4853.15f, Y = 1063.25f, Z = 91.92f, Type = Rough },
                new Waypoint { X = -4839.31f, Y = 1105.29f, Z = 90.65f, Type = Rough },
                new Waypoint { X = -4815.32f, Y = 1141.81f, Z = 90.67f, Type = Rough },
                new Waypoint { X = -4835.49f, Y = 1228.20f, Z = 84.86f, Type = Rough },
                new Waypoint { X = -4857.62f, Y = 1281.24f, Z = 82.11f, Type = Rough },
                new Waypoint { X = -4827.80f, Y = 1301.71f, Z = 83.37f, Type = Rough },
                new Waypoint { X = -4764.35f, Y = 1309.88f, Z = 89.82f, Type = Rough },
                new Waypoint { X = -4639.63f, Y = 1333.08f, Z = 97.67f, Type = Rough },
                new Waypoint { X = -4588.10f, Y = 1330.21f, Z = 105.91f, Type = Rough },
                new Waypoint { X = -4561.39f, Y = 1299.77f, Z = 117.74f, Type = Precise },  //精准移动
                new Waypoint { X = -4551.33f, Y = 1304.45f, Z = 123.92f, Type = Precise },  //精准移动
                new Waypoint { X = -4542.19f, Y = 1305.52f, Z = 126.41f, Type = Precise },  //精准移动
                new Waypoint { X = -4516.79f, Y = 1311.82f, Z = 123.84f, Type = Precise },  //精准移动
                new Waypoint { X = -4482.22f, Y = 1314.97f, Z = 123.79f, Type = Rough },
                new Waypoint { X = -4470.36f, Y = 1330.66f, Z = 124.31f, Type = Rough },   //副本楼梯大门口
                new Waypoint { X = -4447.79f, Y = 1333.60f, Z = 126.00f, Type = Rough },
                new Waypoint { X = -4423.21f, Y = 1346.29f, Z = 131.42f, Type = Rough },
                new Waypoint { X = -4404.95f, Y = 1346.42f, Z = 139.88f, Type = Rough },
                new Waypoint { X = -4380.19f, Y = 1346.21f, Z = 151.55f, Type = Rough },     //在此处下马套盾上马
            };

            // 【墓地接驳线】 部落专属
            // 从双塔山墓地跑到部落主干道的几个点       部落专属
            List<Waypoint> route_GY_TwinColossals_Horde = new List<Waypoint> {
                new Waypoint { X = -4566.47f, Y = 1618.61f, Z = 95.40f, Type = Rough },
                new Waypoint { X = -4562.23f, Y = 1589.08f, Z = 101.48f, Type = Rough },
                new Waypoint { X = -4555.82f, Y = 1552.45f, Z = 103.58f, Type = Rough },
                new Waypoint { X = -4539.62f, Y = 1495.03f, Z = 101.72f, Type = Rough },
                new Waypoint { X = -4554.84f, Y = 1452.41f, Z = 102.82f, Type = Rough },
                new Waypoint { X = -4564.44f, Y = 1418.47f, Z = 103.54f, Type = Rough },
                new Waypoint { X = -4572.15f, Y = 1384.34f, Z = 107.89f, Type = Rough },
                new Waypoint { X = -4573.70f, Y = 1360.08f, Z = 107.47f, Type = Rough },
                new Waypoint { X = -4572.17f, Y = 1336.48f, Z = 109.70f, Type = Rough },
            };

            // 从莫沙彻营地(外围)墓地，跑到部落主干道的几个点   部落专属
            List<Waypoint> route_GY_Mojache_Horde = new List<Waypoint> {
                new Waypoint { X = -4441.58f, Y = 356.69f, Z = 51.21f, Type = Rough },
                new Waypoint { X = -4448.94f, Y = 346.19f, Z = 51.02f, Type = Rough },
                new Waypoint { X = -4456.46f, Y = 333.80f, Z = 50.19f, Type = Rough },
                new Waypoint { X = -4460.53f, Y = 323.80f, Z = 50.52f, Type = Rough },
                new Waypoint { X = -4467.74f, Y = 307.09f, Z = 39.08f, Type = Rough },
            };

            // ========================================================
            //                     公共坐标区域  
            // ========================================================
            // 【厄运东跑尸专线 部落联盟通用】（从双塔山墓地，一路直达副本门口的撞门点之前）  
            List<Waypoint> route_CorpseRun_Instance_East = new List<Waypoint>
            {
                new Waypoint { X = -4564.94f, Y = 1620.16f, Z = 95.46f, Type = Rough },
                new Waypoint { X = -4548.61f, Y = 1594.45f, Z = 101.39f, Type = Rough },
                new Waypoint { X = -4522.95f, Y = 1566.42f, Z = 109.52f, Type = Rough },
                new Waypoint { X = -4510.02f, Y = 1554.83f, Z = 116.04f, Type = Rough },
                new Waypoint { X = -4498.93f, Y = 1549.88f, Z = 124.33f, Type = Rough },
                new Waypoint { X = -4466.53f, Y = 1540.86f, Z = 126.70f, Type = Rough },
                new Waypoint { X = -4454.35f, Y = 1538.35f, Z = 128.14f, Type = Rough },
                new Waypoint { X = -4430.32f, Y = 1538.12f, Z = 126.92f, Type = Precise },
                new Waypoint { X = -4426.54f, Y = 1533.60f, Z = 127.93f, Type = Precise },
                new Waypoint { X = -4422.81f, Y = 1528.71f, Z = 128.53f, Type = Precise },
                new Waypoint { X = -4420.91f, Y = 1526.13f, Z = 128.69f, Type = Precise },
                new Waypoint { X = -4418.10f, Y = 1523.09f, Z = 128.16f, Type = Precise },
                new Waypoint { X = -4409.55f, Y = 1527.06f, Z = 133.07f, Type = Precise },
                new Waypoint { X = -4400.52f, Y = 1527.30f, Z = 137.99f, Type = Precise },
                new Waypoint { X = -4398.24f, Y = 1520.35f, Z = 143.86f, Type = Precise },
                new Waypoint { X = -4398.04f, Y = 1516.23f, Z = 148.24f, Type = Precise },
                new Waypoint { X = -4394.38f, Y = 1514.15f, Z = 150.61f, Type = Precise },
                new Waypoint { X = -4380.73f, Y = 1506.34f, Z = 150.61f, Type = Rough },
                new Waypoint { X = -4357.44f, Y = 1439.77f, Z = 150.61f, Type = Rough },
                new Waypoint { X = -4350.54f, Y = 1384.29f, Z = 153.91f, Type = Rough },
                new Waypoint { X = -4345.36f, Y = 1348.85f, Z = 159.24f, Type = Rough },
                new Waypoint { X = -4311.44f, Y = 1330.46f, Z = 159.24f, Type = Rough },
                new Waypoint { X = -4242.24f, Y = 1326.86f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4192.48f, Y = 1303.87f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4180.40f, Y = 1238.10f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4163.51f, Y = 1155.27f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4083.80f, Y = 1129.01f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4039.61f, Y = 1119.26f, Z = 159.74f, Type = Rough },
                new Waypoint { X = -4019.36f, Y = 1073.83f, Z = 159.80f, Type = Rough },
                new Waypoint { X = -3982.84f, Y = 1066.04f, Z = 161.05f, Type = Rough },
                new Waypoint { X = -3981.59f, Y = 1114.37f, Z = 161.02f, Type = Rough },
                new Waypoint { X = -3959.85f, Y = 1114.22f, Z = 161.05f, Type = Rough },
                new Waypoint { X = -3914.03f, Y = 1109.41f, Z = 149.01f, Type = Rough },
                new Waypoint { X = -3911.69f, Y = 1062.49f, Z = 148.89f, Type = Rough },
                new Waypoint { X = -3842.59f, Y = 1038.34f, Z = 154.79f, Type = Rough },
                new Waypoint { X = -3822.10f, Y = 998.85f, Z = 150.04f, Type = Rough },
                new Waypoint { X = -3793.22f, Y = 966.55f, Z = 156.64f, Type = Rough },
                new Waypoint { X = -3780.18f, Y = 935.86f, Z = 161.03f, Type = Rough },
                new Waypoint { X = -3741.32f, Y = 934.54f, Z = 160.99f, Type = Rough },  // 门外（退出来的点）
            };

            // 【厄运之槌大门口楼梯处到枢纽专线(厄运南)  部落联盟通用】   Precise=精确移动，Rough=模糊移动
            List<Waypoint> routeToInstance = new List<Waypoint> {
                new Waypoint { X = -4366.18f, Y = 1344.59f, Z = 158.16f, Type = Rough },   //已上坡
                new Waypoint { X = -4346.82f, Y = 1343.97f, Z = 159.23f, Type = Rough },
                new Waypoint { X = -4330.61f, Y = 1342.28f, Z = 159.23f, Type = Rough },
                new Waypoint { X = -4320.14f, Y = 1337.36f, Z = 159.23f, Type = Rough },
                new Waypoint { X = -4308.31f, Y = 1332.90f, Z = 159.23f, Type = Rough },
                new Waypoint { X = -4291.01f, Y = 1329.67f, Z = 160.31f, Type = Rough },
                new Waypoint { X = -4276.96f, Y = 1328.63f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4253.82f, Y = 1327.84f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4239.06f, Y = 1328.16f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4225.97f, Y = 1327.36f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4214.98f, Y = 1325.82f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4210.50f, Y = 1317.22f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4205.22f, Y = 1308.26f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4198.57f, Y = 1298.64f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4197.99f, Y = 1288.17f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4198.08f, Y = 1278.26f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4193.18f, Y = 1266.10f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4188.34f, Y = 1257.03f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4180.84f, Y = 1242.17f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4177.05f, Y = 1228.76f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4172.39f, Y = 1218.29f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4168.67f, Y = 1209.35f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4166.49f, Y = 1200.04f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4165.11f, Y = 1189.50f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4165.15f, Y = 1178.75f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4164.05f, Y = 1167.82f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4161.77f, Y = 1154.68f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4153.60f, Y = 1150.18f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4143.67f, Y = 1145.48f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4134.17f, Y = 1137.51f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4123.99f, Y = 1133.37f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4113.87f, Y = 1131.59f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4101.63f, Y = 1128.96f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4088.33f, Y = 1127.83f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4077.65f, Y = 1126.57f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4065.79f, Y = 1123.40f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4059.16f, Y = 1116.05f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4051.89f, Y = 1107.35f, Z = 160.83f, Type = Rough },
                new Waypoint { X = -4045.78f, Y = 1099.24f, Z = 159.77f, Type = Rough },
                new Waypoint { X = -4037.88f, Y = 1091.95f, Z = 159.74f, Type = Rough },
                new Waypoint { X = -4028.74f, Y = 1084.86f, Z = 159.70f, Type = Rough },
                new Waypoint { X = -4020.15f, Y = 1076.93f, Z = 159.66f, Type = Rough },
                new Waypoint { X = -4012.42f, Y = 1073.41f, Z = 161.09f, Type = Rough },
                new Waypoint { X = -4003.06f, Y = 1070.49f, Z = 161.09f, Type = Rough },
                new Waypoint { X = -3994.21f, Y = 1070.33f, Z = 161.06f, Type = Rough },
                new Waypoint { X = -3984.07f, Y = 1071.95f, Z = 161.05f, Type = Rough },
                new Waypoint { X = -3983.06f, Y = 1079.92f, Z = 161.05f, Type = Rough },
                new Waypoint { X = -3982.54f, Y = 1087.47f, Z = 161.04f, Type = Rough },
                new Waypoint { X = -3982.01f, Y = 1101.64f, Z = 161.03f, Type = Rough },
                new Waypoint { X = -3977.35f, Y = 1111.05f, Z = 161.00f, Type = Rough },
                new Waypoint { X = -3964.95f, Y = 1117.06f, Z = 161.04f, Type = Rough },
                new Waypoint { X = -3947.91f, Y = 1122.72f, Z = 155.54f, Type = Rough },
                new Waypoint { X = -3927.95f, Y = 1124.90f, Z = 148.98f, Type = Rough },
                new Waypoint { X = -3914.15f, Y = 1126.86f, Z = 149.05f, Type = Rough },
                new Waypoint { X = -3896.46f, Y = 1125.84f, Z = 151.35f, Type = Rough },
                new Waypoint { X = -3877.80f, Y = 1124.38f, Z = 154.79f, Type = Rough },  //厄运内部中心点 枢纽位置
            };

            // 【厄运之槌枢纽到厄运东专线  部落联盟通用】
            List<Waypoint> routeToInstance_East = new List<Waypoint>
            {
                new Waypoint { X = -3867.57f, Y = 1120.01f, Z = 154.79f, Type = Rough },
                new Waypoint { X = -3860.22f, Y = 1113.27f, Z = 154.79f, Type = Rough },
                new Waypoint { X = -3861.59f, Y = 1102.37f, Z = 154.79f, Type = Rough },
                new Waypoint { X = -3861.64f, Y = 1088.08f, Z = 154.79f, Type = Rough },
                new Waypoint { X = -3861.34f, Y = 1075.22f, Z = 154.79f, Type = Rough },
                new Waypoint { X = -3861.90f, Y = 1061.56f, Z = 154.79f, Type = Rough },
                new Waypoint { X = -3855.58f, Y = 1048.35f, Z = 154.79f, Type = Rough },
                new Waypoint { X = -3849.68f, Y = 1037.84f, Z = 154.79f, Type = Rough },
                new Waypoint { X = -3838.87f, Y = 1032.55f, Z = 154.55f, Type = Rough },
                new Waypoint { X = -3831.73f, Y = 1027.12f, Z = 153.32f, Type = Rough },
                new Waypoint { X = -3826.43f, Y = 1018.45f, Z = 151.35f, Type = Rough },
                new Waypoint { X = -3819.26f, Y = 1005.00f, Z = 149.64f, Type = Rough },
                new Waypoint { X = -3812.38f, Y = 993.28f, Z = 149.64f, Type = Rough },
                new Waypoint { X = -3804.84f, Y = 982.00f, Z = 149.64f, Type = Rough },
                new Waypoint { X = -3799.83f, Y = 974.55f, Z = 152.87f, Type = Rough },
                new Waypoint { X = -3794.46f, Y = 965.11f, Z = 157.33f, Type = Rough },
                new Waypoint { X = -3790.95f, Y = 956.97f, Z = 161.03f, Type = Rough },
                new Waypoint { X = -3783.81f, Y = 944.72f, Z = 161.03f, Type = Rough },
                new Waypoint { X = -3773.80f, Y = 937.05f, Z = 161.03f, Type = Rough },
                new Waypoint { X = -3766.12f, Y = 934.68f, Z = 161.03f, Type = Rough },
                new Waypoint { X = -3756.91f, Y = 934.44f, Z = 161.02f, Type = Rough },
                new Waypoint { X = -3741.32f, Y = 934.54f, Z = 160.99f, Type = Rough },  // 门外（退出来的点）
                new Waypoint { X = -3732.34f, Y = 934.54f, Z = 161.00f, Type = Rough },  // 进本的世界坐标
            };

            // 【厄运之槌枢纽到厄运西专线  部落联盟通用】
            List<Waypoint> routeToInstance_West = new List<Waypoint>
            {
                new Waypoint { X = -3869.07f, Y = 1134.93f, Z = 154.79f, Type = Rough },
                new Waypoint { X = -3858.41f, Y = 1145.81f, Z = 154.79f, Type = Rough },
                new Waypoint { X = -3846.90f, Y = 1158.55f, Z = 154.30f, Type = Rough },
                new Waypoint { X = -3836.30f, Y = 1169.31f, Z = 151.81f, Type = Rough },
                new Waypoint { X = -3826.79f, Y = 1178.17f, Z = 149.77f, Type = Rough },
                new Waypoint { X = -3819.48f, Y = 1184.88f, Z = 149.64f, Type = Rough },
                new Waypoint { X = -3813.79f, Y = 1194.01f, Z = 149.64f, Type = Rough },
                new Waypoint { X = -3807.93f, Y = 1203.97f, Z = 149.64f, Type = Rough },
                new Waypoint { X = -3805.19f, Y = 1213.51f, Z = 153.64f, Type = Rough },
                new Waypoint { X = -3804.13f, Y = 1220.16f, Z = 156.56f, Type = Rough },
                new Waypoint { X = -3802.83f, Y = 1225.07f, Z = 158.73f, Type = Rough },
                new Waypoint { X = -3801.10f, Y = 1232.92f, Z = 160.27f, Type = Rough },
                new Waypoint { X = -3800.32f, Y = 1239.72f, Z = 160.27f, Type = Rough },
                new Waypoint { X = -3800.78f, Y = 1245.35f, Z = 160.27f, Type = Rough },
                new Waypoint { X = -3814.13f, Y = 1249.84f, Z = 160.27f, Type = Rough },   //门口
                new Waypoint { X = -3822.27f, Y = 1252.38f, Z = 160.27f, Type = Rough },   //火把位置
            };


            // 厄运进本总控：厄运副本门口到厄运枢纽和厄运东和厄运西路线  双阵营通用   正向！
            Node Enter_Dire_Maul_Branch = new Sequence(
                // 【第一段】：如果还在外围走廊，就老老实实跑到枢纽！
                // 注意这里用 Selector 包裹。如果条件不满足（比如已经到枢纽了），Selector 会默默跳过它。
                new Selector(
                    // 情况1：还在外面走廊，需要跑
                    new Sequence(
                        new ConditionNode(() => {
                            float x = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                            float y = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                            return x > -4400f && x < -3900f && y < 1350f;
                        }),
                        new ActionNode(() => {
                            Logger.Write($"[{CurrentPlayerName}] [智能寻路] 开始执行 去厄运枢纽路线");
                            return NodeState.Success;
                        }),
                        new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToInstance, false, false)
                    ),
                    // 情况2：已经过了走廊了（条件不满足），直接返回 Success 放行给下一步！
                    new ActionNode(() => { return NodeState.Success; })
                ),

                // 【第二段】：智能道岔！我在西门还是在枢纽中心？
                new Selector(
                    // 场景 A：我刚重置完副本，现在站在【厄运西大门】附近 (80码内)
                    new Sequence(
                        new ConditionNode(() => {
                            float myX = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                            float myY = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                            float distToWest = (float)Math.Sqrt(Math.Pow(myX - (-3821.49f), 2) + Math.Pow(myY - 1253.97f, 2));
                            return distToWest < 80.0f;
                        }),
                        new ActionNode(() => {
                            Logger.Write($"[{CurrentPlayerName}] [智能寻路] 侦测到角色在厄运西区域，正在规划路线...");
                            return NodeState.Success;
                        }),

                        // ==========================================
                        // 【核心修复：基于 X 轴直线的智能门禁跳过系统】
                        // ==========================================
                        new Selector(
                            // 情况 1：角色在副本门口到大门之间（X 坐标在 -3835 到 -3816.5 之间），说明被困在门内！
                            new Sequence(
                                new ConditionNode(() => {
                                    float myX = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);

                                    // 副本门口 X 约为 -3832，大门 X 约为 -3816.68
                                    // 设定一个区间结界：只要在这个区间内，判定为需要开门
                                    return myX >= -3835.0f && myX <= -3816.5f;
                                }),
                                new ActionNode(() => {
                                    Logger.Write($"[{CurrentPlayerName}] [门禁系统] 角色被困在门内，正在执行开门程序...");
                                    return NodeState.Success;
                                }),
                                new ActionNode(() =>
                                {
                                    if (MoveTo(_hProcess, _playerBase, -3821.50f, 1253.41f, 160.27f, 0.5f))    // 移动到开门火把位置
                                    {
                                        StopMovement(_hProcess, _playerBase);
                                        return NodeState.Success;
                                    }
                                    return NodeState.Running;
                                }),
                                new ActionNode(() =>
                                {
                                    uint torchBase = GetGameObjectBaseByEntry(_hProcess, 179507);  // 厄运西大门火把开门拉杆ID
                                    if (torchBase != 0)
                                    {
                                        RightClickGameObject(torchBase);   // 右键GameObject
                                        Logger.Write($"[{CurrentPlayerName}] [门禁系统] 成功向火把开关 (0x{torchBase:X}) 发送了 1 次右键 Call！");
                                        return NodeState.Success;
                                    }
                                    Logger.Write($"[{CurrentPlayerName}] [门禁系统] 没找到火把，请确认你在开关 10 码以内");
                                    return NodeState.Running; // 没找到必须卡住，不然会撞门！
                                }),
                                new WaitNode(1000), // 给门打开留点时间
                                new ActionNode(() =>
                                {
                                    // 目标出门外点 (-3814.13)，此时 X 已经大于 -3816.5，完美脱离“牢笼区间”！
                                    if (MoveTo(_hProcess, _playerBase, -3814.13f, 1249.84f, 160.27f, 0.5f))
                                    {
                                        StopMovement(_hProcess, _playerBase);
                                        return NodeState.Success;
                                    }
                                    return NodeState.Running;
                                }),
                                new WaitNode(100)
                            ),

                            // 情况 2：兜底放行！如果 X 已经 > -3816.5（比如是半路断点启动，或者已经在门外），直接秒过本节点！
                            new ActionNode(() => {
                                Logger.Write($"[{CurrentPlayerName}] [门禁系统] 角色已在门外大马路上，跳过开门操作，直接进行断点寻路！");
                                return NodeState.Success;
                            })
                        ),
                        // ==========================================
                        // 无论是开完门出来的，还是在半路放行的，统一汇聚到这里！
                        // ==========================================

                        // ==========================================
                        // 【核心优化：基于乘骑与战斗状态的智能准备机制】
                        // ==========================================
                        new Selector(
                            // 分支 1：短路放行条件
                            new Sequence(
                                new ConditionNode(() =>
                                {
                                    bool isMounted = IsMounted(_hProcess, _playerBase);

                                    // 假设你底层有判断是否在战斗中的函数，类似 IsInCombat
                                    // 1.12.1 通常读取 UnitFlags 0x00080000 这一位来判断战斗状态
                                    bool inCombat = Unit_Behavioral_State(_hProcess, _playerBase, 19);  //19 代表战斗状态位

                                    // 只要已经在马上，【或者】被打下马进入了战斗，都直接跳过准备阶段！
                                    return isMounted || inCombat;
                                }),
                                new ActionNode(() =>
                                {
                                    // 在马上，或者在战斗中跑路，都什么都不做，直接放行给寻路节点！
                                    return NodeState.Success;
                                })
                            ),

                            // 分支 2：兜底准备逻辑
                            // 只有当：不在马上 且 不在战斗中（比如刚买完东西出发，或者半路脱战了）
                            // 才会执行战前准备（套盾、上马）
                            new PreDungeonShieldNode(this, () => _hProcess, () => _playerBase)
                        ),

                        //new PreDungeonShieldNode(this, () => _hProcess, () => _playerBase),   //上盾+上马函数
                        new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToInstance_West, false, true),
                        new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToInstance_East, false, false)
                    ),

                    // 场景 B：兜底路线。只要不在西门，就一律判定为“正在前往厄运东的路上”！
                    new Sequence(
                        new ActionNode(() => {
                            Logger.Write($"[{CurrentPlayerName}] [智能寻路] 正常从枢纽进入或断点重启，前往厄运东...");
                            return NodeState.Success;
                        }),

                        // 跑正门路线：枢纽 -> 厄运东
                        // 放心，SmartPathNode 自带“断点雷达”，它会自动寻找离你当前 (X=-3832, Y=1015) 最近的航点并继续跑！
                        new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToInstance_East, false, false)
                    )
                ),

                // ==========================================
                // 以下为【防爆本专属死磕循环】
                // 只要代码能走到这里，100% 确诊为进本失败（爆本）！
                // ==========================================
                new ActionNode(() => {
                    Logger.Write($"[{CurrentPlayerName}] [防爆本系统] 进本 10 秒仍然在门口，确诊为爆本！重新尝试...");
                    return NodeState.Success;
                }),

                // 4. 倒车到门外航点 57 的安全距离 (-3741.32)
                new ActionNode(() => {
                    if (MoveTo(_hProcess, _playerBase, -3741.32f, 934.54f, 160.99f))
                    {
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                // 5. 给服务器留出重置副本计数器的判定时间
                new WaitNode(100),

                // 6. 再次推土机冲刺！
                new ActionNode(() => {
                    Logger.Write($"[{CurrentPlayerName}] [防爆本系统] 已经退到本外，重新尝试进本...");
                    MoveToOneShot(_playerBase, -3732.33f, 934.51f, 161.73f);
                    return NodeState.Success;
                }),

                // 7. 等 10 秒防踢。如果依然没进，这一轮执行完毕，下一帧重新循环这段逻辑
                new WaitNode(10000)
            );


            // 厄运出本总控：枢纽/东门/西门 到 厄运外部野外大门 (双阵营通用)  反向
            Node Export_Dire_Maul_Branch = new Sequence(

                // 0. 前提条件：必须还在厄运内部区域
                new ConditionNode(() => {
                    // 🌟 核心修复 1：只要人在船上，那是相对坐标，绝对不可能在副本走廊！直接返回 false！
                    if (IsOnTransport(_hProcess, _playerBase)) return false;

                    float x = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                    float y = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);

                    // 从 -4370f 调整为 -4360f
                    return x > -4360f && y < 1350f;
                }),

                // ==========================================
                // 阶段一：支线收口 (如果在东门或西门走廊，先强制退回枢纽！)
                // ==========================================
                new Selector(
                    // 场景 A：人在【厄运西】支线走廊 (特征：X > -3970 且 Y > 1130)
                    new Sequence(
                        new ConditionNode(() => {
                            float myX = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                            float myY = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                            // 宏观区分走廊：保留，完美覆盖整条西大走廊
                            return myX > -3970f && myY > 1130f;
                        }),
                        new ActionNode(() => {
                            Logger.Write($"[{CurrentPlayerName}] [智能寻路] 位于厄运西走廊，准备退回枢纽...");
                            return NodeState.Success;
                        }),

                        // [智能门禁系统]：替换圆形测距，改用 X轴绝对结界！
                        new Selector(
                            new Sequence(
                                new ConditionNode(() => {
                                    float myX = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                                    // 🌟 核心优化：如果在牢笼区间（出本瞬间刚好落在这个盒子里），强制执行开门上马
                                    return myX >= -3835.0f && myX <= -3816.5f;
                                }),
                                new ActionNode(() => {
                                    if (MoveTo(_hProcess, _playerBase, -3821.50f, 1253.41f, 160.27f, 0.5f))
                                    {
                                        StopMovement(_hProcess, _playerBase);
                                        return NodeState.Success;
                                    }
                                    return NodeState.Running;
                                }),
                                new ActionNode(() =>
                                {
                                    uint torchBase = GetGameObjectBaseByEntry(_hProcess, 179507);  // 厄运西大门火把开门拉杆ID
                                    if (torchBase != 0)
                                    {
                                        RightClickGameObject(torchBase);
                                        Logger.Write($"[{CurrentPlayerName}] [门禁系统] 成功向火把开关 (0x{torchBase:X}) 发送了 1 次右键 Call！");
                                        return NodeState.Success;
                                    }
                                    Logger.Write($"[{CurrentPlayerName}] [门禁系统] 没找到火把，请确认你在开关 10 码以内");
                                    return NodeState.Running; // 没找到必须卡住，不然会撞门！
                                }),
                                new WaitNode(1000), // 给门打开留点时间
                                new ActionNode(() => {
                                    if (MoveTo(_hProcess, _playerBase, -3814.13f, 1249.84f, 160.27f, 0.5f))
                                    {
                                        StopMovement(_hProcess, _playerBase);
                                        return NodeState.Success;
                                    }
                                    return NodeState.Running;
                                }),
                                new WaitNode(100),
                                new ActionNode(() =>
                                {
                                    ExecuteDynamicLua("CastSpellByName('冰甲术');");
                                    return NodeState.Success;
                                }), // 补冰甲
                                new WaitNode(1600),
                                new PreDungeonShieldNode(this, () => _hProcess, () => _playerBase) // 套盾上马
                            ),
                            // 超过 -3816.5 码（已经在门外），或者半路启动的，直接放行，不傻站着上马！
                            new ActionNode(() => NodeState.Success)
                        ),

                        // 启动西线退回路线 -> 直达枢纽中心！
                        new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToInstance_West, false, true)
                    ),

                    // 场景 B：人在【厄运东】支线走廊 (特征：X > -3970 且 Y <= 1130)
                    new Sequence(
                        new ConditionNode(() => {
                            float myX = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                            float myY = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                            return myX > -3970f && myY <= 1130f; // 宏观覆盖整条东大走廊
                        }),
                        new ActionNode(() => {
                            Logger.Write($"[{CurrentPlayerName}] [智能寻路] 位于厄运东走廊，正退回枢纽...");
                            return NodeState.Success;
                        }),

                        // [起步防护]：替换圆形测距，改用 X轴绝对结界！
                        new Selector(
                            new Sequence(
                                new ConditionNode(() => {
                                    float myX = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                                    // 🌟 核心优化：只要人在东门刚出来的这条短走廊里，强制停稳上盾上马
                                    return myX >= -3768.0f && myX <= -3730.0f;
                                }),
                                new ActionNode(() =>
                                {
                                    StopMovement(_hProcess, _playerBase);
                                    return NodeState.Success;
                                }),
                                new WaitNode(100),
                                new ActionNode(() =>
                                {
                                    ExecuteDynamicLua("CastSpellByName('冰甲术');");
                                    return NodeState.Success;
                                }),
                                new WaitNode(1600),
                                new PreDungeonShieldNode(this, () => _hProcess, () => _playerBase)
                            ),
                            // 已经跑出了这个起始框（X < -3768），直接放行
                            new ActionNode(() => NodeState.Success)
                        ),

                        new ActionNode(() => {
                            Logger.Write($"[{CurrentPlayerName}] [智能寻路] 厄运主干道：正从厄运东向厄运枢纽跑去...");
                            return NodeState.Success;
                        }),
                        // 启动东线退回路线 -> 直达枢纽中心！
                        new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToInstance_East, false, true)
                    ),

                    // 场景 C：兜底放行！
                    // 如果上面两个条件都不满足（说明 X <= -3970），证明法师一定已经在【枢纽中心】或【主干道】上了！
                    // 直接返回 Success，把接力棒完美交接给阶段二！
                    new ActionNode(() => NodeState.Success)
                ),

                // ==========================================
                // 阶段二：干道集体冲刺 (从枢纽一路跑出野外大门)
                // ==========================================
                new ActionNode(() => {
                    Logger.Write($"[{CurrentPlayerName}] [智能寻路] 厄运主干道：正从枢纽向野外大门跑去...");
                    return NodeState.Success;
                }),

                // 启动 routeToInstance 反转路线 (终点在外围营地大门)
                new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToInstance, false, true)
            );


            // ========================================================
            //           【跑尸专用】：全境贯通超级国道 (分化版)
            // ========================================================

            // 1. 联盟基础主干道 (码头 -> 枢纽)  如果是旅店和船上被部落击杀，程序将无法自动复活
            // 处理船上复活逻辑很麻烦，暂时不写！
            List<Waypoint> route_MasterCorpseRoad_Alliance = new List<Waypoint>();
            route_MasterCorpseRoad_Alliance.AddRange(routeToInstanceDoor_Alliance);
            route_MasterCorpseRoad_Alliance.AddRange(routeToInstance);

            // 2. 部落基础主干道 (旅店 -> 枢纽)
            List<Waypoint> route_MasterCorpseRoad_Horde = new List<Waypoint>();
            route_MasterCorpseRoad_Horde.AddRange(routeToHome_Horde);
            route_MasterCorpseRoad_Horde.AddRange(routeToInstanceDoor_Horde);
            route_MasterCorpseRoad_Horde.AddRange(routeToInstance);

            // --------------------------------------------------------
            // 分组出 4 条终极国道 跑尸专用
            // --------------------------------------------------------

            // 【联盟 - 厄运东专线】
            List<Waypoint> route_MasterCorpseRoad_Alliance_East = new List<Waypoint>(route_MasterCorpseRoad_Alliance);
            route_MasterCorpseRoad_Alliance_East.AddRange(routeToInstance_East);

            // 【联盟 - 厄运西专线】
            List<Waypoint> route_MasterCorpseRoad_Alliance_West = new List<Waypoint>(route_MasterCorpseRoad_Alliance);
            route_MasterCorpseRoad_Alliance_West.AddRange(routeToInstance_West);

            // 【部落 - 厄运东专线】
            List<Waypoint> route_MasterCorpseRoad_Horde_East = new List<Waypoint>(route_MasterCorpseRoad_Horde);
            route_MasterCorpseRoad_Horde_East.AddRange(routeToInstance_East);

            // 【部落 - 厄运西专线】
            List<Waypoint> route_MasterCorpseRoad_Horde_West = new List<Waypoint>(route_MasterCorpseRoad_Horde);
            route_MasterCorpseRoad_Horde_West.AddRange(routeToInstance_West);


            // ========================================================
            // 部落去副本上班状态机（正向行驶）
            // ========================================================
            _runToInstanceTree_Horde = new Selector(

                // 1、在旅店内部或门口上马点 (X < -4430 且 Y < 260) -> 目标：出门，上马，跨过接驳线
                new Sequence(
                    new ConditionNode(() => {
                        float x = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                        float y = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                        return x < -4430f && y < 260f;
                    }),
                    new ActionNode(() => {
                        Logger.Write($"[{CurrentPlayerName}] [智能寻路] 开始执行 部落从旅店前往门口上马点");
                        // 叫队长重置副本
                        //_needResetInstance = true;
                        // 离开队伍 直接重置副本了
                        Logger.Write($"[{CurrentPlayerName}] [智能检测] 开始执行 离开队伍函数，如果没有正确离队，请检查相关代码。");
                        ExecuteDynamicLua("LeaveParty();");
                        return NodeState.Success;
                    }),

                    new WaitNode(100),

                    // 启动routeToHome_Horde正向路线
                    new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToHome_Horde, false, false),

                    // 走向上马点 (-4432.91 还在框内)
                    new ActionNode(() => {
                        if (MoveTo(_hProcess, _playerBase, -4432.91f, 258.55f, 37.97f, 0.5f))
                        {
                            StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new WaitNode(100),
                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("CastSpellByName('冰甲术');");
                        return NodeState.Success;
                    }), // 补冰甲
                    new WaitNode(500),
                    new ActionNode(() =>
                    {
                        _ = Use_Mount(50);
                        return NodeState.Success;
                    }),   // 上马
                    new WaitNode(3500),

                    //前往邮箱位置
                    // 启动 routeToMail_Horde 正向路线
                    new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToMail_Horde, false, false),

                    //开始邮寄操作
                    new WaitNode(100),

                    new ActionNode(() =>
                    {
                        // 清理一下可能的 UI 残留
                        ExecuteDynamicLua("CloseMail(); ClearCursor();");
                        return NodeState.Success;
                    }),
                    new WaitNode(100),

                    new ActionNode(() =>
                    {
                        // ==========================================
                        // 阶段 0：寻找并打开邮箱 (替代原来的 WaitNode)
                        // ==========================================
                        if (!_hasClickedMailbox && !isMailingInitialized)
                        {
                            string buildVersion = MemoryAPI.ReadString(_hProcess, ModuleBaseAddress + 0x437BFC, 16).Trim();
                            int targetMailboxId = 143986; // 部落 莫沙砌营地 邮箱ID  -- VMaNGOS 1.12.1 5875原版

                            if (buildVersion == "7272")
                            {
                                targetMailboxId = 173221; // 部落 莫沙砌营地 邮箱ID  -- 乌龟服 1.18.1 7272
                            }

                            uint mailboxBase = GetGameObjectBaseByEntry(_hProcess, targetMailboxId);

                            if (mailboxBase == 0)
                            {
                                Logger.Write($"[{CurrentPlayerName}] [邮箱系统] 未检测到邮箱，跳过邮寄流程，直接放行！");

                                // 【核心改动】：找不到邮箱，直接假装自己成功了，让行为树去执行旅店的下一步代码！
                                return NodeState.Success;
                            }

                            // 找到了邮箱，发送交互
                            RightClickGameObject(mailboxBase);
                            Logger.Write($"[{CurrentPlayerName}] [邮箱系统] 已经向邮箱 0x{mailboxBase:X} 发送了一次右键CALL，等待打开邮箱");

                            _hasClickedMailbox = true;
                            nextMailStepTime = DateTime.Now.AddMilliseconds(2000); // 完美替代原来的 WaitNode(2000)
                            return NodeState.Running; // 挂起当前节点，等待 2 秒
                        }

                        // ==========================================
                        // 全局节流阀：控制整个状态机的时间节奏
                        // ==========================================
                        if (DateTime.Now < nextMailStepTime) return NodeState.Running;

                        // ==========================================
                        // 阶段 1：初始化配置与扫描背包 (延迟2秒后开始执行)
                        // ==========================================
                        if (!isMailingInitialized)
                        {
                            int playerDesc = MemoryAPI.ReadInteger(_hProcess, _playerBase + 0x08);
                            int totalCoins = MemoryAPI.ReadInteger(_hProcess, playerDesc + 0x1260);

                            if (totalCoins < 10000)
                            {
                                Logger.Write($"[{CurrentPlayerName}] [邮寄系统] 余额不足 1 金币 (当前 {totalCoins} 铜)，跳过本次邮寄以防邮资破产！");
                                ExecuteDynamicLua("CloseMail(); ClearCursor();");
                                _hasClickedMailbox = false;
                                isMailingInitialized = false;
                                return NodeState.Success; // 退出并放行
                            }

                            AccountConfig currentConfig = ConfigManager.GetConfig(this.AccountName) ?? new AccountConfig();
                            string mailTarget = currentConfig.Mailing_name;

                            if (string.IsNullOrEmpty(mailTarget))
                            {
                                Logger.Write($"[{CurrentPlayerName}] [邮寄系统] 未配置收件人名字 (Mailing_name为空)，取消邮寄任务！");
                                ExecuteDynamicLua("CloseMail(); ClearCursor();");
                                _hasClickedMailbox = false;
                                isMailingInitialized = false;
                                return NodeState.Success; // 退出并放行
                            }

                            HashSet<int> mailWhitelistIds = ItemDbManager.ParseNamesToIds(currentConfig.Mailing_whitelist);
                            List<InventoryItem> allItems = GetAllInventoryItems(_hProcess);

                            itemsToMail = allItems.FindAll(item =>
                            {
                                if (item.IsSoulbound) return false;

                                bool shouldMail = false;
                                if (mailWhitelistIds.Contains(item.ItemId)) shouldMail = true;

                                if (ItemDbManager.IdMap.TryGetValue(item.ItemId, out var template))
                                {
                                    int quality = template.Quality_ID;
                                    if (quality == 3 && currentConfig.Mailing_exquisite) shouldMail = true;
                                    if (quality == 4 && currentConfig.Mailing_epic) shouldMail = true;
                                }
                                return shouldMail;
                            });

                            needToMailGold = false;
                            copperToMail = 0;
                            if (currentConfig.Mailing_Gold)
                            {
                                int reserveCopper = 50000;
                                int estimatedPostage = (itemsToMail.Count * 30) + 30;

                                if (totalCoins > (reserveCopper + estimatedPostage))
                                {
                                    needToMailGold = true;
                                    copperToMail = totalCoins - reserveCopper - estimatedPostage;
                                }
                            }

                            Logger.Write($"[{CurrentPlayerName}] [邮寄系统] 扫描完毕。待寄物品: {itemsToMail.Count} 件 | 待寄金币: {copperToMail / 10000}金");

                            ExecuteDynamicLua("MailFrameTab_OnClick(2); ClearCursor();");

                            mailIndex = 0;
                            mailSubState = 0;
                            isMailingInitialized = true;
                            nextMailStepTime = DateTime.Now.AddMilliseconds(500);

                            if (itemsToMail.Count == 0 && !needToMailGold)
                            {
                                ExecuteDynamicLua("CloseMail();");
                                Logger.Write($"[{CurrentPlayerName}] [邮寄系统] 没有符合条件的物品和金币需要邮寄。");
                                _hasClickedMailbox = false;
                                isMailingInitialized = false;
                                return NodeState.Success; // 退出并放行
                            }

                            return NodeState.Running;
                        }

                        AccountConfig cfg = ConfigManager.GetConfig(this.AccountName) ?? new AccountConfig();
                        string receiver = cfg.Mailing_name;

                        // ==========================================
                        // 阶段 2：逐个邮寄物品 (三步状态机，防丢弃)
                        // ==========================================
                        if (mailIndex < itemsToMail.Count)
                        {
                            var currentItem = itemsToMail[mailIndex];

                            if (mailSubState == 0)
                            {
                                // 【新增】：解析物品名字并输出详细日志
                                string mailName = "未知";
                                if (ItemDbManager.IdMap.TryGetValue(currentItem.ItemId, out var tpl))
                                {
                                    mailName = !string.IsNullOrEmpty(tpl.Chinese_Name) ? tpl.Chinese_Name : tpl.English_Name;
                                }

                                Logger.Write($"[{CurrentPlayerName}] [邮寄系统] 正在邮寄: {mailName} (ID:{currentItem.ItemId}, Bag:{currentItem.BagId}, Slot:{currentItem.SlotId})");
                                ExecuteDynamicLua($"ClearCursor(); PickupContainerItem({currentItem.BagId}, {currentItem.SlotId});");
                                mailSubState = 1;
                                nextMailStepTime = DateTime.Now.AddMilliseconds(150);
                                return NodeState.Running;
                            }
                            else if (mailSubState == 1)
                            {
                                ExecuteDynamicLua("ClickSendMailItemButton();");
                                mailSubState = 2;
                                nextMailStepTime = DateTime.Now.AddMilliseconds(800);
                                return NodeState.Running;
                            }
                            else if (mailSubState == 2)
                            {
                                string safeMailLua = $@"
                SendMailNameEditBox:SetText('{receiver}');
                SendMailSubjectEditBox:SetText('标题');
                SendMailBodyEditBox:SetText('内容');
                SendMailMailButton_OnClick(); 
            ";
                                ExecuteDynamicLua(safeMailLua);

                                mailSubState = 0;
                                mailIndex++;
                                nextMailStepTime = DateTime.Now.AddMilliseconds(1200);
                                return NodeState.Running;
                            }
                        }

                        // ==========================================
                        // 阶段 3：最后发送兜底金币
                        // ==========================================
                        if (needToMailGold)
                        {
                            string safeGoldMailLua = $@"
            ClearCursor();
            SendMailNameEditBox:SetText('{receiver}');
            SendMailSubjectEditBox:SetText('标题');
            SendMailBodyEditBox:SetText('内容');
            SetSendMailMoney({copperToMail});
            SendMailMailButton_OnClick(); 
        ";
                            ExecuteDynamicLua(safeGoldMailLua);

                            Logger.Write($"[{CurrentPlayerName}] [邮寄系统] 成功寄出金币：{copperToMail / 10000} 金！");
                            needToMailGold = false;
                            nextMailStepTime = DateTime.Now.AddMilliseconds(1200);
                            return NodeState.Running;
                        }

                        // ==========================================
                        // 最终阶段：结束收尾
                        // ==========================================
                        ExecuteDynamicLua("CloseMail(); ClearCursor();");
                        Logger.Write($"[{CurrentPlayerName}] [邮寄系统] 全部邮寄任务圆满完成，关闭邮箱！");

                        // 【核心收尾】：彻底复位所有开关变量，为下一次回城清空状态，并让行为树继续往下走
                        _hasClickedMailbox = false;
                        isMailingInitialized = false;
                        return NodeState.Success;
                    })
                    //邮寄已经完成，回到正轨路线
                ),

                // 2、大马路狂奔阶段 (X < -4350) -> 目标：跑到厄运废墟大门
                new Sequence(
                    new ConditionNode(() => {
                        float x = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                        float y = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                        bool inInn = x < -4430f && y < 260f;

                        // 定义最终的下马套盾高台为“接驳区”
                        bool isAtStagingArea = (x > -4400f && y < 1350f);

                        // 只要还没踏入高台，且没过旧大门，才属于大马路狂奔
                        return !inInn && x < -4350f && !isAtStagingArea;
                    }),
                    new ActionNode(() => {
                        Logger.Write($"[{CurrentPlayerName}] [智能寻路] 开始执行 部落从营地大门口去副本门口路线...");
                        return NodeState.Success;
                    }),

                    // 启动 routeToInstanceDoor_Horde 正向路线
                    new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToInstanceDoor_Horde, false, false),

                    new ActionNode(() => {
                        StopMovement(_hProcess, _playerBase);
                        return NodeState.Success;
                    }),
                    // ==========================================
                    // 战前准备判定区：战斗中直接跳过，安全时才套盾上马
                    // ==========================================
                    new Selector(
                        // 【分支 1】：条件拦截（如果在战斗中）
                        new Sequence(
                            // 检测战斗状态 (19号位)
                            new ConditionNode(() => Unit_Behavioral_State(_hProcess, _playerBase, 19)),

                            // 如果条件成立，打个日志，然后返回 Success！
                            new ActionNode(() => {
                                Logger.Write($"[{CurrentPlayerName}] [战前准备] 侦测到正在战斗中，跳过套盾上马流程，直接肉身冲锋！");
                                return NodeState.Success;
                            })
                        ),

                        // 【分支 2】：如果上面条件不成立（不在战斗中），Selector 就会跌落到这里来执行！
                        // 正常执行上马套双盾函数
                        new PreDungeonShieldNode(this, () => _hProcess, () => _playerBase)
                    )

                ),
                Enter_Dire_Maul_Branch

            );

            // ========================================================
            // 部落回城清包状态机（原路返回）
            // ========================================================
            _runToInnTree_Horde = new Selector(

                Export_Dire_Maul_Branch,

                // 2、大马路狂奔回城 (X < -4350) -> 目标：跑回莫沙彻营地大门
                new Sequence(
                    new ConditionNode(() => {
                        float x = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                        float y = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                        bool inInn = x < -4430f && y < 260f;

                        // 一旦 X <= -4360f 且没进旅店，立刻由本节点无缝接管！
                        return x <= -4360f && !inInn;
                    }),
                    new ActionNode(() => {
                        Logger.Write($"[{CurrentPlayerName}] [智能寻路] 开始执行 部落从副本门口回营地路线...");
                        return NodeState.Success;
                    }),

                    // 启动 routeToInstanceDoor_Horde 反转路线
                    new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToInstanceDoor_Horde, false, true),

                    // 跑到营地上马点了，停车准备进屋
                    new ActionNode(() => { StopMovement(_hProcess, _playerBase); return NodeState.Success; }),

                    // 冲进门槛，强行踏入 X < -4430 && Y < 260 的结界！
                    new ActionNode(() => {
                        if (MoveTo(_hProcess, _playerBase, -4442.87f, 251.40f, 39.11f, 1.0f)) { StopMovement(_hProcess, _playerBase); return NodeState.Success; }
                        return NodeState.Running;
                    })
                ),

                // 3、进入旅店内部 (X < -4430 且 Y < 260) -> 目标：走到 NPC 脸上卖破烂
                new Sequence(
                    new ConditionNode(() => {
                        float x = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                        float y = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                        return x < -4430f && y < 260f;
                    }),
                    new ActionNode(() => {
                        Logger.Write($"[{CurrentPlayerName}] [智能寻路] 开始执行 部落旅店门口去售卖NPC路线...");
                        return NodeState.Success;
                    }),

                    // 启动 routeToHome_Horde 反转路线
                    new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToHome_Horde, false, true),

                    // 到达商人面前，踩死刹车
                    new ActionNode(() => { StopMovement(_hProcess, _playerBase); return NodeState.Success; }),
                    new WaitNode(500),
                    new DestroyGarbageNode(this, () => _hProcess, () => _playerBase),   //自动摧毁名单内的物品
                    new WaitNode(500),

                    new ActionNode(() =>
                    {
                        uint targetBase = GetUnitBaseByEntry(_hProcess, 9548);  // 卡温德   
                        if (targetBase != 0)
                        {
                            RightClickUnit(targetBase);
                            Logger.Write($"[{CurrentPlayerName}] [商人系统] 已经向商人（卡温德） 0x{targetBase:X} 发送了一次右键CALL，准备清理背包！");
                        }
                        return NodeState.Success;
                    }),
                    new WaitNode(2000),

                    // 售卖垃圾节点 

                    new ActionNode(() =>
                    {
                        if (!isSellingInitialized)
                        {
                            // 🌟 核心修改：用基类保存的 AccountName 去 JSON 里精准拉取配置！
                            AccountConfig currentConfig = ConfigManager.GetConfig(this.AccountName) ?? new AccountConfig();

                            // 3. 将白名单字符串转换为 ID 集合
                            HashSet<int> whitelistIds = ItemDbManager.ParseNamesToIds(currentConfig.Whitelist);

                            List<InventoryItem> allItems = GetAllInventoryItems(_hProcess);

                            // 4. 核心过滤逻辑
                            itemsToSell = allItems.FindAll(item =>
                            {
                                // 【规则 A】：灵魂绑定的绝对不卖
                                if (item.IsSoulbound) return false;

                                // 【规则 A2】：系统内置底层白名单，绝对不卖
                                if (BuiltInWhitelist.Contains(item.ItemId)) return false;

                                // 【规则 B】：在用户配置的白名单里的物品，不卖
                                if (whitelistIds.Contains(item.ItemId)) return false;

                                // 【规则 C】：判断物品品质是否需要保留
                                if (ItemDbManager.IdMap.TryGetValue(item.ItemId, out var template))
                                {
                                    int quality = template.Quality_ID;

                                    if (quality == 3 && currentConfig.KeepExquisite) return false; // 蓝装
                                    if (quality == 4 && currentConfig.KeepEpic) return false;      // 紫装

                                    // 传说(5)及神器(6)，作为最后兜底，绝对不卖
                                    if (quality >= 5) return false;
                                }

                                // 都不是，那就是垃圾，卖！
                                return true;
                            });

                            Logger.Write($"[{CurrentPlayerName}] [背包系统] 根据账号 [{AccountName}] 的配置，扫描到背包 总物品: {allItems.Count} | 待售卖: {itemsToSell.Count}");

                            sellIndex = 0;
                            isSellingInitialized = true;
                            nextSellTime = DateTime.Now;

                            // 没垃圾可卖的情况
                            if (itemsToSell.Count == 0)
                            {
                                // 第一步：发送修理指令，并挂起 500ms
                                if (!_isWaitingToCloseMerchant)
                                {
                                    ExecuteDynamicLua("RepairAllItems()");
                                    nextSellTime = DateTime.Now.AddMilliseconds(500);
                                    _isWaitingToCloseMerchant = true;
                                    return NodeState.Running; // 必须 return Running，让行为树等这 500ms
                                }

                                // 等待期间：卡住不放行
                                if (DateTime.Now < nextSellTime) return NodeState.Running;

                                // 第二步：时间到了，关商店，清理状态，大功告成
                                ExecuteDynamicLua("CloseMerchant()");
                                Logger.Write($"[{CurrentPlayerName}] [背包系统] 背包很干净，装备已修满！");

                                isSellingInitialized = false;
                                _isWaitingToCloseMerchant = false;
                                _hasAttemptedSell = true;
                                _needGoHome = false;

                                // 注意：这里不要写 _attemptingTurtleBot = false; 留给邮寄去关！
                                return NodeState.Success;
                            }
                        }

                        // 垃圾全部卖完的情况
                        if (sellIndex >= itemsToSell.Count)
                        {
                            // 第一步：发送修理指令，并挂起 500ms
                            if (!_isWaitingToCloseMerchant)
                            {
                                ExecuteDynamicLua("RepairAllItems()");
                                nextSellTime = DateTime.Now.AddMilliseconds(500);
                                _isWaitingToCloseMerchant = true;
                                return NodeState.Running; // 必须 return Running，让行为树等这 500ms
                            }

                            // 等待期间：卡住不放行
                            if (DateTime.Now < nextSellTime) return NodeState.Running;

                            // 第二步：时间到了，关商店，清理状态，大功告成
                            ExecuteDynamicLua("CloseMerchant()");
                            Logger.Write($"[{CurrentPlayerName}] [背包系统] 垃圾清理完毕，装备已修满！");

                            isSellingInitialized = false;
                            _isWaitingToCloseMerchant = false;
                            _hasAttemptedSell = true;
                            _needGoHome = false;

                            // 注意：这里也不要写 _attemptingTurtleBot = false; 留给邮寄去关！
                            return NodeState.Success;
                        }

                        // 控制售卖频率，防止掉线 (300ms 卖一件非常安全)
                        if (DateTime.Now < nextSellTime) return NodeState.Running;

                        var currentItem = itemsToSell[sellIndex];
                        string sellName = "未知";
                        if (ItemDbManager.IdMap.TryGetValue(currentItem.ItemId, out var tpl))
                        {
                            sellName = !string.IsNullOrEmpty(tpl.Chinese_Name) ? tpl.Chinese_Name : tpl.English_Name;
                        }

                        Logger.Write($"[{CurrentPlayerName}] [清包系统] 正在售卖: {sellName} (ID:{currentItem.ItemId}, Bag:{currentItem.BagId}, Slot:{currentItem.SlotId})");
                        //RightClickBagItem(currentItem.BagId, currentItem.SlotId);
                        ExecuteDynamicLua($"UseContainerItem({currentItem.BagId}, {currentItem.SlotId})");
                        sellIndex++;
                        nextSellTime = DateTime.Now.AddMilliseconds(300);
                        return NodeState.Running;
                    })
                )
            );


            // ========================================================
            // 联盟去副本上班状态机（正向行驶）
            // ========================================================
            _runToInstanceTree_Alliance = new Selector(

                /* 羽月要塞跑向副本入口 */

                // 1、在旅店里 (Y >= 3260f) -> 目标：出门，上马，下海
                new Sequence(
                    new ConditionNode(() => {
                        // 这段逻辑包含了乘船，所以只要人在船上，必须强制放行让本节点继续执行等船/下船操作！
                        if (IsOnTransport(_hProcess, _playerBase)) return true;

                        return MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC) >= 3260f;
                    }),

                    new ActionNode(() => {
                        Logger.Write($"[{CurrentPlayerName}] [智能寻路] 开始执行 联盟从旅店前往码头路线");
                        // 叫队长重置副本
                        //_needResetInstance = true;
                        // 离开队伍 直接重置副本了
                        Logger.Write($"[{CurrentPlayerName}] [智能检测] 开始执行 离开队伍函数，如果没有正确离队，请检查相关代码。");
                        ExecuteDynamicLua("LeaveParty();");
                        return NodeState.Success;
                    }),

                    new WaitNode(100),

                    //先去邮箱位置邮寄一下
                    //启动routeToMail_Alliance正向路线
                    new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToMail_Alliance, false, false), //走到邮箱位置

                    new WaitNode(100),

                    new ActionNode(() =>
                    {
                        // 清理一下可能的 UI 残留
                        ExecuteDynamicLua("CloseMail(); ClearCursor();");
                        return NodeState.Success;
                    }),
                    new WaitNode(100),

                    new ActionNode(() =>
                    {
                        // ==========================================
                        // 阶段 0：寻找并打开邮箱 (替代原来的 WaitNode)
                        // ==========================================
                        if (!_hasClickedMailbox && !isMailingInitialized)
                        {
                            string buildVersion = MemoryAPI.ReadString(_hProcess, ModuleBaseAddress + 0x437BFC, 16).Trim();
                            int targetMailboxId = 142119; // 联盟 羽月要塞 邮箱ID  -- VMaNGOS 1.12.1 5875原版

                            if (buildVersion == "7272")
                            {
                                targetMailboxId = 142109; // 联盟 羽月要塞 邮箱ID  -- 乌龟服 1.18.1 7272
                            }

                            uint mailboxBase = GetGameObjectBaseByEntry(_hProcess, targetMailboxId);

                            if (mailboxBase == 0)
                            {
                                Logger.Write($"[{CurrentPlayerName}] [邮箱系统] 未检测到邮箱，跳过邮寄流程，直接放行！");

                                // 【核心改动】：找不到邮箱，直接假装自己成功了，让行为树去执行旅店的下一步代码！
                                return NodeState.Success;
                            }

                            // 找到了邮箱，发送交互
                            RightClickGameObject(mailboxBase);
                            Logger.Write($"[{CurrentPlayerName}] [邮箱系统] 已经向邮箱 0x{mailboxBase:X} 发送了一次右键CALL，等待打开邮箱！");

                            _hasClickedMailbox = true;
                            nextMailStepTime = DateTime.Now.AddMilliseconds(2000); // 完美替代原来的 WaitNode(2000)
                            return NodeState.Running; // 挂起当前节点，等待 2 秒
                        }

                        // ==========================================
                        // 全局节流阀：控制整个状态机的时间节奏
                        // ==========================================
                        if (DateTime.Now < nextMailStepTime) return NodeState.Running;

                        // ==========================================
                        // 阶段 1：初始化配置与扫描背包 (延迟2秒后开始执行)
                        // ==========================================
                        if (!isMailingInitialized)
                        {
                            int playerDesc = MemoryAPI.ReadInteger(_hProcess, _playerBase + 0x08);
                            int totalCoins = MemoryAPI.ReadInteger(_hProcess, playerDesc + 0x1260);

                            if (totalCoins < 10000)
                            {
                                Logger.Write($"[{CurrentPlayerName}] [邮寄系统] 余额不足 1 金币 (当前 {totalCoins} 铜)，跳过本次邮寄以防邮资破产！");
                                ExecuteDynamicLua("CloseMail(); ClearCursor();");
                                _hasClickedMailbox = false;
                                isMailingInitialized = false;
                                return NodeState.Success; // 退出并放行
                            }

                            AccountConfig currentConfig = ConfigManager.GetConfig(this.AccountName) ?? new AccountConfig();
                            string mailTarget = currentConfig.Mailing_name;

                            if (string.IsNullOrEmpty(mailTarget))
                            {
                                Logger.Write($"[{CurrentPlayerName}] [邮寄系统] 未配置收件人名字 (Mailing_name为空)，取消邮寄任务！");
                                ExecuteDynamicLua("CloseMail(); ClearCursor();");
                                _hasClickedMailbox = false;
                                isMailingInitialized = false;
                                return NodeState.Success; // 退出并放行
                            }

                            HashSet<int> mailWhitelistIds = ItemDbManager.ParseNamesToIds(currentConfig.Mailing_whitelist);
                            List<InventoryItem> allItems = GetAllInventoryItems(_hProcess);

                            itemsToMail = allItems.FindAll(item =>
                            {
                                if (item.IsSoulbound) return false;

                                bool shouldMail = false;
                                if (mailWhitelistIds.Contains(item.ItemId)) shouldMail = true;

                                if (ItemDbManager.IdMap.TryGetValue(item.ItemId, out var template))
                                {
                                    int quality = template.Quality_ID;
                                    if (quality == 3 && currentConfig.Mailing_exquisite) shouldMail = true;
                                    if (quality == 4 && currentConfig.Mailing_epic) shouldMail = true;
                                }
                                return shouldMail;
                            });

                            needToMailGold = false;
                            copperToMail = 0;
                            if (currentConfig.Mailing_Gold)
                            {
                                int reserveCopper = 50000;
                                int estimatedPostage = (itemsToMail.Count * 30) + 30;

                                if (totalCoins > (reserveCopper + estimatedPostage))
                                {
                                    needToMailGold = true;
                                    copperToMail = totalCoins - reserveCopper - estimatedPostage;
                                }
                            }

                            Logger.Write($"[{CurrentPlayerName}] [邮寄系统] 扫描完毕。待寄物品: {itemsToMail.Count} 件 | 待寄金币: {copperToMail / 10000}金");

                            ExecuteDynamicLua("MailFrameTab_OnClick(2); ClearCursor();");

                            mailIndex = 0;
                            mailSubState = 0;
                            isMailingInitialized = true;
                            nextMailStepTime = DateTime.Now.AddMilliseconds(500);

                            if (itemsToMail.Count == 0 && !needToMailGold)
                            {
                                ExecuteDynamicLua("CloseMail();");
                                Logger.Write($"[{CurrentPlayerName}] [邮寄系统] 没有符合条件的物品和金币需要邮寄。");
                                _hasClickedMailbox = false;
                                isMailingInitialized = false;
                                return NodeState.Success; // 退出并放行
                            }

                            return NodeState.Running;
                        }

                        AccountConfig cfg = ConfigManager.GetConfig(this.AccountName) ?? new AccountConfig();
                        string receiver = cfg.Mailing_name;

                        // ==========================================
                        // 阶段 2：逐个邮寄物品 (三步状态机，防丢弃)
                        // ==========================================
                        if (mailIndex < itemsToMail.Count)
                        {
                            var currentItem = itemsToMail[mailIndex];

                            if (mailSubState == 0)
                            {
                                // 【新增】：解析物品名字并输出详细日志
                                string mailName = "未知";
                                if (ItemDbManager.IdMap.TryGetValue(currentItem.ItemId, out var tpl))
                                {
                                    mailName = !string.IsNullOrEmpty(tpl.Chinese_Name) ? tpl.Chinese_Name : tpl.English_Name;
                                }

                                Logger.Write($"[{CurrentPlayerName}] [邮寄系统] 正在邮寄: {mailName} (ID:{currentItem.ItemId}, Bag:{currentItem.BagId}, Slot:{currentItem.SlotId})");
                                ExecuteDynamicLua($"ClearCursor(); PickupContainerItem({currentItem.BagId}, {currentItem.SlotId});");
                                mailSubState = 1;
                                nextMailStepTime = DateTime.Now.AddMilliseconds(150);
                                return NodeState.Running;
                            }
                            else if (mailSubState == 1)
                            {
                                ExecuteDynamicLua("ClickSendMailItemButton();");
                                mailSubState = 2;
                                nextMailStepTime = DateTime.Now.AddMilliseconds(800);
                                return NodeState.Running;
                            }
                            else if (mailSubState == 2)
                            {
                                string safeMailLua = $@"
                SendMailNameEditBox:SetText('{receiver}');
                SendMailSubjectEditBox:SetText('标题');
                SendMailBodyEditBox:SetText('内容');
                SendMailMailButton_OnClick(); 
            ";
                                ExecuteDynamicLua(safeMailLua);

                                mailSubState = 0;
                                mailIndex++;
                                nextMailStepTime = DateTime.Now.AddMilliseconds(1200);
                                return NodeState.Running;
                            }
                        }

                        // ==========================================
                        // 阶段 3：最后发送兜底金币
                        // ==========================================
                        if (needToMailGold)
                        {
                            string safeGoldMailLua = $@"
            ClearCursor();
            SendMailNameEditBox:SetText('{receiver}');
            SendMailSubjectEditBox:SetText('标题');
            SendMailBodyEditBox:SetText('内容');
            SetSendMailMoney({copperToMail});
            SendMailMailButton_OnClick(); 
        ";
                            ExecuteDynamicLua(safeGoldMailLua);

                            Logger.Write($"[{CurrentPlayerName}] [邮寄系统] 成功寄出金币：{copperToMail / 10000} 金！");
                            needToMailGold = false;
                            nextMailStepTime = DateTime.Now.AddMilliseconds(1200);
                            return NodeState.Running;
                        }

                        // ==========================================
                        // 最终阶段：结束收尾
                        // ==========================================
                        ExecuteDynamicLua("CloseMail(); ClearCursor();");
                        Logger.Write($"[{CurrentPlayerName}] [邮寄系统] 邮寄任务已完成！");

                        // 【核心收尾】：彻底复位所有开关变量，为下一次回城清空状态，并让行为树继续往下走
                        _hasClickedMailbox = false;
                        isMailingInitialized = false;
                        return NodeState.Success;
                    }),


                    //邮寄已经完成，回到正轨路线
                    new ActionNode(() => {
                        if (MoveTo(_hProcess, _playerBase, -4388.85f, 3273.62f, 13.73f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new ActionNode(() => {
                        if (MoveTo(_hProcess, _playerBase, -4377.12f, 3277.25f, 13.56f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() => {
                        if (MoveTo(_hProcess, _playerBase, -4372.11f, 3292.14f, 13.57f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    //启动routeToHome_Alliance正向路线
                    new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToHome_Alliance, false, false), //走到码头

                    new WaitNode(100),
                    //到了码头之后，跳跃一下，防止Z轴坐标太低导致上船卡住
                    new ActionNode(() => {
                        _ = Jump(50);
                        Logger.Write($"[{CurrentPlayerName}] [乘船系统] 已到达码头，正在等待船只靠岸...");
                        return NodeState.Success;
                    }),

                    new WaitNode(100),

                    //已经到达码头了 ，等待船只靠岸
                    // ==========================================
                    // --- 行为树：羽月要塞上船 (去副本/对岸) ---
                    // ==========================================
                    new ActionNode(() =>
                    {
                        // 0. 【终极防呆】只要已经在船上了，直接放行！
                        if (IsOnTransport(_hProcess, _playerBase))
                        {
                            Logger.Write($"[{CurrentPlayerName}] [乘船系统] 已经成功登船！");
                            _boardState = 0;
                            return NodeState.Success;
                        }

                        // 1. 岸上判定阶段
                        if (_boardState == 0)
                        {
                            if (_isFeathermoonDocked) // 💡 认准雷达A
                            {
                                if (_isFeathermoonTimeKnown)
                                {
                                    // 场景 A：时间已知，精准计算
                                    double dockedTime = (DateTime.Now - _feathermoonDockTime).TotalMilliseconds;
                                    double timeRemaining = BOAT_DOCK_TOTAL_TIME - dockedTime;

                                    if (timeRemaining > BOAT_SAFE_BOARD_TIME)
                                    {
                                        Logger.Write($"[{CurrentPlayerName}] [乘船系统] 船只靠岸中，剩余停靠时间: {timeRemaining / 1000:F1}秒，安全登船！");
                                        MoveToOneShot(_playerBase, -4204.26f, 3283.12f, 6.10f);
                                        _boardWaitTimer = DateTime.Now.AddMilliseconds(1500);
                                        _boardState = 1;
                                    }
                                    else
                                    {
                                        Logger.Write($"[{CurrentPlayerName}] [乘船系统] 危险！仅剩 {timeRemaining / 1000:F1}秒，放弃登船，等待下一班...");
                                        _boardState = 99; // 避让防呆
                                    }
                                }
                                else
                                {
                                    // 场景 B：时间未知，真人冒险冲锋！
                                    Logger.Write($"[{CurrentPlayerName}] [乘船系统] 船只停留时间未知，模拟真人冒险冲锋登船！");
                                    MoveToOneShot(_playerBase, -4204.26f, 3283.12f, 6.10f);
                                    _boardWaitTimer = DateTime.Now.AddMilliseconds(1500);
                                    _boardState = 1;
                                }
                            }
                            return NodeState.Running;
                        }

                        // 2. 登船冲刺缓冲阶段
                        else if (_boardState == 1)
                        {
                            if (DateTime.Now >= _boardWaitTimer)
                            {
                                if (!_isFeathermoonDocked) // 💡 冲锋半路雷达发现船开了
                                {
                                    Logger.Write($"[{CurrentPlayerName}] [乘船系统] 冲刺途中船只驶离，紧急退回码头...");
                                    MoveTo(_hProcess, _playerBase, -4214.75f, 3283.06f, 6.31f, 1.0f);
                                    _boardState = 0;
                                    return NodeState.Running;
                                }

                                Logger.Write($"[{CurrentPlayerName}] [乘船系统] 未成功绑定交通工具，退回码头准备重试...");
                                MoveTo(_hProcess, _playerBase, -4214.75f, 3283.06f, 6.31f, 1.0f);
                                _boardWaitTimer = DateTime.Now.AddMilliseconds(1500);
                                _boardState = 2;
                            }
                            return NodeState.Running;
                        }

                        // 3. 退回码头缓冲阶段
                        else if (_boardState == 2)
                        {
                            if (DateTime.Now >= _boardWaitTimer)
                            {
                                if (!_isFeathermoonDocked)
                                {
                                    Logger.Write($"[{CurrentPlayerName}] [乘船系统] 船只已驶离，取消重试，重新等船...");
                                    _boardState = 0;
                                    return NodeState.Running;
                                }

                                Logger.Write($"[{CurrentPlayerName}] [乘船系统] 已退回码头，重新尝试二次登船。");
                                MoveToOneShot(_playerBase, -4204.26f, 3283.12f, 6.10f);
                                _boardWaitTimer = DateTime.Now.AddMilliseconds(1500);
                                _boardState = 1;
                            }
                            return NodeState.Running;
                        }

                        // 99. 避让防呆阶段
                        else if (_boardState == 99)
                        {
                            if (!_isFeathermoonDocked)
                            {
                                Logger.Write($"[{CurrentPlayerName}] [乘船系统] 船只已驶离码头，准备迎接下一班客船。");
                                _boardState = 0;
                            }
                            return NodeState.Running;
                        }

                        return NodeState.Running;
                    }),

                    // 此时已经上船了，移动到对面船边，并监控船只是否靠岸对面码头

                    new WaitNode(100),

                    //这里需要使用船内部3d坐标
                    new ActionNode(() => {
                        if (MoveTo(_hProcess, _playerBase, -3.45f, 7.39f, 6.10f, 0.5f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new ActionNode(() => {
                        if (MoveTo(_hProcess, _playerBase, -7.06f, 0.05f, 6.10f, 0.5f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new ActionNode(() => {
                        if (MoveTo(_hProcess, _playerBase, -3.49f, -10.16f, 6.10f, 0.5f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new WaitNode(100),

                    //上船之后，跳跃一下，防止Z轴坐标太低导致下船失败
                    new ActionNode(() => {
                        _ = Jump(50);
                        return NodeState.Success;
                    }),

                    //等待1秒跳跃稳定
                    new WaitNode(1000),

                    new ActionNode(() =>
                    {
                        // 0. 【终极防呆 / 胜利条件】：只要脱离了船只绑定，直接放行！
                        if (!IsOnTransport(_hProcess, _playerBase))
                        {
                            Logger.Write($"[{CurrentPlayerName}] [乘船系统] 已成功离开船只。");
                            _disembarkState = 0;
                            return NodeState.Success;
                        }

                        // 1. 航行与雷达预警阶段
                        if (_disembarkState == 0)
                        {
                            if (IsShipAtDestination(_hProcess))
                            {
                                Logger.Write($"[{CurrentPlayerName}] [乘船系统] 船只抵达对岸码头水域，等待 3 秒让船停稳...");

                                // 设定 3 秒的【停稳缓冲期】
                                _disembarkWaitTimer = DateTime.Now.AddMilliseconds(3000);
                                _disembarkState = 1;
                            }
                            return NodeState.Running;
                        }

                        // 2. 【新增】：停稳缓冲阶段
                        else if (_disembarkState == 1)
                        {
                            if (DateTime.Now >= _disembarkWaitTimer)
                            {
                                // 二次确认，确保船真的稳稳停在码头
                                if (IsShipAtDestination(_hProcess))
                                {
                                    Logger.Write($"[{CurrentPlayerName}] [乘船系统] 船只已完全停稳！尝试下船...");
                                    MoveToOneShot(_playerBase, -3.41f, -18.98f, 6.99f);

                                    _disembarkWaitTimer = DateTime.Now.AddMilliseconds(1500);
                                    _disembarkState = 2; // 进入下船冲刺阶段
                                }
                                else
                                {
                                    _disembarkState = 0; // 假警报，退回
                                }
                            }
                            return NodeState.Running;
                        }

                        // 3. 下船冲刺缓冲与失败检测阶段 (原阶段 1)
                        else if (_disembarkState == 2)
                        {
                            if (DateTime.Now >= _disembarkWaitTimer)
                            {
                                Logger.Write($"[{CurrentPlayerName}] [乘船系统] 下船似乎卡住了，准备退回船体内部重试...");
                                MoveTo(_hProcess, _playerBase, -3.49f, -10.16f, 6.10f, 1.0f);

                                _disembarkWaitTimer = DateTime.Now.AddMilliseconds(1500);
                                _disembarkState = 3;
                            }
                            return NodeState.Running;
                        }

                        // 4. 退回内部完毕，准备再次冲锋阶段 (原阶段 2)
                        else if (_disembarkState == 3)
                        {
                            if (DateTime.Now >= _disembarkWaitTimer)
                            {
                                Logger.Write($"[{CurrentPlayerName}] [乘船系统] 已退回安全点，重新尝试下船...");
                                MoveToOneShot(_playerBase, -3.41f, -18.98f, 6.99f);

                                _disembarkWaitTimer = DateTime.Now.AddMilliseconds(1500);
                                _disembarkState = 2; // 拨回冲锋阶段
                            }
                            return NodeState.Running;
                        }

                        return NodeState.Running;
                    }),


                    new WaitNode(500),

                    new ActionNode(() => {
                        if (MoveTo(_hProcess, _playerBase, -4350.45f, 2425.78f, 6.89f, 1.0f))
                        {
                            StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    // 此时已经在码头了，直接上马
                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("CastSpellByName('冰甲术');");
                        return NodeState.Success;
                    }), // 补冰甲
                    new WaitNode(500),
                    new ActionNode(() =>
                    {
                        _ = Use_Mount(50);
                        return NodeState.Success;
                    }),   // 上马
                    new WaitNode(3500),
                    
                    new ActionNode(() => {
                        if (MoveTo(_hProcess, _playerBase, -4347.84f, 2416.03f, 7.93f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() => {
                        if (MoveTo(_hProcess, _playerBase, -4348.27f, 2399.26f, 8.34f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() => {
                        if (MoveTo(_hProcess, _playerBase, -4348.39f, 2382.61f, 8.11f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() => {
                        if (MoveTo(_hProcess, _playerBase, -4348.10f, 2346.10f, 8.27f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() => {
                        if (MoveTo(_hProcess, _playerBase, -4348.25f, 2328.73f, 8.24f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() => {
                        if (MoveTo(_hProcess, _playerBase, -4349.27f, 2301.93f, 6.62f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    })
                ),

                // 4、大陆上 (Y < 2340) -> 目标：前往副本门口
                new Sequence(
                    new ConditionNode(() => {
                        // 在船上不归大陆管
                        if (IsOnTransport(_hProcess, _playerBase)) return false;

                        float x = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                        float y = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);

                        // -4347  2428     -4347 2352
                        bool isAtStagingArea = (x > -4360f && y < 1350f);
                        // 2026.08.21修复：从原先的x轴2340f扩大到2430f以修复在码头断点重启后逻辑判断会在厄运内部的问题
                        return y < 2430f && !isAtStagingArea;
                        //return y < 2340f && !isAtStagingArea;
                    }),

                    new ActionNode(() => {
                        Logger.Write($"[{CurrentPlayerName}] [智能寻路] 开始执行 联盟去副本门口路线");
                        return NodeState.Success;
                    }),

                    // 启动routeToInstanceDoor_Alliance正向路线
                    new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToInstanceDoor_Alliance, false, false),
                    new ActionNode(() => {
                        StopMovement(_hProcess, _playerBase);
                        return NodeState.Success;
                    }),

                    // ==========================================
                    // 战前准备判定区：战斗中直接跳过，安全时才套盾上马
                    // ==========================================
                    new Selector(
                        // 【分支 1】：条件拦截（如果在战斗中）
                        new Sequence(
                            // 检测战斗状态 (19号位)
                            new ConditionNode(() => Unit_Behavioral_State(_hProcess, _playerBase, 19)),

                            // 如果条件成立，打个日志，然后返回 Success！
                            new ActionNode(() => {
                                Logger.Write($"[{CurrentPlayerName}] [战前准备] 侦测到正在战斗中，跳过套盾上马流程，直接肉身冲锋！");
                                return NodeState.Success;
                            })
                        ),

                        // 【分支 2】：如果上面条件不成立（不在战斗中），Selector 就会跌落到这里来执行！
                        // 正常执行上马套双盾函数
                        new PreDungeonShieldNode(this, () => _hProcess, () => _playerBase)
                    ),

                    new WaitNode(100),
                    // ==========================================
                    // 完美交接点 (过渡桥梁)
                    // ==========================================
                    new ActionNode(() => {
                        // 目标点设为 -4355，完美跨越 -4360 边界，且正处于进入通道的路上
                        if (MoveTo(_hProcess, _playerBase, -4366.18f, 1344.59f, 158.16f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running; // 没跑过线之前，死死霸占执行权！
                    }),
                    new ActionNode(() => {
                        // 目标点设为 -4355，完美跨越 -4360 边界，且正处于进入通道的路上
                        if (MoveTo(_hProcess, _playerBase, -4337.05f, 1341.57f, 159.23f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running; // 没跑过线之前，死死霸占执行权！
                    })

                ),

                Enter_Dire_Maul_Branch

            );

            // ========================================================
            // 联盟回城清包状态机（原路返回）
            // ========================================================
            _runToInnTree_Alliance = new Selector(

                /* 跑向羽月要塞的逻辑 */

                Export_Dire_Maul_Branch,

                // 2、副本门口去码头路线
                new Sequence(
                    new ConditionNode(() => {
                        // 🌟 核心修复 2：在船上时，强制接管执行权！防止相对坐标导致状态机断裂跳海！
                        if (IsOnTransport(_hProcess, _playerBase)) return true;

                        float x = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                        float y = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);

                        bool isAtStagingArea = (x > -4360f && y < 1350f);

                        return y < 2430f && !isAtStagingArea;
                    }),
                    // 启动routeToInstanceDoor_Alliance反转路线
                    new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToInstanceDoor_Alliance, false, true),

                    new WaitNode(100),
                    //到了码头之后，跳跃一下，防止Z轴坐标太低导致上船卡住
                    new ActionNode(() => {
                        _ = Jump(50);
                        Logger.Write($"[{CurrentPlayerName}] [乘船系统] 已到达码头，正在等待船只靠岸...");
                        return NodeState.Success;
                    }),

                    new WaitNode(100),

                    // 已经到达码头了，等待船只靠岸（智能雷达版 - 回城/对岸专属）
                    // ==========================================
                    // --- 行为树：遗忘海岸上船 (回城/回岛上) ---
                    // ==========================================
                    new ActionNode(() =>
                    {
                        // 0. 【终极防呆】只要已经在船上了，直接放行！
                        if (IsOnTransport(_hProcess, _playerBase))
                        {
                            Logger.Write($"[{CurrentPlayerName}] [乘船系统] 已经成功登船！");
                            _boardState = 0;
                            return NodeState.Success;
                        }

                        // 1. 岸上判定阶段
                        if (_boardState == 0)
                        {
                            if (_isMainlandDocked) // 💡 认准雷达B
                            {
                                if (_isMainlandTimeKnown)
                                {
                                    // 场景 A：时间已知，精准计算
                                    double dockedTime = (DateTime.Now - _mainlandDockTime).TotalMilliseconds;
                                    double timeRemaining = BOAT_DOCK_TOTAL_TIME - dockedTime;

                                    if (timeRemaining > BOAT_SAFE_BOARD_TIME)
                                    {
                                        Logger.Write($"[{CurrentPlayerName}] [乘船系统] 船只靠岸中，剩余停靠时间: {timeRemaining / 1000:F1}秒，安全登船！");
                                        MoveToOneShot(_playerBase, -4350.89f, 2435.99f, 6.10f);
                                        _boardWaitTimer = DateTime.Now.AddMilliseconds(1500);
                                        _boardState = 1;
                                    }
                                    else
                                    {
                                        Logger.Write($"[{CurrentPlayerName}] [乘船系统] 危险！仅剩 {timeRemaining / 1000:F1}秒，放弃登船，等待下一班...");
                                        _boardState = 99; // 避让防呆
                                    }
                                }
                                else
                                {
                                    // 场景 B：时间未知，真人冒险冲锋！
                                    Logger.Write($"[{CurrentPlayerName}] [乘船系统] 船只停留时间未知，模拟真人冒险冲锋登船！");
                                    MoveToOneShot(_playerBase, -4350.89f, 2435.99f, 6.10f);
                                    _boardWaitTimer = DateTime.Now.AddMilliseconds(1500);
                                    _boardState = 1;
                                }
                            }
                            return NodeState.Running;
                        }

                        // 2. 登船冲刺缓冲阶段
                        else if (_boardState == 1)
                        {
                            if (DateTime.Now >= _boardWaitTimer)
                            {
                                if (!_isMainlandDocked) // 💡 冲锋半路雷达发现船开了
                                {
                                    Logger.Write($"[{CurrentPlayerName}] [乘船系统] 冲刺途中船只驶离，紧急退回码头...");
                                    MoveTo(_hProcess, _playerBase, -4348.90f, 2426.92f, 6.70f, 1.0f);
                                    _boardState = 0;
                                    return NodeState.Running;
                                }

                                Logger.Write($"[{CurrentPlayerName}] [乘船系统] 未成功绑定交通工具，退回码头准备重试...");
                                MoveTo(_hProcess, _playerBase, -4348.90f, 2426.92f, 6.70f, 1.0f);
                                _boardWaitTimer = DateTime.Now.AddMilliseconds(1500);
                                _boardState = 2;
                            }
                            return NodeState.Running;
                        }

                        // 3. 退回码头缓冲阶段
                        else if (_boardState == 2)
                        {
                            if (DateTime.Now >= _boardWaitTimer)
                            {
                                if (!_isMainlandDocked)
                                {
                                    Logger.Write($"[{CurrentPlayerName}] [乘船系统] 船只已驶离，取消重试，重新等船...");
                                    _boardState = 0;
                                    return NodeState.Running;
                                }

                                Logger.Write($"[{CurrentPlayerName}] [乘船系统] 已退回码头，重新尝试二次登船。");
                                MoveToOneShot(_playerBase, -4350.89f, 2435.99f, 6.10f);
                                _boardWaitTimer = DateTime.Now.AddMilliseconds(1500);
                                _boardState = 1;
                            }
                            return NodeState.Running;
                        }

                        // 99. 避让防呆阶段
                        else if (_boardState == 99)
                        {
                            if (!_isMainlandDocked)
                            {
                                Logger.Write($"[{CurrentPlayerName}] [乘船系统] 船只已驶离码头，准备迎接下一班客船。");
                                _boardState = 0;
                            }
                            return NodeState.Running;
                        }

                        return NodeState.Running;
                    }),

                    // 此时已经上船了，移动到对面船边，并监控船只是否靠岸对面码头

                    new WaitNode(100),

                    //这里需要使用船内部3d坐标
                    new ActionNode(() => {
                        if (MoveTo(_hProcess, _playerBase, -3.37f, -8.77f, 6.10f, 0.5f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new ActionNode(() => {
                        if (MoveTo(_hProcess, _playerBase, -6.58f, -1.25f, 6.10f, 0.5f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new ActionNode(() => {
                        if (MoveTo(_hProcess, _playerBase, -3.02f, 8.63f, 6.10f, 0.5f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new WaitNode(100),

                    //上船之后，跳跃一下，防止Z轴坐标太低导致下船失败
                    new ActionNode(() => {
                        _ = Jump(50);
                        return NodeState.Success;
                    }),

                    //等待1秒跳跃稳定
                    new WaitNode(1000),

                    new ActionNode(() =>
                    {
                        // 0. 【终极防呆 / 胜利条件】：只要脱离了船只绑定，直接放行！
                        if (!IsOnTransport(_hProcess, _playerBase))
                        {
                            Logger.Write($"[{CurrentPlayerName}] [乘船系统] 已成功离开船只。");
                            _disembarkState = 0;
                            return NodeState.Success;
                        }

                        // 1. 航行与雷达预警阶段
                        if (_disembarkState == 0)
                        {
                            if (IsFeathermoonShipDocked(_hProcess))
                            {
                                Logger.Write($"[{CurrentPlayerName}] [乘船系统] 船只抵达对岸码头水域，等待 3 秒让船停稳...");

                                // 设定 3 秒的【停稳缓冲期】
                                _disembarkWaitTimer = DateTime.Now.AddMilliseconds(3000);
                                _disembarkState = 1;
                            }
                            return NodeState.Running;
                        }

                        // 2. 【新增】：停稳缓冲阶段
                        else if (_disembarkState == 1)
                        {
                            if (DateTime.Now >= _disembarkWaitTimer)
                            {
                                // 二次确认，确保船真的稳稳停在码头
                                if (IsFeathermoonShipDocked(_hProcess))
                                {
                                    Logger.Write($"[{CurrentPlayerName}] [乘船系统] 船只已完全停稳！尝试下船...");
                                    MoveToOneShot(_playerBase, -1.61f, 16.98f, 6.23f);

                                    _disembarkWaitTimer = DateTime.Now.AddMilliseconds(1500);
                                    _disembarkState = 2; // 进入下船冲刺阶段
                                }
                                else
                                {
                                    _disembarkState = 0; // 假警报，退回
                                }
                            }
                            return NodeState.Running;
                        }

                        // 3. 下船冲刺缓冲与失败检测阶段 (原阶段 1)
                        else if (_disembarkState == 2)
                        {
                            if (DateTime.Now >= _disembarkWaitTimer)
                            {
                                Logger.Write($"[{CurrentPlayerName}] [乘船系统] 下船似乎卡住了，准备退回船体内部重试...");
                                MoveTo(_hProcess, _playerBase, -3.02f, 8.63f, 6.10f, 1.0f);

                                _disembarkWaitTimer = DateTime.Now.AddMilliseconds(1500);
                                _disembarkState = 3;
                            }
                            return NodeState.Running;
                        }

                        // 4. 退回内部完毕，准备再次冲锋阶段 (原阶段 2)
                        else if (_disembarkState == 3)
                        {
                            if (DateTime.Now >= _disembarkWaitTimer)
                            {
                                Logger.Write($"[{CurrentPlayerName}] [乘船系统] 已退回安全点，重新尝试下船...");
                                MoveToOneShot(_playerBase, -1.61f, 16.98f, 6.23f);

                                _disembarkWaitTimer = DateTime.Now.AddMilliseconds(1500);
                                _disembarkState = 2; // 拨回冲锋阶段
                            }
                            return NodeState.Running;
                        }

                        return NodeState.Running;
                    }),

                    new WaitNode(500),

                    new ActionNode(() => {
                        if (MoveTo(_hProcess, _playerBase, -4213.95f, 3284.98f, 6.15f, 1.0f))
                        {
                            StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new WaitNode(100),

                    new ActionNode(() =>
                    {
                        _ = Use_Mount(50);
                        return NodeState.Success;
                    }),   // 上马
                    new WaitNode(3500)

                ),

                // 2、进入营地，跑到商人面前 (Y >= 3260f)
                new Sequence(
                    new ConditionNode(() => MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC) >= 3260f),
                    new ActionNode(() => {
                        Logger.Write($"[{CurrentPlayerName}] [智能寻路] 开始执行 联盟旅店门口去售卖NPC路线...");
                        return NodeState.Success;
                    }),
                    // 启动routeToHome_Alliance反转路线
                    new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToHome_Alliance, false, true),
                    // 到达商人面前，踩死刹车
                    new ActionNode(() => { StopMovement(_hProcess, _playerBase); return NodeState.Success; }),
                    new WaitNode(500),
                    new DestroyGarbageNode(this, () => _hProcess, () => _playerBase),   //自动摧毁名单内的物品
                    new WaitNode(500),
                    // 目标选中商人，打开商店
                    new ActionNode(() =>
                    {
                        uint targetBase = GetUnitBaseByEntry(_hProcess, 10293);  // 杜希雅·霜月  
                        if (targetBase != 0)
                        {
                            RightClickUnit(targetBase);
                            Logger.Write($"[{CurrentPlayerName}] [商人系统] 已经向商人（杜希雅·霜月） 0x{targetBase:X} 发送了一次右键CALL，准备清理背包！");
                        }
                        return NodeState.Success;

                    }),
                    new WaitNode(2000),

                    // 售卖垃圾节点
                    new ActionNode(() =>
                    {
                        if (!isSellingInitialized)
                        {
                            // 🌟 核心修改：用基类保存的 AccountName 去 JSON 里精准拉取配置！
                            AccountConfig currentConfig = ConfigManager.GetConfig(this.AccountName) ?? new AccountConfig();

                            // 3. 将白名单字符串转换为 ID 集合
                            HashSet<int> whitelistIds = ItemDbManager.ParseNamesToIds(currentConfig.Whitelist);

                            List<InventoryItem> allItems = GetAllInventoryItems(_hProcess);

                            // 4. 核心过滤逻辑
                            itemsToSell = allItems.FindAll(item =>
                            {
                                // 【规则 A】：灵魂绑定的绝对不卖
                                if (item.IsSoulbound) return false;

                                // 【规则 A2】：系统内置底层白名单，绝对不卖
                                if (BuiltInWhitelist.Contains(item.ItemId)) return false;

                                // 【规则 B】：在用户配置的白名单里的物品，不卖
                                if (whitelistIds.Contains(item.ItemId)) return false;

                                // 【规则 C】：判断物品品质是否需要保留
                                if (ItemDbManager.IdMap.TryGetValue(item.ItemId, out var template))
                                {
                                    int quality = template.Quality_ID;

                                    if (quality == 3 && currentConfig.KeepExquisite) return false; // 蓝装
                                    if (quality == 4 && currentConfig.KeepEpic) return false;      // 紫装

                                    // 传说(5)及神器(6)，作为最后兜底，绝对不卖
                                    if (quality >= 5) return false;
                                }

                                // 都不是，那就是垃圾，卖！
                                return true;
                            });

                            Logger.Write($"[{CurrentPlayerName}] [背包系统] 根据账号 [{AccountName}] 的配置，扫描到背包 总物品: {allItems.Count} | 待售卖: {itemsToSell.Count}");

                            sellIndex = 0;
                            isSellingInitialized = true;
                            nextSellTime = DateTime.Now;

                            // 没垃圾可卖的情况
                            if (itemsToSell.Count == 0)
                            {
                                // 第一步：发送修理指令，并挂起 500ms
                                if (!_isWaitingToCloseMerchant)
                                {
                                    ExecuteDynamicLua("RepairAllItems()");
                                    nextSellTime = DateTime.Now.AddMilliseconds(500);
                                    _isWaitingToCloseMerchant = true;
                                    return NodeState.Running; // 必须 return Running，让行为树等这 500ms
                                }

                                // 等待期间：卡住不放行
                                if (DateTime.Now < nextSellTime) return NodeState.Running;

                                // 第二步：时间到了，关商店，清理状态，大功告成
                                ExecuteDynamicLua("CloseMerchant()");
                                Logger.Write($"[{CurrentPlayerName}] [背包系统] 背包很干净，装备已修满！");

                                isSellingInitialized = false;
                                _isWaitingToCloseMerchant = false;
                                _hasAttemptedSell = true;
                                _needGoHome = false;

                                // 注意：这里不要写 _attemptingTurtleBot = false; 留给邮寄去关！
                                return NodeState.Success;
                            }
                        }

                        // 垃圾全部卖完的情况
                        if (sellIndex >= itemsToSell.Count)
                        {
                            // 第一步：发送修理指令，并挂起 500ms
                            if (!_isWaitingToCloseMerchant)
                            {
                                ExecuteDynamicLua("RepairAllItems()");
                                nextSellTime = DateTime.Now.AddMilliseconds(500);
                                _isWaitingToCloseMerchant = true;
                                return NodeState.Running; // 必须 return Running，让行为树等这 500ms
                            }

                            // 等待期间：卡住不放行
                            if (DateTime.Now < nextSellTime) return NodeState.Running;

                            // 第二步：时间到了，关商店，清理状态，大功告成
                            ExecuteDynamicLua("CloseMerchant()");
                            Logger.Write($"[{CurrentPlayerName}] [背包系统] 垃圾清理完毕，装备已修满！");

                            isSellingInitialized = false;
                            _isWaitingToCloseMerchant = false;
                            _hasAttemptedSell = true;
                            _needGoHome = false;

                            // 注意：这里也不要写 _attemptingTurtleBot = false; 留给邮寄去关！
                            return NodeState.Success;
                        }

                        // 控制售卖频率，防止掉线 (300ms 卖一件非常安全)
                        if (DateTime.Now < nextSellTime) return NodeState.Running;

                        var currentItem = itemsToSell[sellIndex];
                        string sellName = "未知";
                        if (ItemDbManager.IdMap.TryGetValue(currentItem.ItemId, out var tpl))
                        {
                            sellName = !string.IsNullOrEmpty(tpl.Chinese_Name) ? tpl.Chinese_Name : tpl.English_Name;
                        }

                        Logger.Write($"[{CurrentPlayerName}] [清包系统] 正在售卖: {sellName} (ID:{currentItem.ItemId}, Bag:{currentItem.BagId}, Slot:{currentItem.SlotId})");
                        ExecuteDynamicLua($"UseContainerItem({currentItem.BagId}, {currentItem.SlotId})");
                        sellIndex++;
                        nextSellTime = DateTime.Now.AddMilliseconds(300);
                        return NodeState.Running;
                    })
                )
            );


            // =========================================================
            // Turtle WoW 专属：75B 修理机器人纯内存售卖和邮寄状态机节点
            // =========================================================
            Node TurtleRepairBranch = new Sequence(
                // 节点生效条件：只在Turtle WoW尝试清包时激活
                new ConditionNode(() => _attemptingTurtleBot),
                // 停止移动
                new ActionNode(() => {
                    StopMovement(_hProcess, _playerBase);
                    return NodeState.Success;
                }),
                new WaitNode(500),
                new DestroyGarbageNode(this, () => _hProcess, () => _playerBase),   //自动摧毁名单内的物品
                new WaitNode(2000),
                // 步骤 1：智能寻找与召唤 75B 机器人 (带超时轮询机制)
                new ActionNode(() =>
                {
                    bool ROBOT_75B_Spell_Status = Query_known_skills(_hProcess, 46457);
                    if (!ROBOT_75B_Spell_Status)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [商人系统] 未发现已知的 75B 机器人技能(未购买)，触发原生回城机制！");
                        _attemptingTurtleBot = false;
                        _needGoHome = true;
                        return NodeState.Failure;
                    }

                    uint robotBase = GetMySpecificPetBase(_hProcess, 50041);
                    if (robotBase == 0)
                    {
                        // 如果 _robotWaitTimeout 是 MinValue，说明是刚发现没机器人，第一次触发施法
                        if (_robotWaitTimeout == DateTime.MinValue)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [商人系统] 未发现身边的 75B 机器人，开始施法召唤 (最长等待 8 秒)...");
                            ExecuteDynamicLua("CastSpellByName('修理机器人75B型')");
                            // 设定 8 秒超时上限：给足读条、网络延迟和实体生成的时间
                            _robotWaitTimeout = DateTime.Now.AddMilliseconds(8000);
                            return NodeState.Running; // 挂起状态机，死等
                        }

                        // 如果已经施法，且还在 8 秒等待期内
                        if (DateTime.Now < _robotWaitTimeout)
                        {
                            return NodeState.Running; // 继续挂起，等待下一帧检查
                        }
                        else
                        {
                            // 8 秒过去了，内存里依然没有机器人
                            Logger.Write($"[{CurrentPlayerName}] [商人系统] 召唤超时(可能在CD中或服务端极度卡顿)，触发原生回城！");
                            _robotWaitTimeout = DateTime.MinValue; // 状态复位
                            _attemptingTurtleBot = false;
                            _needGoHome = true;
                            return NodeState.Failure;
                        }
                    }
                    else
                    {
                        // 内存中找到了机器人！
                        if (_robotWaitTimeout != DateTime.MinValue)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [商人系统] 75B 机器人已成功生成并被捕获！");
                            _robotWaitTimeout = DateTime.MinValue; // 状态复位
                        }
                        else
                        {
                            Logger.Write($"[{CurrentPlayerName}] [商人系统] 75B 机器人已在身边，准备直接交互。");
                        }
                        return NodeState.Success; // 顺利放行
                    }
                }),

                // 步骤 2：短暂延迟让机器人模型在客户端中彻底固化，防止交互失效
                new WaitNode(500),

                // 步骤 3：发送右键交互 CALL
                new ActionNode(() =>
                {
                    uint robotBase = GetMySpecificPetBase(_hProcess, 50041);
                    if (robotBase != 0)
                    {
                        RightClickUnit(robotBase);
                        Logger.Write($"[{CurrentPlayerName}] [商人系统] 已经向商人（修理机器人75B型） 0x{robotBase:X} 发送了一次右键CALL，准备清理背包！");
                        return NodeState.Success;
                    }

                    // 理论上走不到这里，做个兜底
                    _attemptingTurtleBot = false;
                    _needGoHome = true;
                    return NodeState.Failure;
                }),

                // 步骤 4：打开商店后的缓冲延迟
                new WaitNode(2000),

                // 步骤 5：售卖垃圾节点 
                new ActionNode(() =>
                {
                    if (!isSellingInitialized)
                    {
                        // 用基类保存的 AccountName 去 JSON 里精准拉取配置！
                        AccountConfig currentConfig = ConfigManager.GetConfig(this.AccountName) ?? new AccountConfig();

                        // 3. 将白名单字符串转换为 ID 集合
                        HashSet<int> whitelistIds = ItemDbManager.ParseNamesToIds(currentConfig.Whitelist);

                        List<InventoryItem> allItems = GetAllInventoryItems(_hProcess);

                        // 4. 核心过滤逻辑
                        itemsToSell = allItems.FindAll(item =>
                        {
                            // 【规则 A】：灵魂绑定的绝对不卖
                            if (item.IsSoulbound) return false;

                            // 【规则 A2】：系统内置底层白名单，绝对不卖
                            if (BuiltInWhitelist.Contains(item.ItemId)) return false;

                            // 【规则 B】：在用户配置的白名单里的物品，不卖
                            if (whitelistIds.Contains(item.ItemId)) return false;

                            // 【规则 C】：判断物品品质是否需要保留
                            if (ItemDbManager.IdMap.TryGetValue(item.ItemId, out var template))
                            {
                                int quality = template.Quality_ID;

                                if (quality == 3 && currentConfig.KeepExquisite) return false; // 蓝装
                                if (quality == 4 && currentConfig.KeepEpic) return false;      // 紫装

                                // 传说(5)及神器(6)，作为最后兜底，绝对不卖
                                if (quality >= 5) return false;
                            }

                            // 都不是，那就是垃圾，卖！
                            return true;
                        });

                        Logger.Write($"[{CurrentPlayerName}] [背包系统] 根据账号 [{AccountName}] 的配置，扫描到背包 总物品: {allItems.Count} | 待售卖: {itemsToSell.Count}");

                        sellIndex = 0;
                        isSellingInitialized = true;
                        nextSellTime = DateTime.Now;

                        // 没垃圾可卖的情况
                        if (itemsToSell.Count == 0)
                        {
                            // 第一步：发送修理指令，并挂起 500ms
                            if (!_isWaitingToCloseMerchant)
                            {
                                ExecuteDynamicLua("RepairAllItems()");
                                nextSellTime = DateTime.Now.AddMilliseconds(500);
                                _isWaitingToCloseMerchant = true;
                                return NodeState.Running; // 必须 return Running，让行为树等这 500ms
                            }

                            // 等待期间：卡住不放行
                            if (DateTime.Now < nextSellTime) return NodeState.Running;

                            // 第二步：时间到了，关商店，清理状态，大功告成
                            ExecuteDynamicLua("CloseMerchant()");
                            Logger.Write($"[{CurrentPlayerName}] [背包系统] 背包很干净，装备已修满！");

                            isSellingInitialized = false;
                            _isWaitingToCloseMerchant = false;
                            _hasAttemptedSell = true;

                            // 注意：这里不要写 _attemptingTurtleBot = false; 留给邮寄去关！
                            return NodeState.Success;
                        }
                    }

                    // 垃圾全部卖完的情况
                    if (sellIndex >= itemsToSell.Count)
                    {
                        // 第一步：发送修理指令，并挂起 500ms
                        if (!_isWaitingToCloseMerchant)
                        {
                            ExecuteDynamicLua("RepairAllItems()");
                            nextSellTime = DateTime.Now.AddMilliseconds(500);
                            _isWaitingToCloseMerchant = true;
                            return NodeState.Running; // 必须 return Running，让行为树等这 500ms
                        }

                        // 等待期间：卡住不放行
                        if (DateTime.Now < nextSellTime) return NodeState.Running;

                        // 第二步：时间到了，关商店，清理状态，大功告成
                        ExecuteDynamicLua("CloseMerchant()");
                        Logger.Write($"[{CurrentPlayerName}] [背包系统] 垃圾清理完毕，装备已修满！");

                        isSellingInitialized = false;
                        _isWaitingToCloseMerchant = false;
                        _hasAttemptedSell = true;

                        // 注意：这里也不要写 _attemptingTurtleBot = false; 留给邮寄去关！
                        return NodeState.Success;
                    }

                    if (DateTime.Now < nextSellTime) return NodeState.Running;
                    var currentItem = itemsToSell[sellIndex];
                    string sellName = "未知";
                    if (ItemDbManager.IdMap.TryGetValue(currentItem.ItemId, out var tpl))
                    {
                        sellName = !string.IsNullOrEmpty(tpl.Chinese_Name) ? tpl.Chinese_Name : tpl.English_Name;
                    }

                    Logger.Write($"[{CurrentPlayerName}] [清包系统] 正在售卖: {sellName} (ID:{currentItem.ItemId}, Bag:{currentItem.BagId}, Slot:{currentItem.SlotId})");
                    ExecuteDynamicLua($"UseContainerItem({currentItem.BagId}, {currentItem.SlotId})");
                    sellIndex++;
                    nextSellTime = DateTime.Now.AddMilliseconds(300);
                    return NodeState.Running;
                }),

                new WaitNode(100),

                new ActionNode(() =>
                {
                    // 清理一下可能的 UI 残留
                    ExecuteDynamicLua("CloseMerchant();CloseMail(); ClearCursor();");
                    return NodeState.Success;
                }),

                new WaitNode(2000),


                // 步骤 6：在内存中寻找自己的 邮箱 技能ID
                new ActionNode(() =>
                {
                    // ==========================================
                    // 阶段 0：寻找、召唤并打开邮箱
                    // ==========================================
                    if (!_hasClickedMailbox && !isMailingInitialized)
                    {
                        // 检查是否购买随身邮箱
                        if (!Query_known_skills(_hProcess, 46001))
                        {
                            Logger.Write($"[{CurrentPlayerName}] [邮箱系统] 未发现随身邮箱技能，自动跳过本次邮寄！");
                            _attemptingTurtleBot = false;       //如果没有购买随身邮箱，直接放行而不是触发原生回城，因为在外部已经做了刷本20次自动回城邮寄(触发条件：乌龟服、购买了机器人、没有购买邮箱)
                            return NodeState.Success;
                        }

                        uint mailboxBase = GetGameObjectBaseByEntry(_hProcess, 144112);

                        if (mailboxBase == 0)
                        {
                            // 内存中没有邮箱，说明需要召唤
                            // 使用节流阀，防止一秒钟发送 60 次施法指令
                            if (DateTime.Now >= nextMailStepTime)
                            {
                                Logger.Write($"[{CurrentPlayerName}] [邮箱系统] 未发现身边的随身邮箱，开始施法召唤...");
                                ExecuteDynamicLua("CastSpellByName('移动邮箱终端')");

                                // 挂起状态机，给 3秒读条 + 0.5秒生成 留出时间
                                nextMailStepTime = DateTime.Now.AddMilliseconds(3500);
                            }
                            return NodeState.Running; // 正在召唤中，卡住状态机
                        }
                        else
                        {
                            // 【关键修复】：如果实体出现了，但时间锁还没到，说明它刚刷出来，等它固化！
                            if (DateTime.Now < nextMailStepTime) return NodeState.Running;

                            // 找到了邮箱，发送交互
                            RightClickGameObject(mailboxBase);
                            Logger.Write($"[{CurrentPlayerName}] [邮箱系统] 已经向邮箱 0x{mailboxBase:X} 发送了一次右键CALL，等待打开邮箱");

                            // 【核心修复】：必须拨动状态，并强制等待 2 秒让 UI 弹出来！
                            _hasClickedMailbox = true;
                            nextMailStepTime = DateTime.Now.AddMilliseconds(2000);
                            return NodeState.Running;
                        }
                    }

                    // 全局节流阀：控制整个状态机的时间节奏 (阶段 1,2,3 的保护伞)
                    if (DateTime.Now < nextMailStepTime) return NodeState.Running;

                    // ==========================================
                    // 阶段 1：初始化配置与扫描背包 (延迟2秒后开始执行)
                    // ==========================================
                    if (!isMailingInitialized)
                    {
                        int playerDesc = MemoryAPI.ReadInteger(_hProcess, _playerBase + 0x08);
                        int totalCoins = MemoryAPI.ReadInteger(_hProcess, playerDesc + 0x1260);

                        if (totalCoins < 10000)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [邮寄系统] 余额不足 1 金币 (当前 {totalCoins} 铜)，跳过本次邮寄以防邮资破产！");
                            ExecuteDynamicLua("CloseMail(); ClearCursor();");
                            _hasClickedMailbox = false;
                            isMailingInitialized = false;
                            _attemptingTurtleBot = false; // 关锁！
                            return NodeState.Success; // 退出并放行
                        }

                        AccountConfig currentConfig = ConfigManager.GetConfig(this.AccountName) ?? new AccountConfig();
                        string mailTarget = currentConfig.Mailing_name;

                        if (string.IsNullOrEmpty(mailTarget))
                        {
                            Logger.Write($"[{CurrentPlayerName}] [邮寄系统] 未配置收件人名字 (Mailing_name为空)，取消邮寄任务！");
                            ExecuteDynamicLua("CloseMail(); ClearCursor();");
                            _hasClickedMailbox = false;
                            isMailingInitialized = false;
                            _attemptingTurtleBot = false; // 关锁！
                            return NodeState.Success; // 退出并放行
                        }

                        HashSet<int> mailWhitelistIds = ItemDbManager.ParseNamesToIds(currentConfig.Mailing_whitelist);
                        List<InventoryItem> allItems = GetAllInventoryItems(_hProcess);

                        itemsToMail = allItems.FindAll(item =>
                        {
                            if (item.IsSoulbound) return false;

                            bool shouldMail = false;
                            if (mailWhitelistIds.Contains(item.ItemId)) shouldMail = true;

                            if (ItemDbManager.IdMap.TryGetValue(item.ItemId, out var template))
                            {
                                int quality = template.Quality_ID;
                                if (quality == 3 && currentConfig.Mailing_exquisite) shouldMail = true;
                                if (quality == 4 && currentConfig.Mailing_epic) shouldMail = true;
                            }
                            return shouldMail;
                        });

                        needToMailGold = false;
                        copperToMail = 0;
                        if (currentConfig.Mailing_Gold)
                        {
                            int reserveCopper = 50000;
                            int estimatedPostage = (itemsToMail.Count * 30) + 30;

                            if (totalCoins > (reserveCopper + estimatedPostage))
                            {
                                needToMailGold = true;
                                copperToMail = totalCoins - reserveCopper - estimatedPostage;
                            }
                        }

                        Logger.Write($"[{CurrentPlayerName}] [邮寄系统] 扫描完毕。待寄物品: {itemsToMail.Count} 件 | 待寄金币: {copperToMail / 10000}金");

                        ExecuteDynamicLua("MailFrameTab_OnClick(2); ClearCursor();");

                        mailIndex = 0;
                        mailSubState = 0;
                        isMailingInitialized = true;
                        nextMailStepTime = DateTime.Now.AddMilliseconds(500);

                        if (itemsToMail.Count == 0 && !needToMailGold)
                        {
                            ExecuteDynamicLua("CloseMail();");
                            Logger.Write($"[{CurrentPlayerName}] [邮寄系统] 没有符合条件的物品和金币需要邮寄。");
                            _hasClickedMailbox = false;
                            isMailingInitialized = false;
                            _attemptingTurtleBot = false; // 关锁！
                            return NodeState.Success; // 退出并放行
                        }

                        return NodeState.Running;
                    }

                    AccountConfig cfg = ConfigManager.GetConfig(this.AccountName) ?? new AccountConfig();
                    string receiver = cfg.Mailing_name;

                    // ==========================================
                    // 阶段 2：逐个邮寄物品 (三步状态机，防丢弃)
                    // ==========================================
                    if (mailIndex < itemsToMail.Count)
                    {
                        var currentItem = itemsToMail[mailIndex];

                        if (mailSubState == 0)
                        {
                            // 【新增】：解析物品名字并输出详细日志
                            string mailName = "未知";
                            if (ItemDbManager.IdMap.TryGetValue(currentItem.ItemId, out var tpl))
                            {
                                mailName = !string.IsNullOrEmpty(tpl.Chinese_Name) ? tpl.Chinese_Name : tpl.English_Name;
                            }

                            Logger.Write($"[{CurrentPlayerName}] [邮寄系统] 正在邮寄: {mailName} (ID:{currentItem.ItemId}, Bag:{currentItem.BagId}, Slot:{currentItem.SlotId})");

                            ExecuteDynamicLua($"ClearCursor(); PickupContainerItem({currentItem.BagId}, {currentItem.SlotId});");
                            mailSubState = 1;
                            nextMailStepTime = DateTime.Now.AddMilliseconds(150);
                            return NodeState.Running;
                        }
                        else if (mailSubState == 1)
                        {
                            ExecuteDynamicLua("ClickSendMailItemButton();");
                            mailSubState = 2;
                            nextMailStepTime = DateTime.Now.AddMilliseconds(800);
                            return NodeState.Running;
                        }
                        else if (mailSubState == 2)
                        {
                            string safeMailLua = $@"
                SendMailNameEditBox:SetText('{receiver}');
                SendMailSubjectEditBox:SetText('标题');
                SendMailBodyEditBox:SetText('内容');
                SendMailMailButton_OnClick(); 
            ";
                            ExecuteDynamicLua(safeMailLua);

                            mailSubState = 0;
                            mailIndex++;
                            nextMailStepTime = DateTime.Now.AddMilliseconds(1200);
                            return NodeState.Running;
                        }
                    }

                    // ==========================================
                    // 阶段 3：最后发送兜底金币
                    // ==========================================
                    if (needToMailGold)
                    {
                        string safeGoldMailLua = $@"
            ClearCursor();
            SendMailNameEditBox:SetText('{receiver}');
            SendMailSubjectEditBox:SetText('标题');
            SendMailBodyEditBox:SetText('内容');
            SetSendMailMoney({copperToMail});
            SendMailMailButton_OnClick(); 
        ";
                        ExecuteDynamicLua(safeGoldMailLua);

                        Logger.Write($"[{CurrentPlayerName}] [邮寄系统] 成功寄出金币：{copperToMail / 10000} 金！");
                        needToMailGold = false;
                        nextMailStepTime = DateTime.Now.AddMilliseconds(1200);
                        return NodeState.Running;
                    }

                    // ==========================================
                    // 最终阶段：结束收尾
                    // ==========================================
                    ExecuteDynamicLua("CloseMail(); ClearCursor();");
                    Logger.Write($"[{CurrentPlayerName}] [邮寄系统] 全部邮寄任务圆满完成，关闭邮箱！");

                    // 【核心收尾】：彻底复位所有开关变量，为下一次回城清空状态，并让行为树继续往下走
                    _hasClickedMailbox = false;
                    isMailingInitialized = false;
                    _attemptingTurtleBot = false; // 只有在邮寄也彻底干完后，才关闭乌龟服判定锁！
                    return NodeState.Success;
                }),


                new WaitNode(100),

                new ActionNode(() =>
                {
                    // 能走到这里此时百分百清完包了，再次检测背包
                    if (CheckIfInventoryFull(_hProcess, _playerBase))
                    {
                        _attemptingTurtleBot = false; // 关闭乌龟服专属流程
                        _needGoHome = true;           // 接力给原版的回城寻路系统
                        Logger.Write($"[{CurrentPlayerName}] [商人系统] 清理垃圾后背包可用格数仍然小于5格(可能白名单太多)，触发强制回城！");
                    }
                    return NodeState.Success;
                })
            );

            // 野外跑尸与副本跑尸状态机
            Node corpseRunBranch = new Sequence(
                new ConditionNode(() => _isDead == true),
                new Selector(
                    // 刚刚死亡，还躺在地上，执行释放灵魂
                    new Sequence(
                        new ConditionNode(() => !_hasReleasedSpirit),
                        new ActionNode(() => {
                            StopMovement(_hProcess, _playerBase);
                            Logger.Write($"[{CurrentPlayerName}] [野外跑尸] 确认死亡，已发送释放灵魂指令！正在等待服务器传送...");
                            ExecuteDynamicLua("RepopMe()");   //释放灵魂
                            _hasReleasedSpirit = true;
                            _releaseSpiritTime = DateTime.Now; // 精准记录点击释放灵魂的时间！
                            return NodeState.Success;
                        })
                    ),

                    // 【副本死亡专线】 (彻底无视野外的那些接驳线！)
                    new Sequence(
                        new ConditionNode(() => {
                            // 释放灵魂后的 5 秒内处于服务器传送过渡期，坚决不进行任何寻路判定，防止脏数据缓存！
                            if ((DateTime.Now - _releaseSpiritTime).TotalSeconds < 5) return false;
                            return IsCorpseInInstance(_hProcess, -3908.03f, 1130.0f) && _hasReleasedSpirit && HasGhostDebuff(_hProcess, _playerBase);
                        }),
                        new Selector(
                            // 1. 已跨入副本 (MapID=429)，复活！
                            new Sequence(
                                new ConditionNode(() => _mapId == 429),
                                new ActionNode(() => {
                                    StopMovement(_hProcess, _playerBase);
                                    Logger.Write($"[{CurrentPlayerName}] [副本复活] 已跨入副本区域！已复活！");
                                    ExecuteDynamicLua("RetrieveCorpse()");
                                    return NodeState.Success;
                                }),
                                new WaitNode(3000)
                            ),
                            // 2. 在门外 10 码内
                            new Sequence(
                                new ConditionNode(() => _mapId == 1 && GetDistanceToCoords(_hProcess, _playerBase, -3732.33f, 934.51f, 161.73f) < 10.0f),
                                new ActionNode(() => {
                                    if (MoveTo(_hProcess, _playerBase, -3741.32f, 934.54f, 160.99f, 0.5f))
                                    {
                                        StopMovement(_hProcess, _playerBase); return NodeState.Success;
                                    }
                                    return NodeState.Running;
                                }),
                                new WaitNode(1500),
                                new ActionNode(() => {
                                    MoveToOneShot(_playerBase, -3732.33f, 934.51f, 161.73f);
                                    return NodeState.Success;
                                }),
                                new WaitNode(5000)
                            ),
                            // 3. 没到门口，在菲拉斯无脑跑专属路线，绝不会卡接驳线！
                            new Sequence(
                                new ConditionNode(() => _mapId == 1),
                                new SmartPathNode(this, () => _hProcess, () => _playerBase, route_CorpseRun_Instance_East, false, false)
                            )
                        )
                    ),

                    // 【野外死亡专属逻辑】 (找真尸体、接驳线防卡死 + 超级国道动态分流)
                    new Sequence(
                        new ConditionNode(() => {
                            if ((DateTime.Now - _releaseSpiritTime).TotalSeconds < 5) return false;
                            return !IsCorpseInInstance(_hProcess, -3908.03f, 1130.0f) && _hasReleasedSpirit && HasGhostDebuff(_hProcess, _playerBase);
                        }),
                        new Selector(
                            // 1. 到达骨头堆复活！
                            new Sequence(
                                new ConditionNode(() => GetDistanceToCorpse(_hProcess, _playerBase) <= 10.0f),
                                new ActionNode(() => {
                                    StopMovement(_hProcess, _playerBase);
                                    Logger.Write($"[{CurrentPlayerName}] [野外跑尸] 到达骨头堆，尝试复活...");
                                    ExecuteDynamicLua("RetrieveCorpse()");
                                    return NodeState.Success;
                                }),
                                new WaitNode(3000)
                            ),

                            // 2. 尸体12码内脱离马路盲扑
                            new Sequence(
                                new ConditionNode(() => {
                                    float cx = MemoryAPI.ReadFloat(_hProcess, ModuleBaseAddress + 0x74E284);
                                    // 核心拦截：尸体坐标一旦清零，绝不允许盲扑！
                                    if (cx == 0) return false;
                                    return GetDistanceToCorpse(_hProcess, _playerBase) <= 12.0f;
                                }),
                                new ActionNode(() => {
                                    float cx = MemoryAPI.ReadFloat(_hProcess, ModuleBaseAddress + 0x74E284);
                                    float cy = MemoryAPI.ReadFloat(_hProcess, ModuleBaseAddress + 0x74E288);
                                    float cz = MemoryAPI.ReadFloat(_hProcess, ModuleBaseAddress + 0x74E28C);

                                    // 防止动作节点死锁：如果被强行锁进来了，发现是 0,0,0，立刻返回 Failure 打断死循环！
                                    if (cx == 0 && cy == 0) return NodeState.Failure;

                                    Logger.Write($"[{CurrentPlayerName}] [跑尸系统] 正在向真实尸体盲扑: X={cx}, Y={cy}");
                                    if (MoveTo(_hProcess, _playerBase, cx, cy, cz, 0.5f)) return NodeState.Success;
                                    return NodeState.Running;
                                })
                            ),

                            // 3. 墓地接驳线 —— 双阵营智能分流
                            new Selector(
                                // ================= 部落接驳线分流 =================
                                new Sequence(
                                    new ConditionNode(() => IsHordePlayer(false, false, _hProcess, _playerBase)),
                                    new Selector(
                                        new Sequence(
                                            new ConditionNode(() => {
                                                if (_activeGyRoute == -1) return false;
                                                if (_activeGyRoute == 4) return true;
                                                if (GetDistanceToCoords(_hProcess, _playerBase, -4439.97f, 370.15f, 51.36f) < 50.0f) { _activeGyRoute = 4; return true; }    //莫沙彻墓地
                                                return false;
                                            }),
                                            // 加一句日志，心里有底！
                                            new ActionNode(() => {
                                                Logger.Write($"[{CurrentPlayerName}] [跑尸系统] 灵魂在莫沙彻墓地降生，启动 部落 - 专属接驳线！");
                                                return NodeState.Success;
                                            }),
                                            new SmartPathNode(this, () => _hProcess, () => _playerBase, route_GY_Mojache_Horde, false, false),
                                            new ActionNode(() => { _activeGyRoute = -1; return NodeState.Success; })
                                        ),
                                        new Sequence(
                                            new ConditionNode(() => {
                                                if (_activeGyRoute == -1) return false;
                                                if (_activeGyRoute == 5) return true;
                                                if (GetDistanceToCoords(_hProcess, _playerBase, -4590.41f, 1632.08f, 93.97f) < 50.0f) { _activeGyRoute = 5; return true; }    //双塔山墓地
                                                return false;
                                            }),
                                            // 加一句日志，心里有底！
                                            new ActionNode(() => {
                                                Logger.Write($"[{CurrentPlayerName}] [跑尸系统] 灵魂在双塔山墓地降生，启动 部落 - 专属接驳线！");
                                                return NodeState.Success;
                                            }),
                                            new SmartPathNode(this, () => _hProcess, () => _playerBase, route_GY_TwinColossals_Horde, false, false),
                                            new ActionNode(() => { _activeGyRoute = -1; return NodeState.Success; })
                                        )
                                    )
                                ),

                                // ================= 联盟接驳线分流 =================
                                new Sequence(
                                    new ConditionNode(() => !IsHordePlayer(false, false, _hProcess, _playerBase)),
                                    new Selector(
                                        new Sequence(
                                            new ConditionNode(() => {
                                                if (_activeGyRoute == -1) return false;
                                                if (_activeGyRoute == 1) return true;
                                                if (GetDistanceToCoords(_hProcess, _playerBase, -4596.40f, 3229.43f, 8.99f) < 50.0f) { _activeGyRoute = 1; return true; }    //羽月要塞墓地
                                                return false;
                                            }),
                                            // 加一句日志，心里有底！
                                            new ActionNode(() => {
                                                Logger.Write($"[{CurrentPlayerName}] [跑尸系统] 灵魂在羽月要塞墓地降生，启动 联盟 - 专属接驳线！");
                                                return NodeState.Success;
                                            }),
                                            new SmartPathNode(this, () => _hProcess, () => _playerBase, route_GY_Feathermoon_Alliance, false, false),
                                            new ActionNode(() => { _activeGyRoute = -1; return NodeState.Success; })
                                        ),
                                        new Sequence(
                                            new ConditionNode(() => {
                                                if (_activeGyRoute == -1) return false;
                                                if (_activeGyRoute == 2) return true;
                                                if (GetDistanceToCoords(_hProcess, _playerBase, -4590.41f, 1632.08f, 93.97f) < 50.0f) { _activeGyRoute = 2; return true; }    //双塔山墓地
                                                return false;
                                            }),
                                            // 加一句日志，心里有底！
                                            new ActionNode(() => {
                                                Logger.Write($"[{CurrentPlayerName}] [跑尸系统] 灵魂在双塔山墓地降生，启动 联盟 - 专属接驳线！");
                                                return NodeState.Success;
                                            }),
                                            new SmartPathNode(this, () => _hProcess, () => _playerBase, route_GY_TwinColossals_Alliance, false, false),
                                            new ActionNode(() => { _activeGyRoute = -1; return NodeState.Success; })
                                        ),
                                        new Sequence(
                                            new ConditionNode(() => {
                                                if (_activeGyRoute == -1) return false;
                                                if (_activeGyRoute == 3) return true;
                                                if (GetDistanceToCoords(_hProcess, _playerBase, -4439.97f, 370.15f, 51.36f) < 50.0f) { _activeGyRoute = 3; return true; }    //莫沙彻墓地
                                                return false;
                                            }),
                                            // 加一句日志，心里有底！
                                            new ActionNode(() => {
                                                Logger.Write($"[{CurrentPlayerName}] [跑尸系统] 灵魂在莫沙彻墓地降生，启动 联盟 - 专属接驳线！");
                                                return NodeState.Success;
                                            }),
                                            new SmartPathNode(this, () => _hProcess, () => _playerBase, route_GY_Mojache_Alliance, false, false),
                                            new ActionNode(() => { _activeGyRoute = -1; return NodeState.Success; })
                                        )
                                    )
                                )
                            ), // 结束墓地接驳线

                            // 4. 野外主干道智能寻路 (开启倒车雷达) —— 6路动态并网
                            new Selector(
                                // ================== 部落雷达路线 ==================
                                new Sequence(
                                    // 场景 A：纯野外死亡，使用原版基础主干道
                                    new ConditionNode(() => IsHordePlayer(false, false, _hProcess, _playerBase) && !IsCorpseInDireMaulArea(_hProcess)),
                                    new ActionNode(() => {
                                        Logger.Write($"[{CurrentPlayerName}] [跑尸系统] 侦测到野外死亡，锁定 部落->常规主干道！");
                                        return NodeState.Success;
                                    }),
                                    new SmartPathNode(this, () => _hProcess, () => _playerBase, route_MasterCorpseRoad_Horde, true, false)
                                ),
                                new Sequence(
                                    // 部落跑尸 + 尸体在厄运西
                                    new ConditionNode(() => IsHordePlayer(false, false, _hProcess, _playerBase) && IsCorpseInWest(_hProcess)),
                                    new ActionNode(() => {
                                        Logger.Write($"[{CurrentPlayerName}] [跑尸系统] 锁定 部落->厄运西 动态干线！");
                                        return NodeState.Success;
                                    }),
                                    new SmartPathNode(this, () => _hProcess, () => _playerBase, route_MasterCorpseRoad_Horde_West, true, false)
                                ),
                                new Sequence(
                                    // 部落跑尸 + 尸体在厄运东
                                    new ConditionNode(() => IsHordePlayer(false, false, _hProcess, _playerBase) && !IsCorpseInWest(_hProcess)),
                                    new ActionNode(() => {
                                        Logger.Write($"[{CurrentPlayerName}] [跑尸系统] 锁定 部落->厄运东 动态干线！");
                                        return NodeState.Success;
                                    }),
                                    new SmartPathNode(this, () => _hProcess, () => _playerBase, route_MasterCorpseRoad_Horde_East, true, false)
                                ),

                                // ================== 联盟雷达路线 ==================
                                new Sequence(
                                    // 场景 A：纯野外死亡，使用原版基础主干道
                                    new ConditionNode(() => !IsHordePlayer(false, false, _hProcess, _playerBase) && !IsCorpseInDireMaulArea(_hProcess)),
                                    new ActionNode(() => {
                                        Logger.Write($"[{CurrentPlayerName}] [跑尸系统] 侦测到野外死亡，锁定 联盟->常规主干道！");
                                        return NodeState.Success;
                                    }),
                                    new SmartPathNode(this, () => _hProcess, () => _playerBase, route_MasterCorpseRoad_Alliance, true, false)
                                ),
                                new Sequence(
                                    // 联盟跑尸 + 尸体在厄运西
                                    new ConditionNode(() => !IsHordePlayer(false, false, _hProcess, _playerBase) && IsCorpseInWest(_hProcess)),
                                    new ActionNode(() => {
                                        Logger.Write($"[{CurrentPlayerName}] [跑尸系统] 锁定 联盟->厄运西 动态干线！");
                                        return NodeState.Success;
                                    }),
                                    new SmartPathNode(this, () => _hProcess, () => _playerBase, route_MasterCorpseRoad_Alliance_West, true, false)
                                ),
                                new Sequence(
                                    // 联盟跑尸 + 尸体在厄运东
                                    new ConditionNode(() => !IsHordePlayer(false, false, _hProcess, _playerBase) && !IsCorpseInWest(_hProcess)),
                                    new ActionNode(() => {
                                        Logger.Write($"[{CurrentPlayerName}] [跑尸系统] 锁定 联盟->厄运东 动态干线！");
                                        return NodeState.Success;
                                    }),
                                    new SmartPathNode(this, () => _hProcess, () => _playerBase, route_MasterCorpseRoad_Alliance_East, true, false)
                                )
                            )
                        )
                    )
                )
            );


            _rootTree = new Selector(
                // 优先级 1：只要是死人，不管三七二十一，先跑尸复活！
                corpseRunBranch,

                // 优先级 2：冷启动野外最高优先级防猝死上马套盾分支
                new Sequence(
                    new ConditionNode(() =>
                    {
                        if (!_needStartupPrep) return false;
                        if (_isDead) return false;
                        if (_mapId == 429) return false;
                        // 如果在船只上，绝对不上马
                        if (IsOnTransport(_hProcess, _playerBase)) return false;
                        // 如果正在战斗中，绝对不上马
                        if (Unit_Behavioral_State(_hProcess, _playerBase, 19)) return false;
                        /*
                        // 联盟专属水域拦截（游泳时无法上马）
                        if (!IsHordePlayer(false, false, _hProcess, _playerBase))
                        {
                            float startY = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                            //return y >= 2340f && y < 3110f;
                            if (startY >= 2340f && startY < 3110f)
                            {
                                _needStartupPrep = false;
                                Logger.Write($"[{CurrentPlayerName}] [冷启动保护] 角色在海域中上线，直接走路。");
                                return false;
                            }
                        }
                        */
                        return true;
                    }),

                    new ActionNode(() =>
                    {
                        Logger.Write($"[{CurrentPlayerName}] [冷启动保护] 触发野外上线保护！重踩物理手刹，准备上盾上马...");
                        StopMovement(_hProcess, _playerBase); // 物理手刹防滑步
                        return NodeState.Success;
                    }),

                    // 核心：执行你的双盾上马状态机节点
                    new PreDungeonShieldNode(this, () => _hProcess, () => _playerBase),

                    //  只要上马节点返回 Success（不论上马成败），立刻在这里关闭开关，终结此分支
                    new ActionNode(() =>
                    {
                        _needStartupPrep = false;
                        Logger.Write($"[{CurrentPlayerName}] [冷启动保护] 首次野外上马套盾尝试完毕！正式把控制权移交给主寻路模块！");
                        return NodeState.Success;
                    })
                ),

                // 优先级 2.1：触发条件：必须是乌龟服 && 已购买修理机器人 && 未购买随身邮箱 && 刷满20次
                /*
                new Sequence(
                    // 1. 条件判定节点
                    new ConditionNode(() =>
                    {
                        
                        string buildVersion = MemoryAPI.ReadString(_hProcess, ModuleBaseAddress + 0x437BFC, 16).Trim();
                        if (buildVersion != "7272") return false;

                        bool hasRobot = Query_known_skills(_hProcess, 46457);
                        bool hasMailbox = Query_known_skills(_hProcess, 46001);

                        if (!hasRobot || hasMailbox) return false;
                        
                        if (_mapId == 429) return false;
                        return _Dungeon_attempts_Count >= 15;
                    }),

                    new ActionNode(() =>
                    {
                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 触发强制回城：乌龟服(有商人/无邮箱)，已刷 {_Dungeon_attempts_Count} 次！");
                        // 不在副本（含：刚出本 / 本来就在外）
                        _needGoHome = true;
                        _needStartupPrep = false;
                        _attemptingTurtleBot = false;
                        _Dungeon_attempts_Count = 0;
                        return NodeState.Success; // 父节点接管
                    })
                ),
                */


                // 优先级 3：自救回城路线（严格限制：必须活着，且包满了，且【人已经安全到了野外 Map=1】！）
                new Sequence(
                    new ConditionNode(() => _isDead == false && _needGoHome == true && _mapId == 1),

                    // 【阵营分流道岔】
                    new Selector(
                        new Sequence(new ConditionNode(() => IsHordePlayer(false, false, _hProcess, _playerBase)), _runToInnTree_Horde),
                        new Sequence(new ConditionNode(() => !IsHordePlayer(false, false, _hProcess, _playerBase)), _runToInnTree_Alliance)
                    )
                ),

                // 优先级 4：包空着，在野外/旅店，刚修完装备要出门打工
                new Sequence(
                    new ConditionNode(() => _isDead == false && _needGoHome == false && _mapId == 1 && MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8) < -1000f),

                    // 【阵营分流道岔】
                    new Selector(
                        new Sequence(new ConditionNode(() => IsHordePlayer(false, false, _hProcess, _playerBase)), _runToInstanceTree_Horde),
                        new Sequence(new ConditionNode(() => !IsHordePlayer(false, false, _hProcess, _playerBase)), _runToInstanceTree_Alliance)
                    )
                ),

                // 优先级 4.5：【乌龟服特权插队】—— 只要需要清包，且活人，强行拦截！
                TurtleRepairBranch,

                // 优先级 5：终极目标 -> 只要人在副本里，哪怕包满了（但炉石在CD），也必须无条件给我把本刷完！
                new Sequence(
                    // 核心修正：去掉了 _needGoHome == false 的限制。只要满足在副本内 (Map=429 或 坐标>-1000)，就无条件进副本战斗树！
                    new ConditionNode(() => _isDead == false && (_mapId == 429 || MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8) > -1000f)),

                    _farmTree // 严格执行厄运之槌全场刷花逻辑！
                )
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
                if (_isTransitioning) _transitionFailsafe = DateTime.Now.AddSeconds(15);
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

                // 【核心精髓】：瞬间重新开锁！
                // 将其变回 true。这样如果在这 5 秒缓冲期内突然又进蓝条了，上面的物理锁依然能完美拦截。
                _wasInWorld = true;
            }

            // 3. 5 秒非阻塞保护期拦截
            if (_isWaitingForWorldLoad)
            {
                // 如果当前时间还没到我们设定的 5 秒后
                if (DateTime.Now < _resumeTimeAfterLoad)
                {
                    // 核心：像上面一样返回字符串，终止当前 Tick 的执行！
                    // 既不卡 UI，又保证在这 5 秒内，行为树和寻路打怪绝对不会提前运行！
                    return "场景缓冲中";
                }
                else
                {
                    // 5 秒时间到了，执行真正的清空与重置动作
                    Logger.Write($"[{CurrentPlayerName}] [智能检测] 缓冲完毕，已自动清空行为树历史记录防错乱！");

                    // [在这里加入解锁代码]
                    _hasLoggedFreeze = false;
                    _hasLoggedRadar = false;
                    _hasLoggedTeleport = false;

                    _needStartupPrep = false;
                    _hasAttemptedSell = false;
                    isSellingInitialized = false;
                    _rootTree?.Reset(); // 强行把所有 SmartPathNode 的进度全部归零！
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

            // 启动时检测！
            if (playerBase != 0)
            {
                int currentClassId = GetPlayerClass(_hProcess, _playerBase);
                int currentLevel = GetPlayerLevel(_hProcess, _playerBase);

                // 1. 职业拦截：不是猎人 (3)
                if (currentClassId > 0 && currentClassId != 3)
                {
                    if ((DateTime.Now - _lastClassWarnTime).TotalSeconds >= 5)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [职业拦截] 当前脚本为猎人(ID 3)专属！检测到非法职业ID: {currentClassId}，脚本逻辑已锁定！");
                        _lastClassWarnTime = DateTime.Now;
                    }
                    return "请使用猎人职业";
                }

                // 2. 等级校验：至少60级才能跑
                if (currentLevel <= 0 || currentLevel < 60)
                {
                    if ((DateTime.Now - _lastLevelWarnTime).TotalSeconds >= 5)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [等级拦截] 脚本要求至少60级猎人，当前等级: {currentLevel}，脚本逻辑已锁定！");
                        _lastLevelWarnTime = DateTime.Now;
                    }
                    return "需要60级猎人";
                }
            }
            // 【刷本号代码】
            AccountConfig config = ConfigManager.GetConfig(this.AccountName) ?? new AccountConfig();
            if (config != null && !string.IsNullOrEmpty(config.TeamName))
            {
                if ((DateTime.Now - _lastAcceptTime).TotalSeconds > 10)
                {
                    _teamName = config.TeamName; // 注意：这里的 TeamName 配置的应该是【门外队长号】的名字

                    // 刷本号逻辑：拒绝陌生人邀请 -> 自己没人组就邀请队长 -> 进组后改自由拾取 -> 移交队长 -> 发现不对劲退组
                    string finalLua = $@"
            local t='{_teamName}';
            local s='PARTY_INVITE';
            if StaticPopup_Visible(s) then DeclineGroup(); StaticPopup_Hide(s); end;
            local m=GetNumPartyMembers();
            if m==0 then 
                InviteByName(t);
            else 
                local f,u=0,nil;
                for i=1,m do if UnitName('party'..i)==t then f=1; u='party'..i; end; end;
                if f==0 then 
                    LeaveParty(); 
                elseif IsPartyLeader() then 
                    if GetLootMethod()~='freeforall' then SetLootMethod('freeforall'); end;
                    if u then PromoteToPartyLeader(u); end;
                end;
            end;
        ";

                    // 移除多余换行
                    finalLua = finalLua.Replace("\r", "").Replace("\n", " ");

                    // 全局只调用一次！
                    ExecuteDynamicLua(finalLua);
                    _lastAcceptTime = DateTime.Now;
                }
            }

            // 2. 采集当前环境数据
            int descriptors = MemoryAPI.ReadInteger(_hProcess, _playerBase + 0x08);
            int currentHp = MemoryAPI.ReadInteger(_hProcess, descriptors + 0x58);
            int maxHp = MemoryAPI.ReadInteger(_hProcess, descriptors + 0x70);
            _mapId = MemoryAPI.ReadInteger(_hProcess, ModuleBaseAddress + 0x46A2CC);
            bool isInCombat = Unit_Behavioral_State(_hProcess, _playerBase, 19);
            float playerX = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
            float playerY = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);

            //获取冻结术ID  9454  永久定身31366
            bool isfreeze = HasUnitAura(_hProcess, _playerBase, 9454, 31366);

            if (isfreeze)
            {
                // 日志防刷屏锁
                if (!_hasLoggedFreeze)
                {
                    //被冻结
                    Logger.Write($"[{CurrentPlayerName}] [安全防护] 警报！检测到角色被冻结，触发 GM 防护！");
                    _hasLoggedFreeze = true;
                }

                // 告诉全局登录服务不要上线
                this.PauseGlobalAutoLogin = true;
                // 建议：除了挂起脚本，最好发一个小退指令，防止 GM 在原地观察你
                //ExecuteDynamicLua("Logout()");
                return "已被GM冻结";  //脚本逻辑暂停
            }

            // ==========================================
            // --- GM 雷达与瞬移防护系统 ---
            // ==========================================
            // 过滤条件：排除刚启动、过蓝条期间、以及死亡状态
            if (!_isFirstStartup && !_isWaitingForWorldLoad && !_isDead)
            {
                // ------------------------------------------
                // 防护网 1：内存活体雷达 (检测隐身 GM)
                // ------------------------------------------
                // 如果在副本内 (429)，理论上周围除了你绝对不该有任何人！
                if (_mapId == 429)
                {
                    int nearbyPlayers = GetNearbyOtherPlayerCount(_hProcess, _playerBase);

                    // 既然排除了自己，只要大于等于 1 就可以报警了 (如果是野外你需要提高阈值)
                    if (nearbyPlayers >= 1)
                    {
                        // 日志防刷屏锁
                        if (!_hasLoggedRadar)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [安全防护] 警报！副本内检测到 {nearbyPlayers} 名外来玩家实体(极大概率是隐身GM)！");
                            _hasLoggedRadar = true;
                        }

                        // 告诉全局登录服务不要上线
                        this.PauseGlobalAutoLogin = true;
                        // 紧急切断：原地小退并锁定脚本
                        StopMovement(_hProcess, _playerBase);
                        ExecuteDynamicLua("Logout()");
                        return "侦测到异常玩家";
                    }
                }

                // ------------------------------------------
                // 防护网 2：物理位移检测 (检测 GM 强拉瞬移)
                // ------------------------------------------
                if (DateTime.Now > _hearthstoneTimer.AddSeconds(5))
                {
                    // 获取当前帧是否在船上
                    bool isCurrentlyOnTransport = IsOnTransport(_hProcess, _playerBase);

                    // 核心修复：只有当“当前不在船上” 且 “上一帧也不在船上” 时，才进行位移计算！
                    // 这完美过滤了上船期间的相对坐标，以及下船那一瞬间的坐标突变。
                    if (!isCurrentlyOnTransport && !_lastTickOnTransport)
                    {
                        if (_mapId == _lastTickMapId && _lastTickX != 0f && _lastTickY != 0f)
                        {
                            double distance = Math.Sqrt(Math.Pow(playerX - _lastTickX, 2) + Math.Pow(playerY - _lastTickY, 2));

                            // 法师闪现极限 20 码，超过 35 码判定为非自然移动
                            if (distance > 35.0)
                            {
                                // 日志防刷屏锁
                                if (!_hasLoggedTeleport)
                                {
                                    Logger.Write($"[{CurrentPlayerName}] [安全防护] 警报！检测到角色异常瞬移 (瞬间移动 {distance:F1} 码)，触发 GM 防护！");
                                    _hasLoggedTeleport = true;
                                }

                                // 告诉全局登录服务不要上线
                                this.PauseGlobalAutoLogin = true;
                                StopMovement(_hProcess, _playerBase);
                                ExecuteDynamicLua("Logout()");
                                return "已被GM传送";
                            }
                        }
                    }
                }
            }

            // ==========================================
            // 在 OnTick 的末尾 (或 return 之前)，更新历史坐标留给下一帧用
            // ==========================================
            _lastTickX = playerX;
            _lastTickY = playerY;
            _lastTickMapId = _mapId;
            _lastTickOnTransport = IsOnTransport(_hProcess, _playerBase); // 记录这一帧的乘船状态

            // 只在脚本刚启动的瞬间执行一次
            if (_isFirstStartup)
            {
                if (currentHp <= 1 && maxHp > 0)
                {
                    Logger.Write($"[{CurrentPlayerName}] [冷启动检测] 发现角色处于死亡或灵魂状态，强制启动跑尸保护！");
                    _isDead = true;
                    _needStartupPrep = false;
                    if (currentHp == 1) _hasReleasedSpirit = true;
                }

                float startX = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                float startY = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);

                bool isAllianceInInn = !IsHordePlayer(false, false, _hProcess, _playerBase) && _mapId == 1 && startY >= 3260f;
                bool isHordeInInn = IsHordePlayer(false, false, _hProcess, _playerBase) && _mapId == 1 && startX < -4430f && startY < 260f;

                if (!_isDead && (isAllianceInInn || isHordeInInn))
                {
                    Logger.Write($"[{CurrentPlayerName}] [冷启动检测] 发现角色在旅店内部启动，强制触发一轮清包与补给流程！");
                    _needGoHome = true;
                    _needStartupPrep = false;
                }
                else if (!_isDead)
                {
                    Logger.Write($"[{CurrentPlayerName}] [冷启动检测] 角色在野外或副本中启动，继续当前任务，不强制回城。");

                    if (_mapId != 429)
                    {
                        // 1. 动态抓取当前精确坐标
                        float currentX = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                        float currentY = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC); // Y轴留着，以备后续打日志或扩展使用

                        // 2. 走廊绝对结界判定（使用极其稳定的 X 轴盒子判定）
                        // 厄运西走廊：X 轴在 -3835.0f 到 -3815.31f 之间
                        bool inWestCorridor = currentX >= -3835.0f && currentX <= -3815.31f;

                        // 厄运东走廊：X 轴在 -3768.0f 到 -3730.0f 之间 (注意负数大小关系，-3768更小)
                        bool inEastCorridor = currentX >= -3768.0f && currentX <= -3730.0f;

                        // 3. 门禁与走廊拦截判定
                        if (inWestCorridor || inEastCorridor)
                        {
                            _needStartupPrep = false; // 坚决关掉上马开关！

                            string corridorName = inWestCorridor ? "厄运西走廊" : "厄运东走廊";
                            Logger.Write($"[{CurrentPlayerName}] [冷启动检测] 侦测到角色在{corridorName}，直接走进去，拒绝原地读条上马！");
                        }
                        else
                        {
                            // 4. 只有在脱离了走廊结界的空旷区域，才允许上马补给
                            _needStartupPrep = true;
                            //_startupPrepTimer = DateTime.Now.AddSeconds(12); // 给 12 秒防卡死沙盒时间
                        }
                    }
                }
                _isFirstStartup = false;
            }


            // ==========================================
            // --- 乘船系统：OnTick 实时雷达代码 ---
            // ==========================================
            // 定义菲拉斯双码头雷达触发的“虚拟电子围栏”（刚好涵盖两个码头）
            float feralasMinX = -5065.0f;
            float feralasMaxX = -3990.0f;
            float feralasMinY = 2210.0f;
            float feralasMaxY = 3920.0f;

            // 判断条件：在卡利姆多(MapID=1) 且 坐标落在电子围栏内
            bool isInFeralasRadarZone = (_mapId == 1) &&
                                        (playerX >= feralasMinX && playerX <= feralasMaxX) &&
                                        (playerY >= feralasMinY && playerY <= feralasMaxY);

            if (isInFeralasRadarZone)
            {
                // ------------------------------------------
                // 追踪雷达A：羽月要塞(码头)
                // ------------------------------------------
                bool isFeatherDocked = IsFeathermoonShipDocked(_hProcess);
                if (isFeatherDocked && !_isFeathermoonDocked)
                {
                    _feathermoonDockTime = DateTime.Now;
                    _isFeathermoonDocked = true;

                    if (!_wasInFeralasRadarZone)
                    {
                        _isFeathermoonTimeKnown = false; // 刚进范围就看到船，时间未知
                        Logger.Write($"[{CurrentPlayerName}] [全局雷达] 角色刚进入雷达区，船只已到达【羽月要塞】码头，停留时间未知！");
                    }
                    else
                    {
                        _isFeathermoonTimeKnown = true;  // 看着船靠岸，时间已知
                        Logger.Write($"[{CurrentPlayerName}] [全局雷达] 船只已到达【羽月要塞】码头，开始 60 秒停留倒计时！");
                    }
                }
                else if (!isFeatherDocked && _isFeathermoonDocked)
                {
                    _isFeathermoonDocked = false;
                    Logger.Write($"[{CurrentPlayerName}] [全局雷达] 船只已离开【羽月要塞】码头。");
                }

                // ------------------------------------------
                // 追踪雷达B：被遗忘的海岸(码头)
                // ------------------------------------------
                bool isMainlandDocked = IsShipAtDestination(_hProcess);
                if (isMainlandDocked && !_isMainlandDocked)
                {
                    _mainlandDockTime = DateTime.Now;
                    _isMainlandDocked = true;

                    if (!_wasInFeralasRadarZone)
                    {
                        _isMainlandTimeKnown = false; // 刚进范围就看到船，时间未知
                        Logger.Write($"[{CurrentPlayerName}] [全局雷达] 角色刚进入雷达区，船只已到达【被遗忘的海岸】码头，停留时间未知！");
                    }
                    else
                    {
                        _isMainlandTimeKnown = true;  // 看着船靠岸，时间已知
                        Logger.Write($"[{CurrentPlayerName}] [全局雷达] 船只已到达【被遗忘的海岸】码头，开始 60 秒停留倒计时！");
                    }
                }
                else if (!isMainlandDocked && _isMainlandDocked)
                {
                    _isMainlandDocked = false;
                    Logger.Write($"[{CurrentPlayerName}] [全局雷达] 船只已离开【被遗忘的海岸】码头。");
                }
            }
            else
            {
                // 如果离开围栏，雷达拉闸断电，状态全部清空
                if (_isFeathermoonDocked) _isFeathermoonDocked = false;
                if (_isMainlandDocked) _isMainlandDocked = false;
            }

            // 记录当前帧状态，供下一帧对比（必须放在雷达逻辑最后）
            _wasInFeralasRadarZone = isInFeralasRadarZone;



            // 基于底层地图切换的物理重置检测 
            if (_lastMapId != 0 && _mapId != 0 && _mapId != _lastMapId)
            {
                // 情况 A：从副本 (429) 跨越到 野外 (1)
                if (_lastMapId == 429 && _mapId == 1)
                {
                    // 核心分流：判断是“活着出来”还是“死着出来”
                    if (_isDead || _hasReleasedSpirit)
                    {
                        // 只有死亡状态下出本，才是释放灵魂导致的跑尸出本，需要物理重置！
                        Logger.Write($"[{CurrentPlayerName}] [过图检测] 侦测到角色【死亡跨出】副本，已在墓地降生，即将呼叫队长重置！");

                        // 在这里赋值是最完美的，它 100% 保证了你已经经历完蓝条并站在了墓地
                        _needResetInstance = true;
                        _resetTimer = DateTime.Now.AddSeconds(4);
                    }
                    else
                    {
                        // 活着出本（包括走出门、小退导致的回城传送），一律放行，不触发重置
                        Logger.Write($"[{CurrentPlayerName}] [过图检测] 侦测到角色活着离开副本(走出门或小退重上)");
                        // 🌟 核心修复：主线程权威介入！
                        // 既然都活蹦乱跳在野外了，直接强行擦除小退留下的 IPC 信号，解放队长！
                        if (IPCManager.IsResetRequestPending(_teamName) || IPCManager.IsResetCompleted(_teamName))
                        {
                            Logger.Write($"[{CurrentPlayerName}] [小退系统] 主线程已确认降生主世界！强行擦除 IPC 信号，允许队长接组！");
                            IPCManager.ClearDeathSignals(_teamName);
                            this.PauseGlobalAutoLogin = false; // 权限归还给全局
                        }
                    }

                    _rootTree?.Reset();
                }
                // 情况 B：从野外 (1) 跨越到 副本 (429)
                else if (_lastMapId == 1 && _mapId == 429)
                {
                    Logger.Write($"[{CurrentPlayerName}] [过图检测] 侦测到角色进入副本");
                    _hasAttemptedSell = false;
                    StopMovement(hProcess, playerBase);
                    _rootTree?.Reset();
                }
            }

            if (_mapId != 0) _lastMapId = _mapId;

            // 【强制重置执行区】
            if (_needResetInstance)
            {
                // 修复：使用 IsDeathResetCompleted 查询状态，而不是 MarkDeathResetComplete
                if (!IPCManager.IsDeathResetRequestPending(_teamName) && !IPCManager.IsDeathResetCompleted(_teamName))
                {
                    // 1. 发送“重置副本”请求给队长
                    IPCManager.RequestDeathReset(_teamName);
                    Logger.Write($"[{CurrentPlayerName}] [副本重置] 呼叫队长进行重置");
                    return "等待队长重置";
                }
                else if (IPCManager.IsDeathResetRequestPending(_teamName) && !IPCManager.IsDeathResetCompleted(_teamName))
                {
                    // 2. 信号发出了，队长还在干活，我就原地发呆等
                    return "等待队长重置";
                }
                else if (IPCManager.IsDeathResetCompleted(_teamName))
                {
                    // 3. 队长发来贺电：副本已重置完毕！
                    _needResetInstance = false;

                    // 擦除死亡重置信号，完成闭环
                    IPCManager.ClearDeathSignals(_teamName);

                    Logger.Write($"[{CurrentPlayerName}] [副本重置] 队长已完成重置！准备向副本门口进发！");

                    // 返回后续动作，比如启动你的“跑尸进本”行为树节点
                    return "重置完成，准备跑尸";
                }
            }

            // 清理干净的终极死亡判定
            bool previousDeadState = _isDead;

            if (currentHp > 1)
            {
                _isDead = false; // 活人绝对法则
            }
            else
            {
                _isDead = (currentHp <= 0) || (_hasReleasedSpirit && currentHp <= 1) || HasGhostDebuff(_hProcess, _playerBase);

                // 高危拦截器现在被安全地关在 else 里面，绝不反杀活人！
                if (_hasReleasedSpirit && !_isDead && _mapId == 1 && GetDistanceToCorpse(_hProcess, _playerBase) > 20.0f)
                {
                    _isDead = true;
                }
            }

            if (previousDeadState != _isDead)
            {
                string status = _isDead ? "死亡/灵魂" : "存活";
                Debug.WriteLine($"\n[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{CurrentPlayerName}] [生命体征监控] 状态发生反转! 当前状态: 【{status}】 | 血量: {currentHp}/{maxHp} | 释放灵魂: {_hasReleasedSpirit}");

                // 全局死亡最高优先级中断机制
                // 只要捕捉到从“存活”变为“死亡”的瞬间 (下降沿触发)
                if (_isDead && !previousDeadState)
                {
                    Logger.Write($"[{CurrentPlayerName}] [全局中断] 确认阵亡！立即清空所有指令与行为树缓存状态！");

                    // 1. 踩下物理手刹，强行终止寻路惯性
                    StopMovement(_hProcess, _playerBase);

                    // 2. 状态机变量彻底初始化
                    _hasReleasedSpirit = false;
                    _activeGyRoute = 0; // 重置墓地接驳线锁
                    _needGoHome = false; // 人都死了，清什么包，归零！

                    // 3. 核心：强制抹除行为树的当前进度 (打断 WaitNode 和各种 Sequence)
                    // 这样下一帧 OnTick 重新 Evaluate 时，就会直接无视之前的进度，强行切入跑尸分支！
                    _rootTree?.Reset();
                }
            }

            //检测从死到活的瞬间
            if (_wasDead && !_isDead)
            {
                float revX = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                float revY = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);

                Debug.WriteLine($"\n=======================================================");
                Logger.Write($"[{CurrentPlayerName}] [重生鉴定] 玩家复活！");
                Logger.Write($"[{CurrentPlayerName}] [重生鉴定] 唤醒坐标: X={revX}, Y={revY}");
                Logger.Write($"[{CurrentPlayerName}] [重生鉴定] 背包满载标志 (_needGoHome): {_needGoHome}");
                Logger.Write($"[{CurrentPlayerName}] [重生鉴定] 正在强行抹除 CTM 移动残留与行为树历史...");

                StopMovement(_hProcess, _playerBase);
                _hasReleasedSpirit = false;
                _activeGyRoute = 0;
                WaitTimer = DateTime.MinValue;
                _rootTree?.Reset(); // 只要这里执行了，下一帧去上班绝对会打印日志！

                Logger.Write($"[{CurrentPlayerName}] [重生鉴定] 系统重置完毕，下一帧将由最高指挥官重新分配任务！");
                Debug.WriteLine($"=======================================================\n");
            }
            _wasDead = _isDead;

            // 【炉石读条动态监控保护锁】 (防打断)
            if (DateTime.Now < _hearthstoneTimer)
            {
                // 计算距离按下炉石过去了多久
                double elapsedSeconds = 11.0 - (_hearthstoneTimer - DateTime.Now).TotalSeconds;

                // 核心监控期：1.5秒 到 9.5秒 
                // (前 1.5 秒给宏执行和网络延迟留起手时间，最后 1.5 秒可能已经读完出蓝条了，防误报)
                if (elapsedSeconds > 1.5 && elapsedSeconds < 9.5)
                {
                    // 动态读取内存：当前正在施放的法术 ID
                    int currentCastingSpellId = MemoryAPI.ReadInteger(_hProcess, _playerBase + 0xC8C);

                    // 炉石的 SpellID 是 8690
                    if (currentCastingSpellId != 8690)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [背包系统] 侦测到炉石读条被打断！(当前施法ID: {currentCastingSpellId}) 立即重试...");

                        StopMovement(_hProcess, _playerBase); // 再次重踩物理手刹，抹除一切残留惯性
                        _hearthstoneTimer = DateTime.MinValue; // 瞬间解除 11 秒发呆锁！让系统下一帧立刻重跑搓炉石逻辑！
                    }
                }
                return "正在炉石";
            }

            // 1. 先读取一次当前背包状态
            bool isBagFull = CheckIfInventoryFull(_hProcess, _playerBase);

            // 2. 【新增解锁逻辑】：如果背包已经有空间了，且没有在执行清包流程，则释放触发锁！
            if (!isBagFull && _hasAttemptedSell && !_attemptingTurtleBot && !_needGoHome)
            {
                _hasAttemptedSell = false;
                Logger.Write($"[{CurrentPlayerName}] [背包系统] 背包空位已恢复，重置清包触发器，允许副本内二次清包！");
            }

            // 背包状态持续监听中心 CheckIfInventoryFull 如果剩余空位小于等于 5，立刻返回 true！
            if (!_isDead && !_needGoHome && !_attemptingTurtleBot && !_hasAttemptedSell && CheckIfInventoryFull(_hProcess, _playerBase))
            {
                string buildVersion = MemoryAPI.ReadString(_hProcess, ModuleBaseAddress + 0x437BFC, 16).Trim();

                if (buildVersion == "7272")
                {
                    Logger.Write($"[{CurrentPlayerName}] [背包系统] 侦测到背包空位极低（剩余<=5格），启动Turtle WoW清包机制！");
                    _attemptingTurtleBot = true; // 激活乌龟服专属售卖分支
                }
                else
                {
                    Logger.Write($"[{CurrentPlayerName}] [背包系统] 侦测到背包空位极低（剩余<=5格），启动原版客户端清包机制！");
                    _needGoHome = true; // 保持原有的回城逻辑
                }
                
            }

            // 【背包已满 · 动态智能回城中控分流器】
            if (!_isDead && _needGoHome)
            {
                bool hasHearthstone = GetItemCount(_hProcess, 6948) > 0;
                bool isHsReady = hasHearthstone && !IsSpellOnCooldown(_hProcess, 6948);

                float currentX = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                float currentY = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                bool isAlreadyInInn = false;

                if (_mapId == 1)
                {
                    if (IsHordePlayer(false, false, _hProcess, _playerBase)) isAlreadyInInn = (currentX < -4430f && currentY < 260f);
                    else isAlreadyInInn = (currentY >= 3260f);
                }

                if (isHsReady && !isAlreadyInInn)
                {
                    if (_mapId == 429 && isInCombat)
                    {
                        if ((DateTime.Now - _lastZijiuPrintTime).TotalSeconds >= 5)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [背包系统] 炉石已就绪，但当前处于战斗状态！暂不打断，请冷静把这波怪打完");
                            _lastZijiuPrintTime = DateTime.Now;
                        }
                    }
                    else
                    {
                        // 新增：坐骑检测与下马逻辑
                        if (IsMounted(_hProcess, _playerBase))
                        {
                            Logger.Write($"[{CurrentPlayerName}] [背包系统] 准备搓炉石，侦测到当前处于骑乘状态，执行紧急下马！");
                            StopMovement(_hProcess, _playerBase); // 必须先停稳，防止移动中下马产生滑行惯性
                            _ = Use_Mount(50); // 模拟按 0 键下马

                            // 核心技巧：立刻 return 交出控制权！
                            // 不要在这里 Sleep！让下一帧 OnTick 重新进来。
                            // 几十毫秒后下一帧进来时，内存里的坐骑模型ID已经变成0，自然就会跳过这个if，去搓炉石。
                            return "下马准备炉石";
                        }

                        Logger.Write($"[{CurrentPlayerName}] [背包系统] 脱战安全期或野外，满足炉石条件！紧急打断，开搓炉石！");
                        StopMovement(_hProcess, _playerBase);
                        UseItemByItemId(_hProcess, 6948);  //使用炉石
                        _hearthstoneTimer = DateTime.Now.AddSeconds(11);
                        _rootTree?.Reset();
                        return "正在炉石";
                    }
                }
                else
                {
                    if (_mapId == 429)
                    {
                        // 战斗状态分流保护机制
                        if (isInCombat)
                        {
                            // 如果还在战斗中，只打印日志，不执行小退！
                            if ((DateTime.Now - _lastZijiuPrintTime).TotalSeconds >= 5)
                            {
                                Logger.Write($"[{CurrentPlayerName}] [背包系统] 准备小退重置，但当前处于战斗状态！暂不打断，请把这波怪彻底清完脱战...");
                                _lastZijiuPrintTime = DateTime.Now;
                            }
                            // 极其关键：这里绝对不能 return！
                            // 不 return，代码才会顺畅地流转到你下方的战斗逻辑中，让机器人把怪反击打死。
                        }
                        else
                        {
                            // 【脱战安全期】正式启动小退重置流程

                            // 【终极防线 1】如果信号已挂起，直接锁死当前帧
                            if (IPCManager.IsResetRequestPending(_teamName))
                            {
                                return "正在小退";
                            }

                            Logger.Write($"[{CurrentPlayerName}] [背包系统] 成功脱战！背包满且炉石CD，直接启动小退重置副本流程！");

                            // 小退前踩死刹车，防止移动打断小退读条（虽然副本外秒退，但副本内往往有20秒倒计时）
                            StopMovement(hProcess, playerBase);

                            this.PauseGlobalAutoLogin = true; // 告诉 Main.cs：接下来没基址的日子，由我全权接管！

                            // 1. 发送 IPC 信号，通知队长
                            IPCManager.RequestReset(_teamName);

                            // 2. 执行小退指令
                            ExecuteDynamicLua("Logout()");

                            // 注入“不死不休”的唤醒间谍线程 (OnTick 适配版)
                            Task.Run(async () => {
                                Logger.Write($"[{CurrentPlayerName}] [小退系统] 子线程已创建：等待队长离队信号");

                                // 阶段 A：死等队长的完成信号
                                while (!IPCManager.IsResetCompleted(_teamName))
                                {
                                    await Task.Delay(500);
                                }
                                Logger.Write($"[{CurrentPlayerName}] [小退系统] 子线程收到队长信号！正在确认角色是否已退出世界");

                                // 阶段 B：【终极防线 2：防止误开聊天框】
                                while (IsInWorld(_hProcess))
                                {
                                    await Task.Delay(500);
                                }

                                Logger.Write($"[{CurrentPlayerName}] [小退系统] 角色已完全小退，尝试上线！");

                                // 阶段 C：【终极防线 3：暴力对抗客户端卡死】
                                while (true)
                                {
                                    // 1. IsInWorld(_hProcess) 读内存成功 (传统检测)
                                    // 2. 【全局蓝条检测器】已经起作用，清除了 IPC 信号 (协同检测)
                                    // 3. 背包回城状态 _needGoHome 已经被重置 (协同检测)
                                    // 只要满足任意一条，说明角色肯定已经在游戏里活蹦乱跳了，间谍立刻自我销毁！
                                    // 建议改成这样，代码可读性和安全性更高：
                                    if (IsInWorld(_hProcess) || (!IPCManager.IsResetRequestPending(_teamName) && !IPCManager.IsResetCompleted(_teamName)) || !_needGoHome)
                                    {
                                        Logger.Write($"[{CurrentPlayerName}] [小退系统] 角色已上线，子线程任务圆满完成并自动销毁！");

                                        // 兜底擦除黑板
                                        IPCManager.ClearSignals(_teamName);
                                        _rootTree?.Reset();
                                        // 🌟 核心：权限交还给全局服务，以防后续真掉线了没人管！
                                        this.PauseGlobalAutoLogin = false;
                                        break; // 刺客功成身退，终止死循环
                                    }

                                    // 【低频动作】：虽然每 100 毫秒检查一次，但只有距离上次敲回车超过 8 秒，才会再敲一次！
                                    if ((DateTime.Now - lastEnterWorldTime).TotalSeconds >= 8)
                                    {
                                        ExecuteDynamicLua("EnterWorld()");
                                        lastEnterWorldTime = DateTime.Now;
                                    }

                                    // 每次循环只让出 100 毫秒的控制权，不吃 CPU，且反应极快
                                    await Task.Delay(100);
                                }
                            });

                            // 触发小退指令后，彻底接管并锁死底层行为树
                            return "正在小退";
                        }
                    }
                }
            }


            // 检查当前时间是否已经达到了设定的Total防作弊重置时间
            if (DateTime.Now >= _nextAntiBotTime)
            {
                // 3. 核心修改：如果不在厄运之槌(_mapId != 429)，或者当前不在战斗中(!isInCombat)，则允许平移
                bool canStrafe = (_mapId != 429) || !isInCombat;

                if (canStrafe)
                {
                    // 4. 时间到了，且符合平移条件，执行随机平移
                    int randomDirection = _antiBotRnd.Next(0, 2); // 随机生成 0 或 1

                    // 动态生成日志文本，方便你观察是哪种情况触发的
                    string triggerReason = (_mapId != 429 && isInCombat) ? "野外战斗强制" : "脱战安全";

                    if (randomDirection == 0)
                    {
                        // 向左平移
                        _ = Q_keyboard(1);
                        Logger.Write($"[{CurrentPlayerName}] [防检测系统] 执行{triggerReason}清零 MovementAnticheat Total：向左平移");
                    }
                    else
                    {
                        // 向右平移 
                        _ = E_keyboard(1);
                        Logger.Write($"[{CurrentPlayerName}] [防检测系统] 执行{triggerReason}清零 MovementAnticheat Total：向右平移");
                    }

                    // 5. 关键步：重新生成下一次的触发时间
                    // （注：既然你修复了野外超时的问题，这里的 3~4 分钟已经很安全了。如果你想更激进，可以改成上文建议的 90000, 150000）
                    int nextIntervalMs = _antiBotRnd.Next(180000, 240000);
                    _nextAntiBotTime = DateTime.Now.AddMilliseconds(nextIntervalMs);

                    Logger.Write($"[{CurrentPlayerName}] [防检测系统] 下一次 MovementAnticheat Total 清零将在 {nextIntervalMs / 1000} 秒后");
                }
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