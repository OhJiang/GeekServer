using System.Reflection;
using Geek.Server.Core.Actors;
using Geek.Server.Core.Hotfix;
using Geek.Server.Core.Hotfix.Agent;
using Geek.Server.Core.Utils;
using NLog;

namespace Geek.Server.Core.Comps
{
    public static class CompRegister
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// ActorType 映射到 CompType 的字典，用于存储每种 ActorType 对应的组件类型集合。
        /// </summary>
        private static readonly Dictionary<ActorType, HashSet<Type>> ActorCompDic = new();

        /// <summary>
        /// CompType 映射到 ActorType 的字典，用于存储每个组件类型对应的 ActorType。
        /// </summary>
        internal static readonly Dictionary<Type, ActorType> CompActorDic = new();

        /// <summary>
        /// 功能 ID 映射到 CompType 的字典，用于存储每种功能对应的组件类型集合。
        /// </summary>
        private static readonly Dictionary<int, HashSet<Type>> FuncCompDic = new();

        /// <summary>
        /// CompType 映射到 功能 ID 的字典，用于存储每个组件类型对应的功能 ID。
        /// </summary>
        private static readonly Dictionary<Type, short> CompFuncDic = new();

        /// <summary>
        /// 获取指定组件类型对应的 ActorType。
        /// </summary>
        /// <param name="compType">组件类型。</param>
        /// <returns>对应的 ActorType。</returns>
        public static ActorType GetActorType(Type compType)
        {
            CompActorDic.TryGetValue(compType, out var actorType);
            return actorType;
        }

        /// <summary>
        /// 获取指定 ActorType 对应的所有组件类型。
        /// </summary>
        /// <param name="actorType">Actor 类型。</param>
        /// <returns>对应的组件类型集合。</returns>
        public static IEnumerable<Type> GetComps(ActorType actorType)
        {
            ActorCompDic.TryGetValue(actorType, out var comps);
            return comps;
        }

        /// <summary>
        /// 初始化组件注册。
        /// </summary>
        /// <param name="assembly">要扫描的程序集，默认为入口程序集。</param>
        public static Task Init(Assembly assembly = null)
        {
            if (assembly == null)
                assembly = Assembly.GetEntryAssembly();
            Type baseCompName = typeof(BaseComp);

            // 遍历程序集中的所有类型，查找继承自 BaseComp 的具体实现类。
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || !type.IsSubclassOf(baseCompName))
                    continue;

                // 检查组件类型是否有 CompAttribute 特性，并进行注册。
                if (type.GetCustomAttribute(typeof(CompAttribute)) is CompAttribute compAttr)
                {
                    var actorType = compAttr.ActorType;
                    var compTypes = ActorCompDic.GetOrAdd(actorType);
                    compTypes.Add(type);

                    CompActorDic[type] = actorType;

                    if (actorType == ActorType.Role)
                    {
                        // 检查组件类型是否有 FuncAttribute 特性，并进行功能映射注册。
                        if (type.GetCustomAttribute(typeof(FuncAttribute)) is FuncAttribute funcAttr)
                        {
                            var set = FuncCompDic.GetOrAdd(funcAttr.func);
                            set.Add(type);
                            CompFuncDic[type] = funcAttr.func;
                        }
                    }
                }
                else
                {
                    throw new Exception($"comp:{type.FullName}未绑定actor类型");
                }
            }
            Log.Info($"初始化组件注册完成");
            return Task.CompletedTask;
        }

        /// <summary>
        /// 激活全局组件。
        /// </summary>
        public static async Task ActiveGlobalComps()
        {
            try
            {
                foreach (var kv in ActorCompDic)
                {
                    var actorType = kv.Key;
                    foreach (var compType in kv.Value)
                    {
                        var agentType = HotfixMgr.GetAgentType(compType);
                        if (agentType == null)
                        {
                            throw new Exception($"{compType}未实现agent");
                        }

                        // 根据需要激活全局组件。
                        // if (actorType > ActorType.Separator)
                        // {
                        //     Log.Info($"激活全局组件：{actorType} {compType}");
                        //     await ActorMgr.GetCompAgent(agentType, actorType);
                        // }
                    }
                    if (actorType > ActorType.Separator)
                    {
                        Log.Info($"激活全局Actor: {actorType}");
                        await ActorMgr.GetOrNew(IdGenerator.GetActorID(actorType));
                    }
                }
                Log.Info($"激活全局组件并检测组件是否都包含Agent实现完成");
            }
            catch (Exception)
            {
                Log.Error($"激活全局组件并检测组件是否都包含Agent实现失败");
                throw;
            }
        }

        /// <summary>
        /// 激活角色组件。
        /// </summary>
        /// <param name="compAgent">组件代理。</param>
        /// <param name="openFuncSet">已开启的功能集合。</param>
        public static Task ActiveRoleComps(ICompAgent compAgent, HashSet<short> openFuncSet)
        {
            return ActiveComps(compAgent.Owner.Actor,
                t => !CompFuncDic.TryGetValue(t, out var func)
                || openFuncSet.Contains(func));
            // foreach (var compType in GetComps(ActorType.Role))
            // {
            //     bool active;
            //     if (CompFuncDic.TryGetValue(compType, out var func))
            //     {
            //         active = openFuncSet.Contains(func);
            //     }
            //     else
            //     {
            //         active = true;
            //     }
            //     if (active)
            //     {
            //         var agentType = HotfixMgr.GetAgentType(compType);
            //         await compAgent.GetCompAgent(agentType);
            //     }
            // }
        }

        /// <summary>
        /// 激活指定 Actor 的组件。
        /// </summary>
        /// <param name="actor">Actor 实例。</param>
        /// <param name="predict">组件筛选条件。</param>
        internal static async Task ActiveComps(Actor actor, Func<Type, bool> predict = null)
        {
            foreach (var compType in GetComps(actor.Type))
            {
                if (predict == null || predict(compType))
                {
                    var agentType = HotfixMgr.GetAgentType(compType);
                    await actor.GetCompAgent(agentType);
                }
            }
        }

        /// <summary>
        /// 为指定的 Actor 创建新组件实例。
        /// </summary>
        /// <param name="actor">Actor 实例。</param>
        /// <param name="compType">组件类型。</param>
        /// <returns>创建的组件实例。</returns>
        internal static BaseComp NewComp(Actor actor, Type compType)
        {
            if (!ActorCompDic.TryGetValue(actor.Type, out var compTypes) || !compTypes.Contains(compType))
            {
                throw new Exception($"获取不属于此actor：{actor.Type}的comp:{compType.FullName}");
            }
            var comp = (BaseComp)Activator.CreateInstance(compType);
            comp.Actor = actor;
            return comp;
        }
    }
}
