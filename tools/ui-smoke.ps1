#requires -Version 5.1
<#
    McInstall 向导界面 UI Automation 自检。

    - 用 Invoke-CimMethod Win32_Process Create 启动 exe（不用 Start-Process，避免子进程继承管道把命令挂住）
    - 用 System.Windows.Automation 逐个步骤验证控件真的渲染出来，并模拟点击走完 5 步
    - 每一步截图到 tools/ui-shots/
    - 启动前删掉 crash.log，结束后检查是否又出现
    - 默认临时把 exe 同目录的 McInstall.json 指向 -GameDir（跑完恢复），并造一个 UI-Test 已安装版本夹具
    - 默认只做界面检查，不安装、不下载；完整安装流程要显式加 -Install

    例：
      powershell -ExecutionPolicy Bypass -File tools\ui-smoke.ps1
      powershell -ExecutionPolicy Bypass -File tools\ui-smoke.ps1 -Install
      powershell -ExecutionPolicy Bypass -File tools\ui-smoke.ps1 -Theme dark -ScreenshotDir tools\ui-shots\dark
#>
param(
    [string]$Exe = "$PSScriptRoot\..\dist\McInstall.exe",
    [string]$GameDir = "$PSScriptRoot\..\test-mc",
    [string]$TestVersion = "1.20.1",
    [string]$InstalledVersion = "UI-Test",
    [string]$ScreenshotDir = "$PSScriptRoot\ui-shots",
    [string]$Theme = "system",
    [switch]$Install,
    [switch]$Repair,
    [bool]$UseTestConfig = $true,
    [bool]$FixtureInstalled = $true,
    [int]$InstallTimeoutSec = 600,
    [int]$RepairTimeoutSec = 300
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -Namespace McUiSmoke -Name Native -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
[DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
'@

$Exe = [System.IO.Path]::GetFullPath($Exe)
$GameDir = [System.IO.Path]::GetFullPath($GameDir)
$ScreenshotDir = [System.IO.Path]::GetFullPath($ScreenshotDir)
New-Item -ItemType Directory -Force -Path $ScreenshotDir | Out-Null

$script:failures = New-Object System.Collections.Generic.List[string]
$script:warnings = New-Object System.Collections.Generic.List[string]
$script:samples = New-Object System.Collections.Generic.List[object]
$script:dialog = $null

# 测试用配置：让程序只写 test-mc，跑完恢复原配置
$configPath = Join-Path (Split-Path -Parent $Exe) 'McInstall.json'
$configBackup = $null
if ($UseTestConfig) {
    if (Test-Path $configPath) { $configBackup = [System.IO.File]::ReadAllText($configPath) }
    $testConfig = [ordered]@{
        PclDirectory   = ''
        GameDirectory  = $GameDir
        Threads        = 16
        FullVerify     = $false
        JavaPath       = $null
        Theme          = $Theme
    }
    $testConfig | ConvertTo-Json | Set-Content -Path $configPath -Encoding UTF8
    Write-Host "  测试配置已写入：$configPath -> $GameDir"
}

# 造一个“已安装版本”夹具，只为验证已安装面板 / 校验修复按钮的渲染（不会下载）
$fixtureDir = Join-Path $GameDir "versions\$InstalledVersion"
$fixtureJson = Join-Path $fixtureDir "$InstalledVersion.json"
$fixtureCreated = $false
if ($FixtureInstalled -and -not (Test-Path $fixtureJson)) {
    New-Item -ItemType Directory -Force -Path $fixtureDir | Out-Null
    [ordered]@{
        id          = $InstalledVersion
        type        = 'release'
        mainClass   = 'net.minecraft.client.main.Main'
        assets      = '5'
        assetIndex  = [ordered]@{ id = '5' }
        libraries   = @()
        javaVersion = [ordered]@{ majorVersion = 17 }
    } | ConvertTo-Json -Depth 5 | Set-Content -Path $fixtureJson -Encoding UTF8
    $fixtureCreated = $true
    Write-Host "  已安装版本夹具已创建：$fixtureJson"
}

function Check([bool]$condition, [string]$message) {
    if ($condition) {
        Write-Host "  [OK]   $message" -ForegroundColor Green
    }
    else {
        Write-Host "  [FAIL] $message" -ForegroundColor Red
        $script:failures.Add($message)
    }
    return $condition
}

function Note([string]$message) {
    Write-Host "  [note] $message" -ForegroundColor Yellow
    $script:warnings.Add($message)
}

function Get-TopWindow([int]$processId) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty), $processId
    return [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Children, $condition)
}

function Find-ById($root, [string]$automationId) {
    if ($null -eq $root) { return $null }
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty), $automationId
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Wait-ById($root, [string]$automationId, [int]$timeoutSec = 10) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    do {
        $element = Find-ById $root $automationId
        if ($null -ne $element -and -not $element.Current.IsOffscreen) { return $element }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Wait-Gone($root, [string]$automationId, [int]$timeoutSec = 8) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    do {
        if ($null -eq (Find-ById $root $automationId)) { return $true }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    return $false
}

function Assert-Visible($root, [string]$automationId, [string]$label, [int]$timeoutSec = 10) {
    $element = Wait-ById $root $automationId $timeoutSec
    Check ($null -ne $element) "$label 已渲染（$automationId）" | Out-Null
    return $element
}

function Check-Hidden($root, [string]$automationId, [string]$label) {
    $element = Find-ById $root $automationId
    Check ($null -eq $element) "$label 已随步骤隐藏（$automationId）" | Out-Null
}

function Assert-Text($root, [string]$automationId, [string]$expected, [string]$label) {
    $element = Find-ById $root $automationId
    $actual = if ($null -eq $element) { "<missing>" } else { $element.Current.Name }
    Check ($actual -eq $expected) "$label = 「$expected」（实际「$actual」）" | Out-Null
}

function Get-Text($root, [string]$automationId) {
    $element = Find-ById $root $automationId
    if ($null -eq $element) { return "" }
    return $element.Current.Name
}

function Invoke-Element($element, [string]$label) {
    if ($null -eq $element) {
        Check $false "找不到可点击元素：$label" | Out-Null
        return $false
    }

    foreach ($patternName in @('Invoke', 'SelectionItem', 'Toggle', 'ExpandCollapse')) {
        try {
            switch ($patternName) {
                'Invoke' { $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
                'SelectionItem' { $element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() }
                'Toggle' { $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle() }
                'ExpandCollapse' { $element.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand() }
            }
            Check $true "点击 $label（$patternName）" | Out-Null
            return $true
        }
        catch {
            continue
        }
    }

    Check $false "点击 $label 失败（没有可用 Pattern）" | Out-Null
    return $false
}

function Click-ById($root, [string]$automationId, [string]$label) {
    $element = Wait-ById $root $automationId 8
    return Invoke-Element $element $label
}

function Set-Text($root, [string]$automationId, [string]$text, [string]$label) {
    $element = Wait-ById $root $automationId 8
    if ($null -eq $element) {
        Check $false "找不到输入框：$label" | Out-Null
        return
    }
    $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text)
    Start-Sleep -Milliseconds 150
    $actual = $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    Check ($actual -eq $text) "$label 已填入「$text」" | Out-Null
}

function Get-Value($root, [string]$automationId) {
    $element = Find-ById $root $automationId
    if ($null -eq $element) { return "" }
    return $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
}

function Find-ListItem($root, [string]$versionId) {
    $list = Find-ById $root 'VersionList'
    if ($null -eq $list) { return $null }
    $items = $list.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    for ($i = 0; $i -lt $items.Count; $i++) {
        $item = $items.Item($i)
        if ($item.Current.ControlType -ne [System.Windows.Automation.ControlType]::ListItem) { continue }
        $name = $item.Current.Name
        if ([string]::IsNullOrWhiteSpace($name)) { continue }
        # ListBoxItem 的 Name 是数据对象的 ToString()：VersionListItem { Id = 1.20.1, ... }
        if ($name -match 'Id = (?<id>[^,]+),') {
            $resolved = $Matches['id'].Trim()
        }
        else {
            $resolved = ($name -split '\s+')[0]
        }
        if ($resolved -eq $versionId) { return $item }
    }
    return $null
}

function Select-Version($root, [string]$versionId) {
    $deadline = (Get-Date).AddSeconds(10)
    do {
        $item = Find-ListItem $root $versionId
        if ($null -ne $item) {
            Invoke-Element $item "版本列表项 $versionId" | Out-Null
            $verifyDeadline = (Get-Date).AddSeconds(5)
            do {
                $selected = Find-ById $root 'SelectedVersionText'
                if ($null -ne $selected -and $selected.Current.Name -eq $versionId) { return $true }
                Start-Sleep -Milliseconds 150
            } while ((Get-Date) -lt $verifyDeadline)
            return $false
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    Check $false "在版本列表里找到 $versionId" | Out-Null
    return $false
}

function Find-Dialog($root, [string]$title, [int]$processId, [int]$timeoutSec = 10) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    do {
        # WPF 的 owned window 在 UIA 树里挂在主窗口下面
        $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition)
        for ($i = 0; $i -lt $all.Count; $i++) {
            $candidate = $all.Item($i)
            if ($candidate.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and
                $candidate.Current.Name -eq $title) { return $candidate }
        }
        $children = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
        for ($i = 0; $i -lt $children.Count; $i++) {
            $candidate = $children.Item($i)
            if ($candidate.Current.ProcessId -eq $processId -and $candidate.Current.Name -eq $title) {
                return $candidate
            }
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Focus-Window($window) {
    try {
        $handle = [IntPtr]$window.Current.NativeWindowHandle
        [McUiSmoke.Native]::ShowWindow($handle, 9) | Out-Null
        [McUiSmoke.Native]::SetForegroundWindow($handle) | Out-Null
        Start-Sleep -Milliseconds 300
    }
    catch { }
}

function Save-Shot($window, [string]$name) {
    try {
        $rect = $window.Current.BoundingRectangle
        if ($rect.Width -le 0 -or $rect.Height -le 0) { return }

        # 前台锁常让 SetForegroundWindow 失败，用 TOPMOST 把窗口顶上来再截屏
        $handle = [IntPtr]$window.Current.NativeWindowHandle
        [McUiSmoke.Native]::SetWindowPos($handle, [IntPtr](-1), 0, 0, 0, 0, 0x0003) | Out-Null
        [McUiSmoke.Native]::ShowWindow($handle, 9) | Out-Null
        Start-Sleep -Milliseconds 300

        $bitmap = New-Object System.Drawing.Bitmap ([int]$rect.Width), ([int]$rect.Height)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.CopyFromScreen([int]$rect.X, [int]$rect.Y, 0, 0, $bitmap.Size)
        $path = Join-Path $ScreenshotDir "$name.png"
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        $graphics.Dispose()
        $bitmap.Dispose()

        [McUiSmoke.Native]::SetWindowPos($handle, [IntPtr](-2), 0, 0, 0, 0, 0x0003) | Out-Null
        Write-Host "  [shot] $path" -ForegroundColor DarkGray
    }
    catch {
        Note "截图失败：$($_.Exception.Message)"
    }
}

function Wait-ButtonName($window, [string]$name, [int]$timeoutSec) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    do {
        $button = Find-ById $window 'NextButton'
        if ($null -ne $button -and $button.Current.Name -eq $name) { return $true }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    return $false
}

function Get-InstalledVersionNames {
    $versions = Join-Path $GameDir 'versions'
    if (-not (Test-Path $versions)) { return @() }
    return @(Get-ChildItem $versions -Directory |
        Where-Object { Test-Path (Join-Path $_.FullName ($_.Name + '.json')) } |
        ForEach-Object { $_.Name })
}

# ---------------- 启动 ----------------
try {
Write-Host "`n== 启动 McInstall（Invoke-CimMethod Win32_Process Create）==" -ForegroundColor Cyan
$crashLog = Join-Path (Split-Path -Parent $Exe) 'crash.log'
if (Test-Path $crashLog) { Remove-Item $crashLog -Force }

$createResult = Invoke-CimMethod -ClassName Win32_Process -MethodName Create `
    -Arguments @{ CommandLine = '"' + $Exe + '"' }
Check ($createResult.ReturnValue -eq 0) "Win32_Process.Create 返回 0（pid=$($createResult.ProcessId)）" | Out-Null
$processId = [int]$createResult.ProcessId

$window = $null
$deadline = (Get-Date).AddSeconds(30)
while ((Get-Date) -lt $deadline -and $null -eq $window) {
    Start-Sleep -Milliseconds 400
    $window = Get-TopWindow $processId
}
if ($null -eq $window) {
    Check $false "主窗口渲染出来"
    if (Test-Path $crashLog) {
        Write-Host "crash.log:" -ForegroundColor Red
        Get-Content $crashLog | Select-Object -First 60
    }
    exit 1
}
Check ($window.Current.Name -eq 'McInstall · MC 版本安装器') "主窗口标题正确（$($window.Current.Name)）" | Out-Null
Check ($window.Current.ProcessId -eq $processId) "窗口属于自检进程" | Out-Null
Focus-Window $window
Save-Shot $window '01-step1'

# ---------------- 顶部步骤条 ----------------
Write-Host "`n== 顶部步骤条 + 设置面板 ==" -ForegroundColor Cyan
foreach ($i in 1..5) {
    Assert-Visible $window "Step${i}Button" "步骤条第 $i 格" | Out-Null
}
foreach ($label in @('Step1Label', 'Step2Label', 'Step3Label', 'Step4Label', 'Step5Label')) {
    Assert-Visible $window $label "步骤条文字 $label" | Out-Null
}
Assert-Visible $window 'LogToggle' '日志开关' | Out-Null

Click-ById $window 'SettingsButton' '设置按钮' | Out-Null
$settingsThreads = Assert-Visible $window 'SettingsThreadsSlider' '设置面板：线程滑块' | Out-Null
Assert-Visible $window 'SettingsFullVerify' '设置面板：全量校验' | Out-Null
Assert-Visible $window 'SettingsJavaPathBox' '设置面板：Java 路径' | Out-Null
Assert-Visible $window 'ThemeBox' '设置面板：主题选择' | Out-Null
Save-Shot $window '02-settings'
$overlay = Find-ById $window 'SettingsThreadsSlider'
if ($null -ne $overlay -and $null -ne $window) {
    # 点设置面板里的「取消」
    $all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    for ($i = 0; $i -lt $all.Count; $i++) {
        $candidate = $all.Item($i)
        if ($candidate.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
            $candidate.Current.Name -eq '取消') {
            Invoke-Element $candidate '设置面板取消' | Out-Null
            break
        }
    }
}
Check (Wait-Gone $window 'SettingsThreadsSlider' 5) '设置面板已关闭' | Out-Null

# ---------------- 第一步 ----------------
Write-Host "`n== 第一步：选版本（正式版 / 快照 / 已安装 + 搜索 + 徽章）==" -ForegroundColor Cyan
Assert-Visible $window 'TabRelease' '正式版标签' | Out-Null
Assert-Visible $window 'TabSnapshot' '快照标签' | Out-Null
Assert-Visible $window 'TabInstalled' '已安装标签' | Out-Null
Assert-Visible $window 'SearchBox' '搜索框' | Out-Null
Assert-Visible $window 'VersionList' '版本列表' | Out-Null
Check-Hidden $window 'LoaderFabric' '第二步加载器卡片' | Out-Null

Set-Text $window 'SearchBox' $TestVersion '搜索版本'
Start-Sleep -Milliseconds 500
Check (Select-Version $window $TestVersion) "选中 $TestVersion" | Out-Null
Assert-Text $window 'SelectedVersionText' $TestVersion '已选版本标题' | Out-Null
Assert-Text $window 'StateText' '未安装' '状态徽章' | Out-Null
Check (Wait-ButtonName $window '继续' 5) '第一步「继续」可用并显示为「继续」' | Out-Null
Save-Shot $window '03-step1-selected'

# 已安装列表里的校验 / 修复（先清空搜索，否则会被正式版的搜索词过滤掉）
Set-Text $window 'SearchBox' '' '清空搜索'
Click-ById $window 'TabInstalled' '已安装标签' | Out-Null
Start-Sleep -Milliseconds 500
Check (Select-Version $window $InstalledVersion) "在已安装列表里选中 $InstalledVersion" | Out-Null
Assert-Visible $window 'RepairButton' '已安装版本的「校验 / 修复」按钮' | Out-Null
Save-Shot $window '04-step1-installed'

# 回到正式版
Click-ById $window 'TabRelease' '正式版标签' | Out-Null
Start-Sleep -Milliseconds 400
Set-Text $window 'SearchBox' $TestVersion '搜索版本'
Start-Sleep -Milliseconds 400
[void](Select-Version $window $TestVersion)
Check (Wait-ButtonName $window '继续' 5) '回到正式版后「继续」可用' | Out-Null

$installedBefore = Get-InstalledVersionNames
Write-Host "  已安装版本：$($installedBefore -join ', ')"

# ---------------- 第二步 ----------------
Write-Host "`n== 第二步：加载器与 OptiFine ==" -ForegroundColor Cyan
Click-ById $window 'NextButton' '第一步继续' | Out-Null
Assert-Visible $window 'LoaderVanilla' '原版卡片' | Out-Null
Assert-Visible $window 'LoaderFabric' 'Fabric 卡片' | Out-Null
Assert-Visible $window 'LoaderForge' 'Forge 卡片' | Out-Null
Assert-Visible $window 'LoaderNeoForge' 'NeoForge 卡片' | Out-Null
Assert-Visible $window 'LoaderVersionBox' '加载器版本下拉框' | Out-Null
Assert-Visible $window 'OptiFineCheck' 'OptiFine 复选框' | Out-Null
Check-Hidden $window 'VersionList' '第一步版本列表' | Out-Null
Save-Shot $window '05-step2'

# 加载器版本列表（网络失败只警告，不判定失败）
$loaderHints = @{}
foreach ($loader in @('LoaderFabric', 'LoaderForge', 'LoaderNeoForge')) {
    Invoke-Element (Find-ById $window $loader) "$loader" | Out-Null
    Start-Sleep -Seconds 2
    $hint = Get-Text $window 'LoaderVersionHint'
    $loaderHints[$loader] = $hint
    Write-Host "  [$loader] 下拉提示：$hint"
}
Save-Shot $window '06-step2-loader-versions'

# 切回原版，后面的安装走最稳的路径
Invoke-Element (Find-ById $window 'LoaderVanilla') '原版' | Out-Null
Start-Sleep -Milliseconds 500
if (-not (Wait-ButtonName $window '继续' 5)) {
    Note '原版分支「继续」不可用，尝试仍前进'
}

# 返回上一步再进来，验证步骤条/上一步
Invoke-Element (Find-ById $window 'Step1Button') '步骤条第 1 格' | Out-Null
Check ($null -ne (Wait-ById $window 'VersionList' 5)) '步骤条可以回到第一步' | Out-Null
Save-Shot $window '07-step1-back'
Click-ById $window 'NextButton' '再次继续到第二步' | Out-Null
Check ($null -ne (Wait-ById $window 'LoaderFabric' 5)) '再次进入第二步' | Out-Null
Click-ById $window 'BackButton' '上一步按钮' | Out-Null
Check ($null -ne (Wait-ById $window 'VersionList' 5)) '「上一步」回到第一步' | Out-Null
Click-ById $window 'NextButton' '第三次进入第二步' | Out-Null
Check ($null -ne (Wait-ById $window 'LoaderFabric' 5)) '第三步进入第二步（返回导航可用）' | Out-Null

Click-ById $window 'NextButton' '第二步继续' | Out-Null

# ---------------- 第三步 ----------------
Write-Host "`n== 第三步：高级设置 ==" -ForegroundColor Cyan
Assert-Visible $window 'ThreadsSlider' '线程滑块' | Out-Null
Assert-Visible $window 'ThreadsLabel' '线程数标签' | Out-Null
Assert-Visible $window 'FullVerifyBox' '全量校验复选框' | Out-Null
Assert-Visible $window 'JavaPathBox' 'Java 路径' | Out-Null
Assert-Visible $window 'BrowseJavaButton' 'Java 浏览按钮' | Out-Null
Assert-Visible $window 'GameDirBox' '安装位置' | Out-Null
Assert-Visible $window 'JavaHintText' 'Java 探测提示' | Out-Null
Check-Hidden $window 'LoaderFabric' '第二步加载器卡片' | Out-Null

Set-Text $window 'GameDirBox' $GameDir '安装位置'
$gameDirValue = Get-Value $window 'GameDirBox'
Write-Host "  安装位置：$gameDirValue"
Check ($gameDirValue.TrimEnd('\') -ieq $GameDir.TrimEnd('\')) "安装位置指向 test-mc（$GameDir）" | Out-Null
$slider = Find-ById $window 'ThreadsSlider'
$slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue(8)
Start-Sleep -Milliseconds 300
Check ((Get-Text $window 'ThreadsLabel') -eq '8') '线程数滑块改为 8 后标签同步' | Out-Null
Save-Shot $window '08-step3'

# 第三步返回再前进，验证设置被保留
Click-ById $window 'BackButton' '上一步（离开第三步）' | Out-Null
Check ($null -ne (Wait-ById $window 'LoaderFabric' 5)) '第三步返回第二步' | Out-Null
Click-ById $window 'NextButton' '再次进入第三步' | Out-Null
Assert-Visible $window 'ThreadsSlider' '线程滑块' | Out-Null
Check ((Get-Text $window 'ThreadsLabel') -eq '8') '返回再前进后线程数仍然保留' | Out-Null

Click-ById $window 'NextButton' '第三步继续' | Out-Null

# ---------------- 第四步 ----------------
Write-Host "`n== 第四步：自定义设置（版本名）==" -ForegroundColor Cyan
Assert-Visible $window 'NameBox' '版本名输入框' | Out-Null
Assert-Visible $window 'RenameButton' '改名按钮' | Out-Null
Assert-Visible $window 'SummaryText' '安装摘要' | Out-Null
Assert-Visible $window 'NameHint' '版名提示' | Out-Null
Check-Hidden $window 'ThreadsSlider' '第三步线程滑块' | Out-Null

$defaultName = Get-Value $window 'NameBox'
Write-Host "  默认版本名：$defaultName"
Check (-not [string]::IsNullOrWhiteSpace($defaultName)) '默认版本名不为空' | Out-Null
Check (-not ($installedBefore -contains $defaultName)) "默认版本名不与已安装版本重复（$defaultName）" | Out-Null
$summary = Get-Text $window 'SummaryText'
Check ($summary -match 'Minecraft' -and $summary -match '安装到') '安装摘要包含版本与安装位置' | Out-Null
Save-Shot $window '09-step4'

# 改名窗口
$testName = 'UI-Smoke-' + (Get-Date -Format 'HHmmss')
Invoke-Element (Find-ById $window 'RenameButton') '改名…' | Out-Null
$dialog = Find-Dialog $window '重命名版本' $processId 15
Check ($null -ne $dialog) '改名窗口弹出' | Out-Null
if ($null -ne $dialog) {
    Assert-Visible $dialog 'NameBox' '改名窗口输入框' | Out-Null
    Set-Text $dialog 'NameBox' $testName '改名窗口版本名'
    Save-Shot $dialog '10-rename-dialog'
    $okButton = $null
    $buttons = $dialog.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    for ($i = 0; $i -lt $buttons.Count; $i++) {
        $candidate = $buttons.Item($i)
        if ($candidate.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
            $candidate.Current.Name -eq '确定') { $okButton = $candidate; break }
    }
    Invoke-Element $okButton '改名窗口确定' | Out-Null
    Start-Sleep -Milliseconds 500
    Check ((Get-Value $window 'NameBox') -eq $testName) "改名后主界面版本名为 $testName" | Out-Null
    $script:dialog = $dialog
}
Save-Shot $window '11-step4-renamed'

Click-ById $window 'NextButton' '第四步下载' | Out-Null

# ---------------- 第五步 ----------------
Write-Host "`n== 第五步：下载与安装 ==" -ForegroundColor Cyan
Assert-Visible $window 'ProgressBar' '进度条' | Out-Null
Assert-Visible $window 'PercentText' '百分比' | Out-Null
Assert-Visible $window 'DownloadSpeedText' '下载速度' | Out-Null
Assert-Visible $window 'CurrentFileText' '当前文件' | Out-Null
Assert-Visible $window 'PhaseText' '阶段文字' | Out-Null
Assert-Visible $window 'TargetSummaryText' '安装目标摘要' | Out-Null
Check (Wait-ButtonName $window '下载' 8) '第五步按钮为「下载」' | Out-Null
Save-Shot $window '12-step5-ready'

if ($Install) {
    Click-ById $window 'NextButton' '下载' | Out-Null
    Start-Sleep -Milliseconds 800
    Check ($null -ne (Wait-ById $window 'CancelButton' 8)) '下载中出现「取消」按钮' | Out-Null

    $deadline = (Get-Date).AddSeconds($InstallTimeoutSec)
    $done = $false
    $shotProgress = $false
    while ((Get-Date) -lt $deadline) {
        $percent = Get-Text $window 'PercentText'
        $speed = Get-Text $window 'DownloadSpeedText'
        $file = Get-Text $window 'CurrentFileText'
        $phase = Get-Text $window 'PhaseText'
        $nextButton = Find-ById $window 'NextButton'
        $nextName = if ($null -ne $nextButton) { $nextButton.Current.Name } else { '' }

        $script:samples.Add([pscustomobject]@{
                Percent = $percent; Speed = $speed; File = $file; Phase = $phase
            })

        if (-not $shotProgress -and $percent -match '^\d+%$' -and $percent -ne '0%') {
            Save-Shot $window '13-step5-progress'
            $shotProgress = $true
        }

        if ($nextName -eq '再装一个') { $done = $true; break }
        Start-Sleep -Milliseconds 150
    }

    Check $done "安装完成（按钮变为「再装一个」）" | Out-Null
    if (-not $shotProgress) { Save-Shot $window '13-step5-progress' }
    $resultText = Get-Text $window 'ResultText'
    Write-Host "  结果：$($resultText -replace "`r?`n", ' / ')"
    Check ($resultText -match '安装完成') '结果区域显示安装完成' | Out-Null
    Check ((Get-Text $window 'PercentText') -eq '100%') '进度显示 100%' | Out-Null

    $progressSamples = @($script:samples | Where-Object { $_.Percent -ne '0%' -and $_.Percent -ne '' })
    Check ($progressSamples.Count -gt 0) "采到 $($progressSamples.Count) 个实时进度样本" | Out-Null
    $speedSamples = @($script:samples | Where-Object { $_.Speed -ne '—' -and $_.Speed -ne '' })
    Check ($speedSamples.Count -gt 0) "采到实时下载速度（例如 $($speedSamples[0].Speed)）" | Out-Null
    $fileSamples = @($script:samples | Where-Object { $_.File -ne '—' -and $_.File -ne '' })
    Check ($fileSamples.Count -gt 0) "采到当前文件名（例如 $($fileSamples[0].File)）" | Out-Null

    $versionJson = Join-Path $GameDir "versions\$testName\$testName.json"
    Check (Test-Path $versionJson) "版本 JSON 已写入 test-mc（$versionJson）" | Out-Null

    # 日志折叠
    Invoke-Element (Find-ById $window 'LogToggle') '日志开关' | Out-Null
    Start-Sleep -Milliseconds 500
    $logBox = Assert-Visible $window 'LogBox' '日志面板' | Out-Null
    if ($null -ne $logBox) {
        $logText = $logBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
        Check ($logText.Length -gt 100) "日志有内容（$($logText.Length) 字符）" | Out-Null
        Check ($logText -match '开始安装') '日志里有安装过程' | Out-Null
    }
    Save-Shot $window '14-step5-done-log'
    Invoke-Element (Find-ById $window 'LogToggle') '日志开关（收起）' | Out-Null
    Check (Wait-Gone $window 'LogBox' 5) '日志可以折叠起来' | Out-Null

    # 再装一个 -> 已安装 -> 校验 / 修复
    if ($Repair) {
        Write-Host "`n== 已安装版本：校验 / 修复 ==" -ForegroundColor Cyan
        Click-ById $window 'NextButton' '再装一个' | Out-Null
        Check ($null -ne (Wait-ById $window 'VersionList' 8)) '「再装一个」回到第一步' | Out-Null
        Click-ById $window 'TabInstalled' '已安装标签' | Out-Null
        Start-Sleep -Milliseconds 600
        $repairItem = Find-ListItem $window $TestVersion
        if ($null -eq $repairItem) {
            $repairItem = Find-ListItem $window $testName
        }
        Check ($null -ne $repairItem) "在已安装列表里找到 $TestVersion（或 $testName）" | Out-Null
        if ($null -ne $repairItem) {
            Invoke-Element $repairItem '已安装版本列表项' | Out-Null
            Start-Sleep -Milliseconds 500
            Assert-Visible $window 'RepairButton' '「校验 / 修复」按钮' | Out-Null
            Click-ById $window 'RepairButton' '校验 / 修复' | Out-Null
            $deadline = (Get-Date).AddSeconds($RepairTimeoutSec)
            $repairDone = $false
            while ((Get-Date) -lt $deadline) {
                $result = Find-ById $window 'RepairResultText'
                if ($null -ne $result -and -not $result.Current.IsOffscreen -and
                    $result.Current.Name.Length -gt 0) {
                    $repairDone = $true
                    break
                }
                Start-Sleep -Milliseconds 500
            }
            Check $repairDone '校验 / 修复完成并显示结果' | Out-Null
            $repairText = Get-Text $window 'RepairResultText'
            Write-Host "  修复结果：$($repairText -replace "`r?`n", ' / ')"
            Check ($repairText -match '校验通过') '校验 / 修复通过（test-mc 的 1.20.1 文件完整）' | Out-Null
            Save-Shot $window '15-repair'
        }
    }
}
else {
    Note '界面检查模式：没有触发安装/下载（需要完整流程请加 -Install）'
}

# ---------------- 关闭 ----------------
Write-Host "`n== 关闭 ==" -ForegroundColor Cyan
# 先关掉可能存在的子窗口/提示框，否则主窗口关不掉
$sameProcessWindows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
    [System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
for ($i = 0; $i -lt $sameProcessWindows.Count; $i++) {
    $candidate = $sameProcessWindows.Item($i)
    if ($candidate.Current.ProcessId -eq $processId -and
        $candidate.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and
        $candidate.Current.Name -ne 'McInstall · MC 版本安装器') {
        Write-Host "  关闭子窗口：$($candidate.Current.Name)"
        try { $candidate.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() }
        catch { }
        Start-Sleep -Milliseconds 300
    }
}
if ($null -ne $script:dialog) {
    try {
        $script:dialog.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
        Start-Sleep -Milliseconds 400
    }
    catch { }
}
try {
    $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
}
catch {
    # 自绘标题栏在部分系统上不暴露 WindowPattern，退回 WM_CLOSE 走正常关闭流程
    $hwnd = [IntPtr]$window.Current.NativeWindowHandle
    if ($hwnd -ne [IntPtr]::Zero) {
        [McUiSmoke.Native]::PostMessage($hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    }
}

$deadline = (Get-Date).AddSeconds(10)
while ((Get-Date) -lt $deadline -and $null -ne (Get-Process -Id $processId -ErrorAction SilentlyContinue)) {
    Start-Sleep -Milliseconds 300
}
if ($null -ne (Get-Process -Id $processId -ErrorAction SilentlyContinue)) {
    Note "窗口关闭后进程仍在，强制结束"
    Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
}
Check ($null -eq (Get-Process -Id $processId -ErrorAction SilentlyContinue)) '进程正常退出' | Out-Null

# ---------------- crash.log ----------------
if (Test-Path $crashLog) {
    Check $false 'crash.log 不存在'
    Write-Host "crash.log 内容：" -ForegroundColor Red
    Get-Content $crashLog | Select-Object -First 80
}
else {
    Check $true '没有产生 crash.log'
}
}
finally {
    # 恢复配置 / 清理夹具
    if ($UseTestConfig) {
        if ($null -ne $configBackup) {
            [System.IO.File]::WriteAllText($configPath, $configBackup, (New-Object System.Text.UTF8Encoding($false)))
            Write-Host "  已恢复原配置：$configPath"
        }
        elseif (Test-Path $configPath) {
            Remove-Item $configPath -Force -ErrorAction SilentlyContinue
        }
    }
    if ($fixtureCreated -and (Test-Path $fixtureDir)) {
        Remove-Item $fixtureDir -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "  已删除测试夹具：$fixtureDir"
    }
}

# ---------------- 汇总 ----------------
Write-Host "`n================ 汇总 ================" -ForegroundColor Cyan
Write-Host ("通过检查：{0} 项失败" -f $script:failures.Count)
if ($script:warnings.Count -gt 0) {
    Write-Host "警告：" -ForegroundColor Yellow
    foreach ($warning in $script:warnings) { Write-Host "  - $warning" -ForegroundColor Yellow }
}
if ($script:failures.Count -gt 0) {
    Write-Host "失败项：" -ForegroundColor Red
    foreach ($failure in $script:failures) { Write-Host "  - $failure" -ForegroundColor Red }
    Write-Host "截图目录：$ScreenshotDir"
    exit 1
}

Write-Host '全部检查通过' -ForegroundColor Green
Write-Host "截图目录：$ScreenshotDir"
exit 0
