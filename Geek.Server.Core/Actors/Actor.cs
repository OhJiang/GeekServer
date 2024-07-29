using System.Collections.Concurrent;
using Geek.Server.Core.Actors.Impl;
using Geek.Server.Core.Comps;
using Geek.Server.Core.Hotfix.Agent;
using Geek.Server.Core.Timer;

namespace Geek.Server.Core.Actors
{
    /// <summary>
    /// Actor 类用于管理各类组件及其代理，提供异步执行任务和组件生命周期管理功能。
    /// </summary>
    sealed public class Actor
    {
        // 用于记录日志的 NLog Logger 实例
        private static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

        // 存储组件实例的字典
        private readonly ConcurrentDictionary<Type, BaseComp> compDic = new();

        // Actor 的唯一标识
        public long Id { get; init; }

        // Actor 的类型
        public ActorType Type { get; init; }

        // 用于处理工作任务的 WorkerActor 实例
        public WorkerActor WorkerActor { get; init; }

        // 自动回收标志
        public bool AutoRecycle { get; private set; } = false;

        // 存储调度 ID 的集合
        public HashSet<long> ScheduleIdSet = new();

        /// <summary>
        /// 设置自动回收标志的值。
        /// </summary>
        /// <param name="autoRecycle">新的自动回收标志值。</param>
        public void SetAutoRecycle(bool autoRecycle)
        {
            Tell(() =>
            {
                AutoRecycle = autoRecycle;
            });
        }

        /// <summary>
        /// 获取指定类型的组件代理。
        /// </summary>
        /// <typeparam name="T">组件代理的类型。</typeparam>
        /// <returns>组件代理实例。</returns>
        public async Task<T> GetCompAgent<T>() where T : ICompAgent
        {
            return (T)await GetCompAgent(typeof(T));
        }

        /// <summary>
        /// 获取指定类型的组件代理。
        /// </summary>
        /// <param name="agentType">组件代理的类型。</param>
        /// <returns>组件代理实例。</returns>
        public async Task<ICompAgent> GetCompAgent(Type agentType)
        {
            // 获取组件的实际类型
            var compType = agentType.BaseType.GetGenericArguments()[0];
            // 从字典中获取组件实例，如果不存在则创建新的组件实例
            var comp = compDic.GetOrAdd(compType, k => CompRegister.NewComp(this, k));
            // 获取组件代理实例
            var agent = comp.GetAgent(agentType);
            // 如果组件未激活，则激活组件和代理
            if (!comp.IsActive)
            {
                await SendAsyncWithoutCheck(async () =>
                {
                    await comp.Active();
                    agent.Active();
                });
            }
            return agent;
        }

        // 超时时间常量
        public const int TIME_OUT = int.MaxValue;

        /// <summary>
        /// 构造函数，初始化 Actor 实例。
        /// </summary>
        /// <param name="id">Actor 的唯一标识。</param>
        /// <param name="type">Actor 的类型。</param>
        public Actor(long id, ActorType type)
        {
            Id = id;
            Type = type;
            WorkerActor = new(id);

            if (type == ActorType.Role)
            {
                Tell(() => SetAutoRecycle(true));
            }
            else
            {
                Tell(() => CompRegister.ActiveComps(this));
            }
        }

        /// <summary>
        /// 跨天处理逻辑。
        /// </summary>
        /// <param name="openServerDay">开服天数。</param>
        /// <returns>异步任务。</returns>
        public async Task CrossDay(int openServerDay)
        {
            Log.Debug($"actor跨天 id:{Id} type:{Type}");
            foreach (var comp in compDic.Values)
            {
                var agent = comp.GetAgent();
                if (agent is ICrossDay crossDay)
                {
                    // 使用 try-catch 缩小异常影响范围
                    try
                    {
                        await crossDay.OnCrossDay(openServerDay);
                    }
                    catch (Exception e)
                    {
                        Log.Error($"{agent.GetType().FullName}跨天失败 actorId:{Id} actorType:{Type} 异常：\n{e}");
                    }
                }
            }
        }

        // 判断 Actor 是否准备好去激活
        internal bool ReadyToDeactive => compDic.Values.All(item => item.ReadyToDeactive);

        /// <summary>
        /// 保存所有组件的状态。
        /// </summary>
        /// <returns>异步任务。</returns>
        internal async Task SaveAllState()
        {
            foreach (var item in compDic)
            {
                await item.Value.SaveState();
            }
        }

        /// <summary>
        /// 解除所有组件的激活状态。
        /// </summary>
        /// <returns>异步任务。</returns>
        public async Task Deactive()
        {
            foreach (var item in compDic.Values)
            {
                await item.Deactive();
            }
        }

        #region Actor 入队

        /// <summary>
        /// 将工作任务入队并异步执行。
        /// </summary>
        /// <param name="work">要执行的工作任务。</param>
        /// <param name="timeout">超时时间。</param>
        public void Tell(Action work, int timeout = TIME_OUT)
        {
            WorkerActor.Tell(work, timeout);
        }

        /// <summary>
        /// 将异步工作任务入队并异步执行。
        /// </summary>
        /// <param name="work">要执行的异步工作任务。</param>
        /// <param name="timeout">超时时间。</param>
        public void Tell(Func<Task> work, int timeout = TIME_OUT)
        {
            WorkerActor.Tell(work, timeout);
        }

        /// <summary>
        /// 将工作任务入队并异步执行，返回执行结果。
        /// </summary>
        /// <param name="work">要执行的工作任务。</param>
        /// <param name="timeout">超时时间。</param>
        /// <returns>异步任务。</returns>
        public Task SendAsync(Action work, int timeout = TIME_OUT)
        {
            return WorkerActor.SendAsync(work, timeout);
        }

        /// <summary>
        /// 将工作任务入队并异步执行，返回执行结果。
        /// </summary>
        /// <typeparam name="T">工作任务的返回类型。</typeparam>
        /// <param name="work">要执行的工作任务。</param>
        /// <param name="timeout">超时时间。</param>
        /// <returns>异步任务，返回执行结果。</returns>
        public Task<T> SendAsync<T>(Func<T> work, int timeout = TIME_OUT)
        {
            return WorkerActor.SendAsync(work, timeout);
        }

        /// <summary>
        /// 将异步工作任务入队并异步执行。
        /// </summary>
        /// <param name="work">要执行的异步工作任务。</param>
        /// <param name="timeout">超时时间。</param>
        /// <returns>异步任务。</returns>
        public Task SendAsync(Func<Task> work, int timeout = TIME_OUT)
        {
            return WorkerActor.SendAsync(work, timeout);
        }

        /// <summary>
        /// 将异步工作任务入队并异步执行，不进行检查。
        /// </summary>
        /// <param name="work">要执行的异步工作任务。</param>
        /// <param name="timeout">超时时间。</param>
        /// <returns>异步任务。</returns>
        public Task SendAsyncWithoutCheck(Func<Task> work, int timeout = TIME_OUT)
        {
            return WorkerActor.SendAsync(work, timeout, false);
        }

        /// <summary>
        /// 将异步工作任务入队并异步执行，返回执行结果。
        /// </summary>
        /// <typeparam name="T">工作任务的返回类型。</typeparam>
        /// <param name="work">要执行的异步工作任务。</param>
        /// <param name="timeout">超时时间。</param>
        /// <returns>异步任务，返回执行结果。</returns>
        public Task<T> SendAsync<T>(Func<Task<T>> work, int timeout = TIME_OUT)
        {
            return WorkerActor.SendAsync(work, timeout);
        }

        #endregion

        /// <summary>
        /// 返回表示当前对象的字符串。
        /// </summary>
        /// <returns>表示当前对象的字符串。</returns>
        public override string ToString()
        {
            return $"{base.ToString()}_{Type}_{Id}";
        }

        /// <summary>
        /// 清除所有组件的代理缓存。
        /// </summary>
        public void ClearAgent()
        {
            foreach (var comp in compDic.Values)
            {
                comp.ClearCacheAgent();
            }
        }
    }
}
