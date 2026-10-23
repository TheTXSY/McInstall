# MC 版本安装器（McInstall）

> \*\*非官方工具，与 PCL / Plain Craft Launcher 的作者无关。\*\*
> 它不改 PCL、不打补丁、不内置任何游戏文件；只把 Minecraft 官方发布的内容下载到 PCL 的游戏目录，
> 让 PCL 能重新「看见并启动」原版、Fabric、Forge、NeoForge 版本。

\---

## 下载

* 在 [Releases](../../releases) 下载 `McInstall.exe`：Windows x64 单文件，需要 **.NET 8 桌面运行时**。
* 也可以按「从源码构建」自己编译；`dist/cli/McInstall.Cli.exe` 是可选命令行版。

## 这是什么

PCL 从 2.99.0.0 起不再提供 MC 下载功能（官方说法是「为遵守相关法律法规」）。
这个工具是它的**伴生安装器**：放在 PCL 同目录，读取 PCL 的配置找到 `.minecraft`，
把版本文件按官方格式写进去。PCL 的启动功能没有受影响，所以装完就能在 PCL 的版本列表里启动。

* 单文件 exe，双击即用
* 需要 **.NET 8 桌面运行时**（能跑 PCL 的机器基本都已经有）
* 不修改 PCL 的任何文件；PCL 的配置只读

## 界面：自绘标题栏 + 常驻配置栏

不再是「一张大卡片里堆表单」。窗口改成应用级结构：

* **自绘标题栏**：没有系统标题栏。左边是品牌，中间是 5 步极简步骤轨，右边是日志开关和窗口控制；
拖动标题栏移动窗口，双击最大化 / 还原，边缘可缩放。
* **左侧常驻「当前配置」栏**：选中版本、类型 / Java / 安装状态、加载器、版本名、安装路径、PCL 探测结果
一直可见；已安装版本的「校验 / 修复」也在这里，不再跳来跳去。底部是定位 PCL、打开目录、更改目录、
设置和**外观切换（跟随系统 / 浅色 / 深色）**。
* **右侧内容区**：去掉大外框卡片，用留白、细分隔线和排版层级组织内容；版本列表是表格，
加载器是 2×2 大卡片，高级设置是表格式设置页，命名页带一张「将创建」预览卡，安装页是
大号百分比 + 速度 + 当前文件。
* **双主题**：深色 / 浅色两套完整配色，默认**跟随 Windows 深浅模式**（2 秒轮询注册表），
也可以固定为浅色或深色；选择写进 `McInstall.json`，下次启动保持。自定义标题栏走 DWM 深色 / 圆角。
* **排版**：`Segoe UI Variable Display` + `Microsoft YaHei UI`，版本号、路径、日志用等宽字体；
统一 8pt 间距、1px 分隔线、低对比噪点纹理，避免「渐变 + 阴影堆满」的廉价感。
* **动效**：标题栏 / 配置栏 / 内容区错峰入场、步骤切换淡入 + 上移、步骤圆点脉冲、按钮悬停轻微放大、
加载器选中勾弹入、开关滑块滑动、列表选中条生长、设置弹窗淡入缩放、日志抽屉滑入、进度条扫光、
状态点呼吸、改名弹窗淡入缩放。
* **加载状态**：版本清单拉取时显示骨架屏；失败时给出网络错误提示，并可直接重新获取。

|深色|浅色|
|-|-|
|!\[深色主题](docs/screenshots/dark-step1.png)|!\[浅色主题](docs/screenshots/light-step1.png)|

> 截图是 `tools/PreviewRenderer` 的离屏渲染结果：不显示窗口、不触发网络，只渲染视觉树。

<details>
<summary>更多界面截图</summary>

|加载器|高级设置|
|-|-|
|!\[加载器](docs/screenshots/dark-step2.png)|!\[高级设置](docs/screenshots/dark-step3.png)|

|命名|设置|
|-|-|
|!\[命名](docs/screenshots/dark-step4.png)|!\[设置](docs/screenshots/dark-settings.png)|

|下载安装|改名弹窗|
|-|-|
|!\[下载安装](docs/screenshots/dark-step5.png)|!\[改名弹窗](docs/screenshots/rename-dark.png)|

</details>

|步骤|内容|
|-|-|
|1. 选版本|正式版 / 快照 / 已安装三个列表，支持按版本号搜索；每项带类型徽章和发布时间。选中后左侧配置栏显示类型、Java、安装状态；**已安装版本在左侧栏里直接「校验 / 修复」**，进度和结果也内联显示。|
|2. 选加载器|原版 / Fabric / Forge / NeoForge 四张大卡片；加载器版本默认「自动（推荐）」，也可以挑具体版本。Forge 下可以勾「同时安装 OptiFine」，OptiFine 以 Forge mod 的形式装进 `mods/`。|
|3. 高级设置|下载线程数（1–32）、安装后全量 SHA1 校验、Java 路径（留空自动探测，找不到会下载 JRE）、**安装位置**（可直接改游戏目录，等价于「更改目录」）。|
|4. 自定义设置|大字版本名 + 「改名…」弹窗（`RenameWindow` 也是自绘标题栏）；默认名已去过重，不会和已有版本撞名（例如已有 `1.20.1` 时默认是 `1.20.1 (2)`）；Forge / NeoForge 会提示版本名由官方安装器决定。|
|5. 下载安装|大号渐变百分比 + **实时下载速度** + 剩余时间 + 进度条 + **当前文件** + 阶段 / 文件数；标题栏「日志」可展开完整日志。取消后可重试，完成后显示结果和「打开版本目录」。|

保留的旧功能：

* **设置面板**（左侧栏「设置」）：默认线程数、默认全量校验、默认 Java 路径、外观、清理缓存
* **日志折叠**（标题栏右侧）：安装 / 修复出错时自动展开；日志标题栏可一键复制
* **改名窗口**：第四步「改名…」，确认时再兜一次去重
* **校验 / 修复**：第一步切到「已安装」列表，选中版本后在左侧栏点按钮
* 左侧栏里的目录信息、定位 PCL、打开目录、更改目录

> 安装位置、线程数、Java 路径在离开第三步时就会保存；无效路径会被拦下或还原。

## 功能

|功能|状态|说明|
|-|-|-|
|分步向导|✅|5 步、自绘标题栏步骤轨、可回退、前进需「继续」，防止跳过必填项|
|深浅主题|✅|默认跟随 Windows；可固定浅色 / 深色；写进 `McInstall.json`|
|自绘窗口|✅|无系统标题栏，拖动 / 双击最大化 / 边缘缩放，DWM 圆角|
|加载 / 错误状态|✅|版本清单骨架屏；拉取失败时可重新获取|
|原版正式版 / 快照|✅|官方 manifest，含 client.jar、资源索引、全部 libraries|
|Fabric|✅|官方 meta profile，自动选稳定版|
|Forge|✅|调官方 installer（1.13+ 需要 Java，缺失时自动下载 JRE）|
|NeoForge|✅|同上；1.20.1 走旧坐标|
|OptiFine|✅|以「Forge + OptiFine mod」方式装入 `mods/`（1.14+ 主流做法）|
|断点续传|✅|`.part` 文件 + HTTP Range，中断后重跑会接着下|
|SHA1 校验|✅|下载完逐文件校验；安装后做完整性检查，可选全量校验|
|自动补文件|✅|重跑安装 = 补齐缺失文件；`McInstall.Cli verify` 可单独体检|
|已安装版本校验 / 修复|✅|第一步「已安装」列表内，进度与结果在左侧配置栏内联显示|
|下载进度 / 速度 / 当前文件|✅|进度条 + 百分比 + 实时速度 + 剩余时间 + 当前文件名|
|实时日志|✅|可折叠；失败自动展开，可一键复制，最多保留 1500 行|
|PCL 直接启动|✅|文件写在 PCL 游戏目录里，PCL 版本列表直接可见|

## 用法

1. 把 `McInstall.exe` 放到 `PCL2.exe` 旁边（和 `.minecraft` 同级）。
2. 双击打开。左侧「当前配置」会显示探测到的游戏目录；不对就点「更改目录」或在第三步改「安装位置」。
3. 按向导走：

   * 第一步选版本（正式版 / 快照 / 已安装，可搜索）；选已安装的版本可以直接「校验 / 修复」
   * 第二步选加载器：原版直接继续；Fabric / Forge / NeoForge 默认自动选版本；Forge 可加 OptiFine
   * 第三步按需改线程数、全量校验、Java 路径、安装位置
   * 第四步确认版本名（可改名）
   * 第五步点「下载」，看进度条走完
4. 回到 PCL，刷新版本列表（或重启 PCL），选中新版本启动。
5. 外观默认跟随 Windows；想固定就在左侧栏底部切「浅色 / 深色」，或进「设置 → 外观」。

> 首次打开会自动选中最新的正式版；不喜欢可以直接改，或者按 `Ctrl+F` 搜索。

## 快捷键

|快捷键|作用|
|-|-|
|`Ctrl + F`|在第一步聚焦版本搜索框（自动全选）|
|`Esc`|关闭设置弹窗 / 取消改名|
|`Enter`|改名窗口里确认|

> 安装过程会写 `McInstall/` 目录：`cache/`（Forge 安装器、OptiFine jar）、`runtime/`（自动下载的 JRE）、`tmp/`。
> 都可以随时删。
>
> 工具自己的配置在同目录 `McInstall.json`（PCL 目录、游戏目录、线程、Java 路径、外观），删掉会重新探测。

## 命令行（可选）

`McInstall.Cli.exe`（在 `cli/` 目录）适合排查问题和做「体检」：

```text
McInstall.Cli dirs                     显示 PCL 目录 / 游戏目录探测结果
McInstall.Cli list                     列出可安装版本
McInstall.Cli install --mc 1.20.1      安装原版
McInstall.Cli install --mc 1.20.1 --loader fabric
McInstall.Cli install --mc 1.20.1 --loader forge --optifine latest
McInstall.Cli verify --mc 1.20.1 --full   全量校验某个已安装版本
```

通用选项：`--game <目录>` 指定游戏目录，`--pcl <目录>` 指定 PCL 目录，`--threads <N>` 改并发。

## UI 自检（开发用）

默认**只检查界面**，不安装、不下载游戏文件：

```powershell
powershell -ExecutionPolicy Bypass -File tools\\ui-smoke.ps1

# 指定主题
powershell -ExecutionPolicy Bypass -File tools\\ui-smoke.ps1 -Theme dark
```

需要完整安装流程时才显式加 `-Install`（只写 `test-mc`，不碰 PCL2 的 `.minecraft`）：

```powershell
powershell -ExecutionPolicy Bypass -File tools\\ui-smoke.ps1 -Install
```

脚本行为：

* 用 `Invoke-CimMethod Win32\_Process Create` 启动 exe（不用 `Start-Process`，避免子进程继承管道把命令挂住）
* 用 `System.Windows.Automation` 按 AutomationId 查找并点击控件，逐步断言
* 启动前删除 `crash.log`，结束后检查是否产生；有崩溃会把内容打出来
* 截图输出到 `tools/ui-shots/`
* 默认会临时把 `dist\\McInstall.json` 指向 `-GameDir`（默认 `test-mc`），跑完自动恢复
* 默认会在 `test-mc\\versions\\UI-Test` 造一个只有 JSON 的「已安装版本」夹具，验证已安装面板 / 校验修复按钮，跑完自动删除（不下载任何游戏文件）
* 默认不安装；`-Install` 才触发下载，`-Repair` 才跑修复流程
* 测试安装/修复只写 `-GameDir`（默认 `test-mc`）

### 不弹窗的离屏预览

改外观时更推荐这个：它不 `Show()` 窗口、不触发 `Loaded` 和网络，只把视觉树渲染成 PNG。

```powershell
dotnet run --project tools\\PreviewRenderer -c Release -- tools\\ui-shots\\preview
```

会在 `tools/ui-shots/preview/` 生成深色 / 浅色各 5 步、设置弹窗和改名弹窗的截图。
渲染器只引用 App，不会被打进 `dist`。

## 常见问题

**装完 PCL 里看不到？**
重启 PCL，或在版本列表里点刷新。实在不行用 `McInstall.Cli dirs` 确认两边指向的是同一个 `.minecraft`。

**Forge 安装失败？**
先看日志最后的 `\[ERR]` 行。常见原因：没有 Java（工具会自动下载 JRE，失败时可能被网络拦了）、
磁盘空间不足、目标目录没有写权限。Forge 的安装器日志会完整保留在窗口里，可以直接复制。

**PCL 提示「没有合适的 Java」？**
PCL 只用它自己扫描到的 Java。本工具自动下载的 JRE 在 `McInstall\\runtime\\` 下
（例如 `jre-17\\...\\bin\\java.exe`），可以在 PCL 的「设置 → Java」里手动添加；
或者直接让 PCL 用它自己的「自动安装 Java」功能装一个，效果一样。

**OptiFine 装在哪？**
为了兼容 PCL 的「版本隔离」开/关两种情况，jar 会同时放进：

* `<游戏目录>/mods/`
* `<游戏目录>/versions/<Forge 版本>/mods/`

哪边生效由 PCL 的隔离设置决定，另一个位置会被忽略，不会重复加载。

**版本名能随便改吗？**
原版 / Fabric 可以（第四步「改名…」）；Forge / NeoForge 的版本名由官方安装器决定，
界面里会提示改名不生效。默认名会自动去重，不会覆盖已有版本。

**要多大空间？**
原版 1.20.1 约 700 MB（含资源文件）；Forge 再加约 100 MB；每个自动下载的 JRE 约 50 MB。
已经存在的库和资源会被校验后复用，不会重复下载。

**杀软报毒？**
单文件 exe 容易被误报。工具只做三件事：HTTPS 下载、写 `.minecraft` 里的文件、调用 Java 跑 Forge 官方安装器。
介意的话可以自己从源码构建（见下）。

## 已知限制

* **镜像未启用**：BMCLAPI 在 2026-10 实测已不可用，目前只用官方源。国内网络慢的话建议挂代理。
* **古老版本**（alpha / beta / 1.5 及以前）没有专门适配，资源索引可能是 `legacy` 结构。
* **NeoForge 最新版**（26.x）尚未实测。
* **OptiFine 纯原版安装**（改 client.jar 那种）没有做，只支持 Forge + mod 方式。
* 不处理登录/账号，登录仍然由 PCL 负责。
* 下载速度是引擎统计的瞬时速度；刚复用大量已有文件时数值会偏高，属正常现象。

## 从源码构建

需要 .NET 8 SDK。

```powershell
# 图形界面（单文件 exe -> dist\\McInstall.exe）
dotnet publish src/McInstall.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist

# 命令行版（-> dist\\cli\\McInstall.Cli.exe）
dotnet publish src/McInstall.Cli -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist\\cli
```

或者直接跑 `tools/build.ps1`。如果提示「禁止运行脚本」，用：

```powershell
powershell -ExecutionPolicy Bypass -File tools\\build.ps1
```

仓库带 GitHub Actions：`.github/workflows/build.yml` 在 push / PR 时构建，
`.github/workflows/release.yml` 在推送 `v\*` tag 时自动打包 `McInstall-win-x64.zip` 并创建 Release。

## 项目结构

```text
src/McInstall.App       # WPF 界面、主题、向导、自绘窗口
src/McInstall.Core      # 下载 / 安装 / 校验 / 修复，不含界面
src/McInstall.Cli       # 命令行体检工具
tools/PreviewRenderer   # 离屏渲染截图（不弹窗）
tools/video             # 宣传片生成（离屏截图 + ffmpeg）
tools/ui-smoke.ps1      # UI Automation 自检，默认不安装
docs/                   # 截图与宣传片
```

## 更新记录

* **0.5.0**：GitHub 发布准备。自适应 1366×768 等小屏的初始窗口尺寸；首次加载 / 切标签 / 搜索时自动选中第一条；
版本清单骨架屏与网络错误状态、日志一键复制、点击设置遮罩关闭；新增 `Ctrl+F` 搜索聚焦、`Esc` 关闭设置；
左下角显示版本号；补齐 README 所需的截图、CHANGELOG、LICENSE、构建 / 发布工作流和 35 秒宣传片。
* **0.4.0**：界面第二次重构，从「单页向导」升级成应用级布局：自绘标题栏（无系统标题栏、可拖动 / 双击最大化 /
边缘缩放）+ 顶部极简步骤轨 + 左侧常驻「当前配置」栏 + 无大卡片的内容区；版本列表改成表格，
加载器改成 2×2 大卡，高级设置改成表格式设置页，命名页带版本预览卡，安装页改成大号百分比 + 速度 + 当前文件。
新增完整深 / 浅主题：默认跟随 Windows，可固定，写入配置；标题栏走 DWM 深色 / 圆角。改名弹窗同步改为自绘标题栏。
新增 `tools/PreviewRenderer` 离屏渲染器（不弹窗、不联网）；UI 自检默认改为只查界面、不装游戏文件，`-Install` 才跑安装。
* **0.3.0**：界面视觉重做。新设计系统（渐变主色、卡片阴影、圆角、细化滚动条、应用图标、深色日志控制台），
加入窗口入场、步骤切换、步骤圆点、按钮悬停、开关滑块、加载器勾选、列表选中条、设置弹窗、日志抽屉、
进度条扫光、状态点呼吸等动画；改名弹窗同步重做；自检脚本支持临时测试配置与已安装版本夹具。
* **0.2.0**：界面改为 5 步向导（选版本 / 选加载器 / 高级设置 / 版本名 / 下载安装），
顶部步骤条可回退；新增实时下载速度与当前文件显示；已安装版本的校验/修复内联到第一步；
修复「校验 / 修复」在后台线程读取界面控件导致的跨线程异常；安装完成统计改为「共 X（已存在的文件自动复用）」，
避免把复用文件误当成下载量。
* **0.1.2**：设置面板、日志折叠、改名窗口、校验修复、断点续传、SHA1 校验。

## License

MIT，见 [LICENSE](LICENSE)。PCL / Plain Craft Launcher 是其作者的作品，本项目与官方无关。

## 免责声明

* 本工具从 Mojang / Fabric / Forge / NeoForge / OptiFine 的官方端点下载文件，不托管、不分发任何游戏内容。
* 请使用正版账号游玩；本工具不提供任何登录绕过。
* 因使用本工具产生的任何后果由使用者自行承担。请遵守当地法律法规与 Minecraft EULA。

