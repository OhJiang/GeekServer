namespace Geek.Server.Core.Actors.Impl
{
    /// <summary>
    /// 抽象的工作封装类，提供任务执行和上下文管理的基础结构。
    /// </summary>
    public abstract class WorkWrapper
    {
        /// <summary>
        /// 获取或设置所属的 WorkerActor。
        /// </summary>
        public WorkerActor Owner { get; set; }

        /// <summary>
        /// 获取或设置任务的超时时间。
        /// </summary>
        public int TimeOut { get; set; }

        /// <summary>
        /// 执行任务的抽象方法，子类需要实现具体的任务执行逻辑。
        /// </summary>
        public abstract Task DoTask();

        /// <summary>
        /// 获取任务的跟踪信息的抽象方法，子类需要实现具体的跟踪信息。
        /// </summary>
        public abstract string GetTrace();

        /// <summary>
        /// 强制设置任务结果的抽象方法，子类需要实现具体的逻辑。
        /// </summary>
        public abstract void ForceSetResult();

        /// <summary>
        /// 获取或设置调用链 ID，用于跟踪任务执行链。
        /// </summary>
        public long CallChainId { get; set; }

        /// <summary>
        /// 设置任务执行的上下文。
        /// </summary>
        protected void SetContext()
        {
            RuntimeContext.SetContext(CallChainId, Owner.Id);
            Owner.CurChainId = CallChainId;
        }

        /// <summary>
        /// 重置任务执行的上下文。
        /// </summary>
        public void ResetContext()
        {
            Owner.CurChainId = 0;
        }
    }

    /// <summary>
    /// 同步任务的工作封装类，不带返回值。
    /// </summary>
    public class ActionWrapper : WorkWrapper
    {
        static readonly NLog.Logger LOGGER = NLog.LogManager.GetCurrentClassLogger();

        /// <summary>
        /// 获取要执行的任务 (Action)。
        /// </summary>
        public Action Work { private set; get; }

        /// <summary>
        /// 获取任务完成通知的 TaskCompletionSource。
        /// </summary>
        public TaskCompletionSource<bool> Tcs { private set; get; }

        /// <summary>
        /// 初始化 ActionWrapper 实例。
        /// </summary>
        /// <param name="work">要执行的任务 (Action)。</param>
        public ActionWrapper(Action work)
        {
            Work = work;
            Tcs = new TaskCompletionSource<bool>();
        }

        /// <summary>
        /// 执行任务并设置结果。
        /// </summary>
        public override Task DoTask()
        {
            try
            {
                SetContext();
                Work();
            }
            catch (Exception e)
            {
                LOGGER.Error(e.ToString());
            }
            finally
            {
                ResetContext();
                Tcs.TrySetResult(true);
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// 获取任务的跟踪信息。
        /// </summary>
        public override string GetTrace()
        {
            return Work.Target + "|" + Work.Method.Name;
        }

        /// <summary>
        /// 强制设置任务结果并重置上下文。
        /// </summary>
        public override void ForceSetResult()
        {
            ResetContext();
            Tcs.TrySetResult(false);
        }
    }

    /// <summary>
    /// 同步任务的工作封装类，带返回值。
    /// </summary>
    /// <typeparam name="T">任务返回值的类型。</typeparam>
    public class FuncWrapper<T> : WorkWrapper
    {
        static readonly NLog.Logger LOGGER = NLog.LogManager.GetCurrentClassLogger();

        /// <summary>
        /// 获取要执行的任务 (Func)。
        /// </summary>
        public Func<T> Work { private set; get; }

        /// <summary>
        /// 获取任务完成通知的 TaskCompletionSource。
        /// </summary>
        public TaskCompletionSource<T> Tcs { private set; get; }

        /// <summary>
        /// 初始化 FuncWrapper 实例。
        /// </summary>
        /// <param name="work">要执行的任务 (Func)。</param>
        public FuncWrapper(Func<T> work)
        {
            Work = work;
            Tcs = new TaskCompletionSource<T>();
        }

        /// <summary>
        /// 执行任务并设置结果。
        /// </summary>
        public override Task DoTask()
        {
            T ret = default;
            try
            {
                SetContext();
                ret = Work();
            }
            catch (Exception e)
            {
                LOGGER.Error(e.ToString());
            }
            finally
            {
                ResetContext();
                Tcs.TrySetResult(ret);
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// 获取任务的跟踪信息。
        /// </summary>
        public override string GetTrace()
        {
            return Work.Target + "|" + Work.Method.Name;
        }

        /// <summary>
        /// 强制设置任务结果并重置上下文。
        /// </summary>
        public override void ForceSetResult()
        {
            ResetContext();
            Tcs.TrySetResult(default);
        }
    }

    /// <summary>
    /// 异步任务的工作封装类，不带返回值。
    /// </summary>
    public class ActionAsyncWrapper : WorkWrapper
    {
        static readonly NLog.Logger LOGGER = NLog.LogManager.GetCurrentClassLogger();

        /// <summary>
        /// 获取要执行的任务 (Func<Task>)。
        /// </summary>
        public Func<Task> Work { private set; get; }

        /// <summary>
        /// 获取任务完成通知的 TaskCompletionSource。
        /// </summary>
        public TaskCompletionSource<bool> Tcs { private set; get; }

        /// <summary>
        /// 初始化 ActionAsyncWrapper 实例。
        /// </summary>
        /// <param name="work">要执行的任务 (Func<Task>)。</param>
        public ActionAsyncWrapper(Func<Task> work)
        {
            Work = work;
            Tcs = new TaskCompletionSource<bool>();
        }

        /// <summary>
        /// 异步执行任务并设置结果。
        /// </summary>
        public override async Task DoTask()
        {
            try
            {
                SetContext();
                await Work();
            }
            catch (Exception e)
            {
                LOGGER.Error(e.ToString());
            }
            finally
            {
                ResetContext();
                Tcs.TrySetResult(true);
            }
        }

        /// <summary>
        /// 获取任务的跟踪信息。
        /// </summary>
        public override string GetTrace()
        {
            return Work.Target + "|" + Work.Method.Name;
        }

        /// <summary>
        /// 强制设置任务结果并重置上下文。
        /// </summary>
        public override void ForceSetResult()
        {
            ResetContext();
            Tcs.TrySetResult(false);
        }
    }

    /// <summary>
    /// 异步任务的工作封装类，带返回值。
    /// </summary>
    /// <typeparam name="T">任务返回值的类型。</typeparam>
    public class FuncAsyncWrapper<T> : WorkWrapper
    {
        static readonly NLog.Logger LOGGER = NLog.LogManager.GetCurrentClassLogger();

        /// <summary>
        /// 获取要执行的任务 (Func<Task<T>>)。
        /// </summary>
        public Func<Task<T>> Work { private set; get; }

        /// <summary>
        /// 获取任务完成通知的 TaskCompletionSource。
        /// </summary>
        public TaskCompletionSource<T> Tcs { private set; get; }

        /// <summary>
        /// 初始化 FuncAsyncWrapper 实例。
        /// </summary>
        /// <param name="work">要执行的任务 (Func<Task<T>>)。</param>
        public FuncAsyncWrapper(Func<Task<T>> work)
        {
            Work = work;
            Tcs = new TaskCompletionSource<T>();
        }

        /// <summary>
        /// 异步执行任务并设置结果。
        /// </summary>
        public override async Task DoTask()
        {
            T ret = default;
            try
            {
                SetContext();
                ret = await Work();
            }
            catch (Exception e)
            {
                LOGGER.Error(e.ToString());
            }
            finally
            {
                ResetContext();
                Tcs.TrySetResult(ret);
            }
        }

        /// <summary>
        /// 获取任务的跟踪信息。
        /// </summary>
        public override string GetTrace()
        {
            return Work.Target + "|" + Work.Method.Name;
        }

        /// <summary>
        /// 强制设置任务结果并重置上下文。
        /// </summary>
        public override void ForceSetResult()
        {
            ResetContext();
            Tcs.TrySetResult(default);
        }
    }
}
