M365Hub Windows 版（WinUI 3 原生）— README
========================================

这是什么
--------
跟 Mac 的 SwiftUI 版、Android 的原生版一样的做法：网关编 Windows 版，
界面用原生 WinUI 3（C# + XAML）写，不套 WebView，不套 Electron。
打开就是聊天，Mica 窗口背景，按键和输入栏是 WinUI 的 Acrylic 液态玻璃
（WinUI 3 没有 iOS 那种折射材质，Acrylic 是平台上最接近的等价物：
磨砂 + 染色 + 噪点，由合成器做背景模糊）。

x64 与 ARM64
------------
两个架构都支持，同一个包：
- 网关有两个 exe：gateway/m365hub.exe（x64）、gateway/m365hub-arm64.exe（ARM64）
- 应用启动时按本机 CPU 自动选对的网关（x64 机器跑 x64 版，ARM64 机器
  如 Surface Pro X / Snapdragon X Elite 跑 ARM64 版）
- 编译时用 -r win-x64 或 -r win-arm64 选目标架构（见下面 build.bat）

前置要求（在你的 Windows 电脑上）
----------------------------------
1. Windows 10 1809 或更高 / Windows 11（x64 或 ARM64）
2. .NET 8 SDK（https://dotnet.microsoft.com/download/dotnet/8.0）
3. Windows App Runtime 1.6（只装一次；没有它 WinUI 3 应用打不开。
   装法：Microsoft Store 搜 "Windows App Runtime"，或到
   https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads
   下 1.6 的安装包）

构建
----
双击 build.bat（按本机 CPU 自动选 win-x64 或 win-arm64），或手动用
Visual Studio 的 MSBuild（WinUI 3 资源打包要 VS 自带的 Pri 任务，
dotnet build 单独编会报 MSB4062）：

    msbuild src\M365Hub.App\M365Hub.App.csproj -restore -p:Configuration=Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64
    msbuild src\M365Hub.App\M365Hub.App.csproj -restore -p:Configuration=Release -p:Platform=ARM64 -p:RuntimeIdentifier=win-arm64

产物在 src\M365Hub.App\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\
（或 bin\ARM64\Release\...\win-arm64\），M365Hub.App.exe，旁边会自动
带上两个网关 exe。直接双击 M365Hub.App.exe 运行。
（用 dotnet build 时产物在 bin\Release\...，没有 x64/ARM64 这一层。）

首次运行
--------
1. 应用启动本地网关（127.0.0.1:8230），启动屏显示进度；60 秒没应答会出
   失败屏，带重试和查看网关日志。
2. 输控制台管理员密码登录（首次是默认密码 admin123，登录后会被要求改掉）。
3. 账号页 → 添加账号（PKCE）：授权链接会自动复制，浏览器打开 Microsoft
   登录，完成后把地址栏的完整回调 URL 粘贴回来点确认。浏览器只打开首页
   时也一样：在地址栏粘贴已复制的链接。设备码是回退方式。
4. 聊天页顶栏可以指定账号（默认轮询，选了就只用那个号），模型默认
   gpt-6.1-sol-reasoning。

数据存在哪
----------
%LOCALAPPDATA%\M365Hub\：
- data\（网关数据：账号、token、用量）
- chat-history.json（聊天记录）
- chat-images\（上传的图片缩略图）
- admin-cookie.txt（登录会话，重启不用重新登录）
- settings.json（主题、pin 的账号、模型）
- serve-windows.log（网关日志）

功能对照（与 Android 原生 1.2.7 同步）
--------------------------------------
- 聊天：Markdown（标题/列表/加粗/斜体/行内代码）、TeX 公式（Cambria Math
  渲染，含 aligned/gathered/cases、根号一笔画、长箭头）、上传图片/文件/
  拍照、生图模式、账号 pin、原样发请求
- 仪表盘：24h/累计/缓存/会话数 + 最近请求
- 账号：搜索/状态筛选、批量操作、模型测试、PKCE/设备码添加
- 用量：1/7/30/90 天、每日 Token 趋势、按模型/账号/Key/端点 Top 8、请求明细
- 代理：添加/删除、健康列、本机代理检测（Clash/V2Ray/Surge/QX 常用端口）
- 对话：本地绑定 + 云端拉取（走网关 DoH 客户端）
- 设置：改密、亮暗切换、退出登录、停止网关并退出
- 本地记忆：按标签查看摘要与轮次、删除（Android 版只有 toast，这里有完整查看器）

核心逻辑验证
------------
M365Hub.Core（Markdown / TeXSplit / MathSegments / TeXRenderer /
ApiClient / ChatStore / GatewayManager / Models）已在 Linux 上用 .NET 8
真编真测：tests/M365Hub.Tests 与 Android 宿主测试同一套用例，
51 项全过（加粗/列表/标题、aligned、⟹、根号三笔连线等）。
跑测试：dotnet run --project tests/M365Hub.Tests

诚实说明
--------
- WinUI 界面本身没法在这台 Linux 虚拟机上编译：首次在你的 Windows
  电脑上 build.bat 跑通就是验收步骤；如果报包还原或 SDK 相关错误，
  把报错贴回来。
- 液态玻璃是 Acrylic，不是 iOS 那种逐像素折射；导航药丸与按钮是静态
  玻璃外观，模糊由合成器做。
- 公式用 Cambria Math（Windows 自带数学字体）画；Mac 版用 SwiftMath，
  两边字形略有不同是正常的。

1.0.1（2026-10-31，网关只修生图下载）
与 Android 1.2.12 同一个网关修法：Designer 图片下载改走 outbound 共享传输（DoH 兜底），之前用裸 http.Client，在直连 DNS 被拒的机器上取图失败，生图只剩一句话、图是空白的。应用代码未改，只重编了 gateway/m365hub.exe 与 gateway/m365hub-arm64.exe。包 sha256 a99606fb48ff6b6f1e190905e0a34a1eab3b361a8dff7c68fc7691dd6327eeda。

1.0.2（2026-10-31，只换图标）
- 应用图标换成用户给的机器人新图标：新增 src/M365Hub.App/app.ico（16/32/48/256 四档），csproj 用 <ApplicationIcon> 嵌进 exe，MainWindow 启动时再调 AppWindow.SetIcon，任务栏与窗口都显示新图标。
- 应用逻辑与网关未改。WinUI 界面仍未在 Windows 上编译过，首次在用户 Windows 电脑上双击 build.bat 跑通即验收。包 sha256 97f79028bd36177380a71c711f3402d5030de66018dff9eae028faa925f606a7。
