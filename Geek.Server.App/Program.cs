using Geek.Server.App.Common;
using Geek.Server.Core.Storage;
using Geek.Server.Core.Utils;
using Geek.Server.Proto;
using NLog;
using System.Diagnostics;
using System.Text;

namespace Geek.Server.App
{
    class Program
    {
        // NLog日志记录器，用于记录日志信息
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        // 用于管理游戏主循环任务
        private static volatile Task GameLoopTask = null;

        // 用于管理服务器的关闭任务
        private static volatile Task ShutDownTask = null;

        // 程序的主入口方法，使用async以支持异步操作
        static async Task Main(string[] args)
        {
            try
            {
                // 初始化应用程序退出处理程序，并传入退出处理方法
                AppExitHandler.Init(HandleExit);

                // 启动游戏主循环任务，AppStartUp.Enter方法应返回一个Task
                GameLoopTask = AppStartUp.Enter();

                // 等待游戏主循环任务完成
                await GameLoopTask;

                // 如果关闭任务不为空，等待关闭任务完成
                if (ShutDownTask != null)
                    await ShutDownTask;
            }
            catch (Exception e)
            {
                // 捕获运行过程中发生的异常
                string error;
                if (Settings.AppRunning)
                {
                    // 如果服务器正在运行，记录并显示异常信息
                    error = $"服务器运行时异常 e:{e}";
                    Console.WriteLine(error);

                    // 将异常信息写入文件server_error.txt
                    File.WriteAllText("server_error.txt", $"{e}", Encoding.UTF8);
                } 
            }
        }

        // 处理程序退出的方法
        private static void HandleExit()
        {
            // 记录程序即将退出的日志信息
            Log.Info($"监听到退出程序消息");

            // 启动一个新的任务来处理服务器的关闭过程
            ShutDownTask = Task.Run(() =>
            {
                // 设置应用程序运行状态为false
                Settings.AppRunning = false;

                // 等待游戏主循环任务完成
                GameLoopTask?.Wait();

                // 关闭日志记录器
                LogManager.Shutdown();

                // 执行自定义的应用程序退出处理逻辑
                AppExitHandler.Kill();

                // 在控制台输出退出程序的信息
                Console.WriteLine($"退出程序");

                // 强制终止当前进程
                Process.GetCurrentProcess().Kill(true);
            });

            // 等待关闭任务完成
            ShutDownTask.Wait();
        }
    }
}
