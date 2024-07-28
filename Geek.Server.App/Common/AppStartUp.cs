using Geek.Server.Core.Actors.Impl;
using Geek.Server.Core.Comps;
using Geek.Server.Core.Hotfix;
using Geek.Server.Core.Storage;
using Geek.Server.Proto;
using NLog;
using NLog.Config;
using PolymorphicMessagePack;

namespace Geek.Server.App.Common
{
    internal class AppStartUp
    {
        // NLog日志记录器，用于记录日志信息
        static readonly Logger Log = LogManager.GetCurrentClassLogger();

        // 服务器启动入口方法，使用async以支持异步操作
        public static async Task Enter()
        {
            try
            {
                // 调用Start方法，初始化服务器配置并注册必要的类型和映射
                var flag = Start();
                if (!flag) return; // 如果服务器启动失败，直接返回

                // 记录启动嵌入式数据库的日志信息
                Log.Info($"launch embedded db...");
                // 初始化Actor限制规则
                ActorLimit.Init(ActorLimit.RuleType.None);
                // 初始化并打开游戏数据库
                GameDB.Init();
                GameDB.Open();
                // 记录组件注册的日志信息
                Log.Info($"regist comps...");
                // 异步初始化组件注册
                await CompRegister.Init();
                // 记录加载热更新模块的日志信息
                Log.Info($"load hotfix module");
                // 异步加载热更新模块
                await HotfixMgr.LoadHotfixModule();

                // 记录进入游戏主循环的日志信息
                Log.Info("进入游戏主循环...");
                Console.WriteLine("***进入游戏主循环***");
                // 设置启动时间和应用程序运行状态
                Settings.LauchTime = DateTime.Now;
                Settings.AppRunning = true;

                // 等待应用程序退出令牌的完成
                await Settings.AppExitToken;
            }
            catch (Exception e)
            {
                // 捕获运行过程中发生的异常并记录日志
                Console.WriteLine($"服务器执行异常，e:{e}");
                Log.Fatal(e);
            }

            // 记录退出服务器的日志信息
            Console.WriteLine($"退出服务器开始");
            // 异步停止热更新管理器
            await HotfixMgr.Stop();
            Console.WriteLine($"退出服务器成功");
        }

        // 服务器启动方法，返回是否成功的布尔值
        private static bool Start()
        {
            try
            {
                // 加载服务器配置
                Settings.Load<AppSetting>("Configs/app_config.json", ServerType.Game);
                Console.WriteLine("init NLog config...");
                // 初始化NLog配置
                LogManager.Setup().SetupExtensions(s => s.RegisterConditionMethod("logState", (e) => Settings.IsDebug ? "debug" : "release"));
                LogManager.Configuration = new XmlLoggingConfiguration("Configs/app_log.config");
                LogManager.AutoShutdown = false;

                // 注册多态类型映射
                PolymorphicTypeMapper.Register(typeof(AppStartUp).Assembly); // app
                PolymorphicRegister.Load();
                PolymorphicResolver.Instance.Init(); 

                // mongodb bson类型映射
                BsonClassMapHelper.SetConvention();
                BsonClassMapHelper.RegisterAllClass(typeof(ReqLogin).Assembly);
                BsonClassMapHelper.RegisterAllClass(typeof(Program).Assembly);

                return true; // 启动成功
            }
            catch (Exception e)
            {
                // 捕获启动过程中发生的异常并记录日志
                Log.Error($"启动服务器失败,异常:{e}");
                return false; // 启动失败
            }
        }
    }
}
