# McInstall 宣传片

独立的 TypeScript / React / Remotion 4.0.537 项目。新增内容仅位于 `promo/`、三个 `docs/media/mcinstall-promo*` 文件与独立宣传图 `docs/media/mcinstall-brand-promo.png`。应用源码、版本号、dist、配置及旧 `docs/media/promo.*` 均保留。本任务不编辑根 README。

## 预览与常规渲染

需要 Node.js 22 或更新版本。安装的是视频项目的依赖，不运行 McInstall，也不下载游戏。

```powershell
cd promo
npm ci
npx remotion studio src/index.ts
npm run typecheck
npx remotion render src/index.ts McInstallPromo ../docs/media/mcinstall-promo.mp4 --codec=h264 --crf=18 --pixel-format=yuv420p
npx remotion still src/index.ts McInstallPromo ../docs/media/mcinstall-promo-poster.png --frame=180
npx remotion render src/index.ts McInstallPromoGif ../docs/media/mcinstall-promo.gif --codec=gif
npm run brand-image
```

项目包含 package-lock.json，可用 `npm install` 首次安装。依赖、浏览器与中间帧均被 `promo/.gitignore` 排除，不需要改根 `.gitignore`。

| Composition | 画幅 | 帧率 | 帧数 | 时长 |
| --- | --- | --- | --- | --- |
| McInstallPromo | 1920 × 1080 | 30 fps | 1590 | 53.0 秒 |
| McInstallPromoGif | 960 × 540 | 15 fps | 180 | 12.0 秒 |
| McInstallBrandCard | 1920 × 1080 | — | 1 | 静态宣传图 |

## 宣传图与浅色配色

`docs/media/mcinstall-brand-promo.png` 为 1920 × 1080 PNG：左侧使用应用原标志，右侧为主标题 `McInstall`，下方原文为“专为还原、延续PCL2下载功能打造”。源布局在 `src/BrandCard.tsx`，以本地字体直接排版，使用 `npm run brand-image` 重新导出。

主片、GIF 和视频海报已统一为浅白渐变背景（`#FCFDFE` → `#F1F4F8`）、深灰主文字（`#343B46`）及深灰说明（`#626D7C`）。标签、特写标题栏、品牌卡片和阴影也相应调整为浅色。应用截图及其局部特写保留原始真实配色；现有时间轴、运镜、逐字入场和特写动效保持不变。首尾淡入、淡出到浅白背景。

GIF 是四个各约 3 秒的短剪：片头 → PCL2 适配宣传语 → 版本选择与修复 → 开源信任段。所有动画使用帧数计算；GIF 按帧率映射到相同的 30fps 设计时间。不使用 CSS animation / transition，也不使用随机数。

## 当前版本：53 秒，连续镜头与独立宣传语

在 50 秒版前部新增 3 秒独立宣传语：“一键适配PCL2，无需复杂上手，简单高效”。McInstall 与 PCL2 两个标识块通过绘制的连线与移动光点接通，标题逐字升起。主片共 53 秒，GIF 仍为 12 秒。五步介绍分别停留 7 / 6.4 / 6 / 6 / 6.8 秒，信任段停留 5 秒。

功能段采用连续取景：完整窗口平滑推近、平移与回拉，并有小幅透视运动，取景始终保留窗口边缘；约在每段的前四分之一，真实截图的局部特写浮入，直接放大分类、加载器、线程、名称预览或下载进度。特写复制原始像素，不重绘界面，也不伪造选中、点击或下载进度。

双行大标题逐字揭示，三个功能点按节拍接力进入，当前说明有移动的强调条。片头图标带轻微透视入场和描边绘制，McInstall 字标逐字进入；信任段三个关键词依次落位，解释文案接力出现。主要文字落位后保持清楚，镜头持续移动而不影响阅读。

主片时长、分镜与抽帧点集中在 `src/timing.json`；Composition、画面动画、兼容编码器和验收脚本共用这份时间轴。新增宣传语保留后续功能段原有的阅读时间，最后 1 秒整体淡出。

场景切换改为 22 个设计帧（约 0.73 秒）的斜向裁切衔接。旧画面横移、后退并轻微失焦，新画面由右侧揭开，使用贝塞尔曲线平滑减速。独立背景遮挡旧文字，避免交叉淡化造成叠影；左上品牌、版本号和底部全局进度线保持稳定。截图提示框和功能点强调按节拍平滑亮起、熄灭，不伪造鼠标或修改真实界面状态。

前四版源码与成品分别备份于被忽略的 `out/revision-1/` 到 `out/revision-4/`；本次修改仅限 `promo/` 与四个宣传媒体文件。

## 本机编码器兼容方案（本次实际使用）

本环境 Remotion 自带的 FFmpeg / ffprobe 启动返回 Windows `0xC0000005`。画面仍由 Remotion 完整渲染，编码采用独立 FFmpeg；无需修改应用或 node_modules。可用系统 FFmpeg，或从 [FFmpeg Windows 下载页](https://ffmpeg.org/download.html#build-windows) 选择静态构建。本次采用 [Gyan essentials build](https://www.gyan.dev/ffmpeg/builds/) 9.0.2，临时程序及许可证放在被忽略的 `out/ffmpeg/`。

```powershell
npx remotion render src/index.ts McInstallPromo out/mp4-frames --sequence --image-format=jpeg --jpeg-quality=95
npx remotion render src/index.ts McInstallPromoGif out/gif-frames --sequence --image-format=jpeg --jpeg-quality=95
# 如果 FFmpeg 已在 PATH 中，无需设置此变量。
# $env:PROMO_FFMPEG = 'C:\path\to\ffmpeg.exe'
node scripts/encode.cjs mp4
node scripts/encode.cjs gif
```

`encode.cjs` 将 1590 张新帧编码为 H.264 / CRF 18 / yuv420p / faststart；GIF 使用 96 色调色板、15fps 和 Bayer 抖动控制体积。所有画面来自本项目，未使用旧宣传片或 `tools/video/make_promo.py`。

若其他环境既不支持直接 GIF，也不需要短剪，可以从新 MP4 生成前 12 秒预览：

```powershell
ffmpeg -i ../docs/media/mcinstall-promo.mp4 -t 12 -filter_complex "fps=12,scale=960:540:flags=lanczos,split[a][b];[a]palettegen=max_colors=128[p];[b][p]paletteuse=dither=bayer" -loop 0 ../docs/media/mcinstall-promo.gif
```

## 素材与字体

- 截图：从 `../docs/screenshots/` 复制的 10 张干净截图，存放在 `public/screens/`，以 `staticFile()` 加载。主片使用 dark-step1–5 和 light-step1，不使用 UI-Smoke 截图。
- 图标：`../src/McInstall.App/app.ico` 的最大内嵌图像（256px）以 Lanczos 转成 `public/icon.png`（512 × 512）；没有重新设计图标。
- 噪点：`../src/McInstall.App/noise.png` → `public/noise.png`，以 2.5% 透明度平铺。
- 中文 Noto Sans SC、拉丁 Inter、数字 JetBrains Mono 来自 Google Fonts。`scripts/prepare-fonts.cjs` 使用 `@remotion/google-fonts` 的字体元信息，将字体和 OFL 许可证保存到 `public/fonts/`。Noto 仅包含当前影片使用的中文字形，修改文案后运行 `node scripts/prepare-fonts.cjs` 更新。
- 渲染时通过 `@remotion/google-fonts/from-info`、`staticFile()`、`delayRender()` / `continueRender()` 等待本地字体加载，无在线字体请求。字体缺失时回退 Microsoft YaHei / Segoe UI / Consolas，并输出警告。本次渲染已确认全部本地 Google 字体成功加载，未使用回退。
- 字体加载 API 见 [Remotion 官方文档](https://www.remotion.dev/docs/google-fonts/)，序列渲染见 [Image Sequence](https://www.remotion.dev/docs/options/sequence)。

## 时间轴与真实界面

| 秒 | 内容 |
| --- | --- |
| 0–4.4 | 先显示指定第一句话，再显示图标与品牌 |
| 4.4–7.4 | 一键适配 PCL2 / 无需复杂上手，简单高效 |
| 7.4–14.4 | STEP 01：版本分类、搜索、校验修复 |
| 14.4–20.8 | STEP 02：原版、模组加载器与 OptiFine |
| 20.8–26.8 | STEP 03：线程、SHA1、Java 与目录 |
| 26.8–32.8 | STEP 04：名称去重与安装预览 |
| 32.8–39.6 | STEP 05：进度、日志与断点续传 |
| 39.6–44.0 | 深浅色横向 wipe |
| 44.0–49.0 | 全屏开源信任段 |
| 49.0–53.0 | 片尾；最后 1 秒淡出 |

截图保持等比，窗口约 1120 × 770，镜头整体推近不超过 1.065 倍，保留窗口全貌。局部特写约放大到原始像素的 1.42 倍，保持真实内容；加载器提示描边不改变选中状态；下载页保留 42%。版本页依次提示实际分类栏、搜索框与版本行。第一步截图没有展示“校验 / 修复”按钮，因此以文案解释已有版本修复，不虚构按钮位置或鼠标操作。

## 音频与链接

交付版静音。`src/theme.ts` 的 `AUDIO_FILE` 默认为 `null`；`Promo.tsx` 保留可选 `<Audio>` 插槽。仅在取得音乐授权后，将文件放入 `public/` 并设置相对文件名，再重新渲染。序列编码兼容方案为静音输出，需要音乐时请使用 Remotion 常规渲染或自行合并已授权音轨。

`RELEASES_URL` 默认为空，影片仅显示“在 GitHub Releases 下载”，不显示虚构仓库地址。片尾保留“非官方工具 · 与 PCL 作者无关”。

## 验收

视频平台封面另存为 `../docs/media/mcinstall-video-cover.png`（1920 × 1080）与 `mcinstall-video-cover.jpg`（1280 × 720）。构图采用深蓝背景、白黄大标题、原始标志与真实下载界面；主文案为“PCL2 下载没了？用它继续装！”。在 320 × 180 缩略图下检查过主标题可读性。运行 `npm run cover` 可重新渲染 PNG，封面源码为 `src/VideoCover.tsx`。这项调整没有重新编码或改写宣传片、GIF 与原静态宣传图。

交付参数、抽帧记录和文件完整性检查见 `verification.json`。正文检查时间点：2 / 10.5 / 17.5 / 23.5 / 29.5 / 36 / 42 / 46.5 / 51 秒；另检查 6 秒宣传语、局部特写入场及转场中间帧。中间截图、拼图和原文件哈希基线保存在被忽略的 `out/`，不需要提交。

MP4 要求小于 25 MB，GIF 小于 8 MB；视频海报为第 180 帧（6 秒，宣传语已完整落位）的 1920 × 1080 PNG，另有独立静态宣传图。MP4 的 faststart 以 MP4 顶层 `moov` 位于 `mdat` 之前验证。首尾淡入、淡出为浅白；检查正文各帧左上角的亮度，确认背景配色已经更新。

本轮制作期间，根 `README.md` 出现独立于视频任务的更新；本任务未编辑或恢复该文件。验收使用 `--external-change README.md` 显式记录这项外部变化，其余原始文件仍逐一比较基线哈希。具体结果保存在 `verification.json`。
