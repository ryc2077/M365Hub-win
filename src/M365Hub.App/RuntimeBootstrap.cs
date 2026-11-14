using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Windows.ApplicationModel.DynamicDependency;

namespace M365Hub.App;

/// <summary>
/// Windows App Runtime 自举（1.0.6）：关掉框架默认的“需要安装 Runtime”
/// 提示框，改由应用自己接管。
///
/// 做法：
/// 1. 先试本机已装的 Windows App Runtime 1.6（应用按 Windows App SDK 1.6
///    编译；1.7/1.8 是独立框架包，不能代替 1.6——Microsoft Learn 与实测都
///    证实了这一点，所以只认 1.6，不赌更新版）。
/// 2. 没有 1.6 就静默跑自带的官方安装器（安装包里 runtime/WindowsAppRuntimeInstall.exe，
///    随 x64/arm64 各一份打进 release zip），等它装完再试一次。
/// 3. 还不行才弹一句人话的报错框（不是框架那个框），再退出。
///
/// 为什么放在模块初始化器里：SDK 自带的自动初始化就是这么干的（由
/// &lt;WindowsPackageType&gt;None 生成的模块初始化器调 bootstrapper；本工程已用
/// &lt;WindowsAppSdkBootstrapInitialize&gt;false 关掉它，改由这里接管）。
/// 模块初始化器在程序集加载时跑、在任何 WinUI 类型加载之前；放到 App 构造
/// 函数里就晚了——WinUI 程序集加载时框架包还没进包图，进程直接崩。
/// 模块初始化器里抛异常会让整个程序集加载失败，所以这里全程 try/catch，
/// 绝不把异常抛出去。
/// </summary>
internal static class RuntimeBootstrap
{
    /// <summary>Windows App SDK 1.6，编码为 0x00010006。</summary>
    private const uint AppSdk16 = 0x00010006;

    /// <summary>框架要求 1.6 框架包最低 6000.266.2241.0（就是用户 1.0.5 真机
    /// 上那个安装框里写的版本）。</summary>
    private static readonly PackageVersion MinFrameworkVersion = new(6000, 266, 2241, 0);

    private const string InstallerRelativePath = "runtime/WindowsAppRuntimeInstall.exe";

    [ModuleInitializer]
    internal static void EnsureRuntime()
    {
        try
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try { Bootstrap.Shutdown(); } catch { }
            };

            if (Bootstrap.TryInitialize(AppSdk16, "", MinFrameworkVersion, out _))
            {
                Log("runtime 1.6 already present, bootstrap ok");
                return;
            }

            Log("runtime 1.6 missing, trying bundled installer");
            if (TryInstallBundledRuntime()
                && Bootstrap.TryInitialize(AppSdk16, "", MinFrameworkVersion, out _))
            {
                Log("bundled installer succeeded, bootstrap ok");
                return;
            }

            Log("bootstrap failed after bundled installer");
        }
        catch (Exception ex)
        {
            Log("bootstrap exception: " + ex.GetType().Name + ": " + ex.Message);
        }

        ShowFatalErrorAndExit();
    }

    /// <summary>静默跑自带的 1.6 安装器。装框架包要管理员权限，会弹一次 UAC；
    /// 用户点了取消就返回 false。超时 5 分钟。</summary>
    private static bool TryInstallBundledRuntime()
    {
        try
        {
            string installer = Path.Combine(AppContext.BaseDirectory, InstallerRelativePath);
            if (!File.Exists(installer))
            {
                Log("bundled installer not found: " + installer);
                return false;
            }

            var start = new ProcessStartInfo
            {
                FileName = installer,
                Arguments = "--quiet",
                UseShellExecute = true,
                Verb = "runas",
            };
            using Process? process = Process.Start(start);
            if (process == null)
            {
                Log("bundled installer did not start");
                return false;
            }

            if (!process.WaitForExit(5 * 60 * 1000))
            {
                try { process.Kill(); } catch { }
                Log("bundled installer timed out");
                return false;
            }

            Log("bundled installer exit code: " + process.ExitCode);
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Log("bundled installer exception: " + ex.GetType().Name + ": " + ex.Message);
            return false;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private static void ShowFatalErrorAndExit()
    {
        try
        {
            MessageBoxW(IntPtr.Zero,
                "M365Hub 需要 Windows App Runtime 1.6 才能启动，但自动安装没有成功。\n\n" +
                "请手动安装：打开安装包里的 runtime 文件夹，双击 WindowsAppRuntimeInstall.exe（需要管理员权限），装完再打开 M365Hub。\n\n" +
                "如果刚才是以管理员身份运行的，请改用普通方式双击打开。",
                "M365Hub 无法启动",
                0x10 /* MB_ICONERROR */);
        }
        catch { }
        Environment.Exit(1);
    }

    /// <summary>自举诊断写 %LOCALAPPDATA%\M365Hub\runtime-bootstrap.log，
    /// 写失败就放弃（不能因为日志写不进耽误启动）。</summary>
    private static void Log(string message)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "M365Hub");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "runtime-bootstrap.log"),
                DateTimeOffset.Now.ToString("o") + " " + message + "\n");
        }
        catch { }
    }
}
