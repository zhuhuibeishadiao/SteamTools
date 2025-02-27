using Avalonia.Controls.ApplicationLifetimes;
using System.Application.Services;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Windows;
using AvaloniaApplication = Avalonia.Application;

namespace System.Application.UI
{
    partial class Program
    {
        const string command_main = "main";
        static ApplicationInstance? appInstance;

        /// <summary>
        /// 命令行工具(Command Line Tools/CLT)
        /// </summary>
        static class CommandLineTools
        {
            public static int Run(string[] args,
                Action<DILevel> initStartup,
                Action initUIApp,
                Action initCef)
            {
                if (args.Length == 0) args = new[] { "-h" };

                // https://docs.microsoft.com/zh-cn/archive/msdn-magazine/2019/march/net-parse-the-command-line-with-system-commandline
                var rootCommand = new RootCommand("命令行工具(Command Line Tools/CLT)");

                void MainHandler()
                {
#if StartupTrace
                    StartupTrace.Restart("ProcessCheck");
#endif
                    initStartup(IsMainProcess ? DILevel.MainProcess : DILevel.Min);
#if StartupTrace
                    StartupTrace.Restart("Startup.Init");
#endif
                    if (IsMainProcess)
                    {
                        appInstance = new ApplicationInstance();
                        if (!appInstance.IsFirst)
                        {
                            //Console.WriteLine("ApplicationInstance.SendMessage(string.Empty);");
                            ApplicationInstance.SendMessage(string.Empty);
                            return;
                        }
                        appInstance.MessageReceived += value =>
                        {
                            if (string.IsNullOrEmpty(value))
                            {
                                var app = App.Instance;
                                if (app != null)
                                {
                                    MainThreadDesktop.BeginInvokeOnMainThread(app.RestoreMainWindow);
                                }
                            }
                        };
                    }
#if StartupTrace
                    StartupTrace.Restart("ApplicationInstance");
#endif
                    initCef();
#if StartupTrace
                    StartupTrace.Restart("InitCefNetApp");
#endif
                    if (IsMainProcess)
                    {
                        initUIApp();
                    }
#if StartupTrace
                    StartupTrace.Restart("InitAvaloniaApp");
#endif
                }
                void MainHandlerByCLT()
                {
                    IsMainProcess = true;
                    IsCLTProcess = false;
                    MainHandler();
                }

#if DEBUG
                // -clt debug -args 730
                var debug = new Command("debug", "调试");
                debug.AddOption(new Option<string>("-args", () => "", "测试参数"));
                debug.Handler = CommandHandler.Create((string args) => // 参数名与类型要与 Option 中一致！
                {
                    //Console.WriteLine("-clt debug -args " + args);
                    // OutputType WinExe 导致控制台输入不会显示，只能附加一个新的控制台窗口显示内容，不合适
                    // 如果能取消 管理员权限要求，改为运行时管理员权限，
                    // 则可尝试通过 Windows Terminal 或直接 Host 进行命令行模式
                    MainHandlerByCLT();
                });
                rootCommand.AddCommand(debug);
#endif

                var main = new Command(command_main);
                main.Handler = CommandHandler.Create(MainHandler);
                rootCommand.AddCommand(main);

                // -clt devtools
                var devtools = new Command("devtools");
                devtools.Handler = CommandHandler.Create(() =>
                {
                    AppHelper.EnableDevtools = true;
                    MainHandlerByCLT();
                });
                rootCommand.AddCommand(devtools);

                // -clt c -silence
                var common = new Command("c", "common");
                common.AddOption(new Option<bool>("-silence", "静默启动（不弹窗口）"));
                common.Handler = CommandHandler.Create((bool silence) =>
                {
                    IsMinimize = silence;
                    MainHandlerByCLT();
                });
                rootCommand.AddCommand(common);

                // -clt app -id 632360
                var unlock_achievement = new Command("app", "打开成就解锁窗口");
                unlock_achievement.AddOption(new Option<int>("-id", "指定一个Steam游戏Id"));
                unlock_achievement.AddOption(new Option<bool>("-silence", "静默启动（不弹窗口）"));
                unlock_achievement.Handler = CommandHandler.Create((int id, bool silence) =>
                {
                    if (id <= 0) return;
                    initStartup(DILevel.GUI | DILevel.Steam | DILevel.HttpClientFactory);
                    IWindowService.Instance.InitUnlockAchievement(id);
                    IsMinimize = silence;
                    initUIApp();
                });
                rootCommand.AddCommand(unlock_achievement);

                var r = rootCommand.InvokeAsync(args).Result;
                return r;
            }
        }
    }
}