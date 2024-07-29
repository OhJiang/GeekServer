using System.Threading.Tasks.Dataflow; // 提供数据流块的功能
using Geek.Server.Core.Utils; // 引入自定义实用程序
using NLog; // 引入 NLog 用于日志记录

namespace Geek.Server.Core.Actors.Impl
{
    public class WorkerActor
    {
        // 定义一个静态只读的日志记录器
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        // 当前调用链 ID
        internal long CurChainId { get; set; }

        // 当前 actor 的 ID，使用 init 修饰符来确保在初始化后不能更改
        internal long Id { get; init; }

        // 超时时间常量，单位为毫秒
        public const int TIME_OUT = 13000;

        // 构造函数
        public WorkerActor(long id = 0)
        {
            // 如果 ID 为 0，则生成一个唯一的 ID
            if (id == 0)
                id = IdGenerator.GetUniqueId(IDModule.WorkerActor);
            Id = id;

            // 初始化 ActionBlock，用于处理任务队列，确保任务串行执行（MaxDegreeOfParallelism = 1）
            ActionBlock = new ActionBlock<WorkWrapper>(InnerRun, new ExecutionDataflowBlockOptions() { MaxDegreeOfParallelism = 1 });
        }

        // 内部运行方法，用于在 ActionBlock 中执行任务
        private static async Task InnerRun(WorkWrapper wrapper)
        {
            var task = wrapper.DoTask();
            try
            {
                // 等待任务完成或超时
                await task.WaitAsync(TimeSpan.FromMilliseconds(wrapper.TimeOut));
            }
            catch (TimeoutException)
            {
                // 如果任务超时，记录日志并强制设置任务结果
                Log.Fatal("wrapper执行超时:" + wrapper.GetTrace());
                //强制设状态-取消该操作
                wrapper.ForceSetResult();
            }
        }

        // 定义 ActionBlock，用于处理 WorkWrapper 类型的任务
        private ActionBlock<WorkWrapper> ActionBlock { get; init; }

        /// <summary>
        /// chainId == 0说明是新的异步环境
        /// chainId相等说明是一直await下去的（一种特殊情况是自己入自己的队）
        /// </summary>
        /// <returns>是否需要加入队列，以及新的调用链 ID</returns>
        public (bool needEnqueue, long chainId) IsNeedEnqueue()
        {
            var chainId = RuntimeContext.CurChainId;
            bool needEnqueue = chainId == 0 || chainId != CurChainId;
            if (needEnqueue && chainId == 0) chainId = NextChainId();
            return (needEnqueue, chainId);
        }

        #region 勿调用(仅供代码生成器调用)
        // 将任务加入队列（Action 版本）
        public Task Enqueue(Action work, long callChainId, bool discard = false, int timeOut = TIME_OUT)
        {
            if (!discard && Settings.IsDebug && !ActorLimit.AllowCall(Id))
                return default;
            var at = new ActionWrapper(work)
            {
                Owner = this,
                TimeOut = timeOut,
                CallChainId = callChainId
            };
            ActionBlock.SendAsync(at);
            return at.Tcs.Task;
        }

        // 将任务加入队列（Func<T> 版本）
        public Task<T> Enqueue<T>(Func<T> work, long callChainId, bool discard = false, int timeOut = TIME_OUT)
        {
            if (!discard && Settings.IsDebug && !ActorLimit.AllowCall(Id))
                return default;
            var at = new FuncWrapper<T>(work)
            {
                Owner = this,
                TimeOut = timeOut,
                CallChainId = callChainId
            };
            ActionBlock.SendAsync(at);
            return at.Tcs.Task;
        }

        // 将任务加入队列（异步 Action 版本）
        public Task Enqueue(Func<Task> work, long callChainId, bool discard = false, int timeOut = TIME_OUT)
        {
            if (!discard && Settings.IsDebug && !ActorLimit.AllowCall(Id))
                return default;
            var at = new ActionAsyncWrapper(work)
            {
                Owner = this,
                TimeOut = timeOut,
                CallChainId = callChainId
            };
            ActionBlock.SendAsync(at);
            return at.Tcs.Task;
        }

        // 将任务加入队列（异步 Func<Task<T>> 版本）
        public Task<T> Enqueue<T>(Func<Task<T>> work, long callChainId, bool discard = false, int timeOut = TIME_OUT)
        {
            if (!discard && Settings.IsDebug && !ActorLimit.AllowCall(Id))
                return default;
            var at = new FuncAsyncWrapper<T>(work)
            {
                Owner = this,
                TimeOut = timeOut,
                CallChainId = callChainId
            };
            ActionBlock.SendAsync(at);
            return at.Tcs.Task;
        }
        #endregion

        #region 供框架底层调用(逻辑开发人员应尽量避免调用)
        // 将任务告知 actor 执行（Action 版本）
        public void Tell(Action work, int timeout = Actor.TIME_OUT)
        {
            var at = new ActionWrapper(work)
            {
                Owner = this,
                TimeOut = timeout,
                CallChainId = NextChainId(),
            };
            _ = ActionBlock.SendAsync(at);
        }

        // 将任务告知 actor 执行（异步 Action 版本）
        public void Tell(Func<Task> work, int timeout = Actor.TIME_OUT)
        {
            var wrapper = new ActionAsyncWrapper(work)
            {
                Owner = this,
                TimeOut = timeout,
                CallChainId = NextChainId(),
            };
            _ = ActionBlock.SendAsync(wrapper);
        }

        /// <summary>
        /// 调用该方法禁止丢弃 Task，丢弃 Task 请使用 Tell 方法
        /// </summary>
        // 异步发送任务（Action 版本）
        public Task SendAsync(Action work, int timeout = Actor.TIME_OUT)
        {
            (bool needEnqueue, long chainId) = IsNeedEnqueue();
            if (needEnqueue)
            {
                if (Settings.IsDebug && !ActorLimit.AllowCall(Id))
                    return default;

                var at = new ActionWrapper(work)
                {
                    Owner = this,
                    TimeOut = timeout,
                    CallChainId = chainId,
                };
                ActionBlock.SendAsync(at);
                return at.Tcs.Task;
            }
            else
            {
                work();
                return Task.CompletedTask;
            }
        }

        // 异步发送任务（Func<T> 版本）
        public Task<T> SendAsync<T>(Func<T> work, int timeout = Actor.TIME_OUT)
        {
            (bool needEnqueue, long chainId) = IsNeedEnqueue();
            if (needEnqueue)
            {
                if (Settings.IsDebug && !ActorLimit.AllowCall(Id))
                    return default;

                var at = new FuncWrapper<T>(work)
                {
                    Owner = this,
                    TimeOut = timeout,
                    CallChainId = chainId,
                };
                ActionBlock.SendAsync(at);
                return at.Tcs.Task;
            }
            else
            {
                return Task.FromResult(work());
            }
        }

        // 异步发送任务（异步 Func<Task> 版本）
        public Task SendAsync(Func<Task> work, int timeout = Actor.TIME_OUT, bool checkLock = true)
        {
            (bool needEnqueue, long chainId) = IsNeedEnqueue();
            if (needEnqueue)
            {
                if (checkLock && Settings.IsDebug && !ActorLimit.AllowCall(Id))
                    return default;

                var wrapper = new ActionAsyncWrapper(work)
                {
                    Owner = this,
                    TimeOut = timeout,
                    CallChainId = chainId,
                };
                ActionBlock.SendAsync(wrapper);
                return wrapper.Tcs.Task;
            }
            else
            {
                return work();
            }
        }

        // 异步发送任务（异步 Func<Task<T>> 版本）
        public Task<T> SendAsync<T>(Func<Task<T>> work, int timeout = Actor.TIME_OUT)
        {
            (bool needEnqueue, long chainId) = IsNeedEnqueue();
            if (needEnqueue)
            {
                if (Settings.IsDebug && !ActorLimit.AllowCall(Id))
                    return default;

                var wrapper = new FuncAsyncWrapper<T>(work)
                {
                    Owner = this,
                    TimeOut = timeout,
                    CallChainId = chainId,
                };
                ActionBlock.SendAsync(wrapper);
                return wrapper.Tcs.Task;
            }
            else
            {
                return work();
            }
        }
        #endregion

        // 静态链 ID，初始化为当前时间戳
        private static long chainId = DateTime.Now.Ticks;

        /// <summary>
        /// 生成下一个调用链 ID
        /// </summary>
        public static long NextChainId()
        {
            var id = Interlocked.Increment(ref chainId);
            if (id == 0)
            {
                id = Interlocked.Increment(ref chainId);
            }
            return id;
        }
    }
}
