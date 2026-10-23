using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shell;
using System.Windows.Threading;
using McInstall.Core;

namespace McInstall.App;

public partial class MainWindow : Window
{
    private enum OpMode
    {
        None,
        Install,
        Repair,
    }

    private sealed record VersionListItem(string Id, string Subtitle, string Type)
    {
        public string Badge => Type switch
        {
            "installed" => "已安装",
            "snapshot" => "快照",
            _ => "正式版",
        };
    }

    private sealed class LoaderChoice(string label, string? version)
    {
        public string? Version { get; } = version;
        public override string ToString() => label;
    }

    private const int LastStep = 5;

    private static readonly CubicEase EaseOut = CreateEaseOut();

    private static CubicEase CreateEaseOut()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ease.Freeze();
        return ease;
    }

    private static Brush ThemeBrush(string key)
        => Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;

    private static Effect? ThemeEffect(string key)
        => Application.Current?.TryFindResource(key) as Effect;

    private readonly HttpClient _http;
    private readonly VersionService _versionService;
    private readonly FabricInstaller _fabric;
    private readonly ForgeInstaller _forge;
    private readonly ForgeInstaller _neoForge;
    private readonly OptiFineInstaller _optiFine;
    private readonly MinecraftInstaller _installer;
    private readonly RepairService _repair;
    private readonly ObservableCollection<VersionListItem> _view = new();

    private readonly Button[] _stepButtons;
    private readonly Border[] _stepLines;
    private readonly Border[] _stepDots;
    private readonly TextBlock[] _stepNumbers;
    private readonly TextBlock[] _stepLabels;

    private IReadOnlyList<VersionSummary> _allVersions = Array.Empty<VersionSummary>();
    private McInstallConfig _config = new();
    private string _pclDir = "";
    private string _gameDir = "";
    private CancellationTokenSource? _operationCts;
    private CancellationTokenSource? _loaderCts;
    private Task<IReadOnlyList<JavaInfo>>? _javaDiscovery;
    private bool _busy;
    private bool _ready;
    private bool _suspendSelection;
    private bool _settingName;
    private bool _nameTouched;
    private bool _installStarted;
    private bool _completed;
    private int _logLines;
    private int _step = 1;
    private int _maxStep = 1;
    private string? _currentVersionId;
    private string? _lastInstallVersionId;
    private int _settingsTransition;
    private bool _syncingThemeOptions;
    private OpMode _opMode = OpMode.None;

    public MainWindow()
    {
        InitializeComponent();

        _stepButtons = new[] { Step1Button, Step2Button, Step3Button, Step4Button, Step5Button };
        _stepLines = new[] { StepLine1, StepLine2, StepLine3, StepLine4 };
        _stepDots = new[] { Step1Dot, Step2Dot, Step3Dot, Step4Dot, Step5Dot };
        _stepNumbers = new[] { Step1Num, Step2Num, Step3Num, Step4Num, Step5Num };
        _stepLabels = new[] { Step1Label, Step2Label, Step3Label, Step4Label, Step5Label };

        _http = HttpClientFactory.Create();
        _versionService = new VersionService(_http);
        _fabric = new FabricInstaller(_http);
        _forge = new ForgeInstaller(_http, ModLoaderKind.Forge);
        _neoForge = new ForgeInstaller(_http, ModLoaderKind.NeoForge);
        _optiFine = new OptiFineInstaller(_http);
        _installer = new MinecraftInstaller(_http);
        _repair = new RepairService(_http);

        VersionList.ItemsSource = _view;
        AppVersionText.Text = $"v{typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.5.0"}";

        // 小屏幕（1366×768 等）上不要把窗口顶出工作区
        var work = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, Math.Min(Width, work.Width - 32));
        Height = Math.Max(MinHeight, Math.Min(Height, work.Height - 32));

        SourceInitialized += (_, _) => ApplyWindowTheme();
        ThemeManager.Changed += OnThemeChanged;
        Closed += (_, _) => ThemeManager.Changed -= OnThemeChanged;
        _ready = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && SettingsOverlay.Visibility == Visibility.Visible)
        {
            HideSettingsOverlay();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            && _step == 1 && !_busy && SettingsOverlay.Visibility != Visibility.Visible)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void ApplyWindowTheme()
    {
        WindowTheme.Apply(this, ThemeManager.IsDark);
    }

    private void OnThemeChanged()
    {
        ApplyWindowTheme();
        SyncThemeOptions();
        if (ThemeBox is not null)
        {
            ThemeBox.SelectedIndex = ThemeManager.Mode switch
            {
                AppTheme.Light => 1,
                AppTheme.Dark => 2,
                _ => 0,
            };
        }
        UpdateStepChrome();
        StatusDot.Background = _completed
            ? ThemeBrush("Success")
            : _busy ? ThemeBrush("Accent") : ThemeBrush("StatusIdle");
    }

    private void SyncThemeOptions()
    {
        if (ThemeSystemOption is null) return;
        _syncingThemeOptions = true;
        try
        {
            ThemeSystemOption.IsChecked = ThemeManager.Mode == AppTheme.System;
            ThemeLightOption.IsChecked = ThemeManager.Mode == AppTheme.Light;
            ThemeDarkOption.IsChecked = ThemeManager.Mode == AppTheme.Dark;
        }
        finally
        {
            _syncingThemeOptions = false;
        }
    }

    private void ThemeOption_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready || _syncingThemeOptions) return;

        var mode = sender == ThemeLightOption ? AppTheme.Light
            : sender == ThemeDarkOption ? AppTheme.Dark
            : AppTheme.System;
        if (ThemeManager.Mode == mode) return;

        ThemeManager.SetMode(mode);
        _config.Theme = ThemeManager.ToConfig(mode);
        _config.Save();
        AppendLog($"外观已切换为：{ThemeManager.ToConfig(mode)}");
    }

    private void UpdateRail()
    {
        if (RailLoaderText is null) return;

        var loader = GetSelectedLoader();
        RailLoaderText.Text = loader == "vanilla"
            ? "原版"
            : $"{LoaderDisplayName(loader)} · {LoaderVersionDisplay()}";

        var name = NameBox?.Text?.Trim();
        RailNameText.Text = string.IsNullOrEmpty(name) ? "自动命名" : name;
    }

    // ---------- 自绘窗口 ----------

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (IsInteractive(e.OriginalSource as DependencyObject)) return;
        try { DragMove(); } catch { /* 窗口状态切换时忽略 */ }
    }

    private static bool IsInteractive(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase or TextBoxBase or ComboBox or Slider) return true;
            try { source = VisualTreeHelper.GetParent(source); }
            catch { source = LogicalTreeHelper.GetParent(source); }
        }
        return false;
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (MaximizeGlyph is not null)
            MaximizeGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        if (WindowFrame is not null)
            WindowFrame.BorderThickness = WindowState == WindowState.Maximized
                ? new Thickness(0)
                : new Thickness(1);
    }

    private void ToggleMaximize()
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ================= 启动 =================

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _config = McInstallConfig.Load();
        _pclDir = ResolveInitialPclDirectory();
        _gameDir = ResolveInitialGameDir();
        UpdateHeader();

        if (!string.IsNullOrEmpty(_pclDir))
        {
            _config.PclDirectory = _pclDir;
            _config.GameDirectory = _gameDir;
            _config.Save();
        }

        if (string.IsNullOrEmpty(_pclDir))
            AppendLog("没有自动找到 PCL2.exe，可以点左侧栏「定位 PCL」手动选择");
        else
            AppendLog($"PCL：{_pclDir}");
        AppendLog($"游戏目录：{_gameDir}");

        LogToggle.IsChecked = false;
        ApplyLogVisibility();

        ThreadsSlider.Value = Math.Clamp(_config.Threads, 1, 32);
        ThreadsLabel.Text = ((int)ThreadsSlider.Value).ToString();
        FullVerifyBox.IsChecked = _config.FullVerify;
        JavaPathBox.Text = _config.JavaPath ?? "";
        GameDirBox.Text = _gameDir;
        UpdateGameDirHint();

        UpdateStepChrome();
        UpdateFooter();
        SyncThemeOptions();
        UpdateRail();
        PlayEntrance();
        ApplyFilter();
        await LoadVersionsAsync();
    }

    // ================= 动画 =================

    private void PlayEntrance()
    {
        FadeSlideIn(TitleBar, 0, 240, 0);
        FadeSlideIn(Rail, 8, 260, 40);
        FadeSlideIn(ContentArea, 10, 300, 80);
        FadeSlideIn(FooterBar, 8, 260, 120);
    }

    private static TranslateTransform EnsureTranslate(FrameworkElement element)
    {
        if (element.RenderTransform is TranslateTransform translate && !translate.IsFrozen)
            return translate;

        translate = new TranslateTransform();
        element.RenderTransform = translate;
        return translate;
    }

    private static ScaleTransform EnsureScale(FrameworkElement element)
    {
        if (element.RenderTransform is ScaleTransform scale && !scale.IsFrozen)
            return scale;

        scale = new ScaleTransform(1, 1);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = scale;
        return scale;
    }

    private void FadeSlideIn(FrameworkElement element, double fromY = 12, int milliseconds = 240,
        int delayMilliseconds = 0)
    {
        var translate = EnsureTranslate(element);
        var delay = TimeSpan.FromMilliseconds(delayMilliseconds);
        var duration = TimeSpan.FromMilliseconds(milliseconds);

        element.Opacity = 0;
        translate.Y = fromY;
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration)
        {
            BeginTime = delay,
            EasingFunction = EaseOut,
        });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, duration)
        {
            BeginTime = delay,
            EasingFunction = EaseOut,
        });
    }

    private void FadeIn(FrameworkElement element, int milliseconds = 180)
    {
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1,
            TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = EaseOut });
    }

    private void FadeScaleIn(FrameworkElement element, int milliseconds = 220)
    {
        var scale = EnsureScale(element);
        element.Opacity = 0;
        scale.ScaleX = 0.96;
        scale.ScaleY = 0.96;

        var duration = TimeSpan.FromMilliseconds(milliseconds);
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = EaseOut });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.96, 1, duration) { EasingFunction = EaseOut });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.96, 1, duration) { EasingFunction = EaseOut });
    }

    private void PulseStepDot(int index)
    {
        if (index < 0 || index >= _stepDots.Length) return;

        var dot = _stepDots[index];
        dot.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = EnsureScale(dot);
        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(0.86, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(1.08,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(110)), EaseOut));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(1,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(240)), EaseOut));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation.Clone());
    }

    private static void StartProgressSheen(ProgressBar bar)
    {
        if (bar.Template?.FindName("SheenShift", bar) is not TranslateTransform shift) return;
        shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-160, 1600,
            TimeSpan.FromSeconds(1.8))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        });
    }

    private static void StopProgressSheen(ProgressBar bar)
    {
        if (bar.Template?.FindName("SheenShift", bar) is TranslateTransform shift)
            shift.BeginAnimation(TranslateTransform.XProperty, null);
    }

    private void ShowSettingsOverlay()
    {
        _settingsTransition++;
        SettingsOverlay.Visibility = Visibility.Visible;
        SettingsOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1,
            TimeSpan.FromMilliseconds(180)) { EasingFunction = EaseOut });
        FadeScaleIn(SettingsCard, 220);
    }

    private static void ResetScroll(ScrollViewer viewer)
    {
        if (viewer is null) return;
        viewer.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            viewer.UpdateLayout();
            viewer.ScrollToTop();
        }));
    }

    private void StartStatusPulse()
    {
        StatusDot.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1,
            TimeSpan.FromMilliseconds(760))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = EaseOut,
        });
    }

    private void StopStatusPulse()
    {
        StatusDot.BeginAnimation(OpacityProperty, null);
        StatusDot.Opacity = 1;
    }

    private void HideSettingsOverlay()
    {
        var token = ++_settingsTransition;
        var fade = new DoubleAnimation(SettingsOverlay.Opacity, 0,
            TimeSpan.FromMilliseconds(140)) { EasingFunction = EaseOut };
        fade.Completed += (_, _) =>
        {
            if (token != _settingsTransition) return;
            SettingsOverlay.Visibility = Visibility.Collapsed;
            SettingsOverlay.Opacity = 1;
        };
        SettingsOverlay.BeginAnimation(OpacityProperty, fade);
    }

    private string ResolveInitialPclDirectory()
    {
        // 1) 上次记住的
        if (!string.IsNullOrWhiteSpace(_config.PclDirectory) &&
            File.Exists(Path.Combine(_config.PclDirectory, "PCL2.exe")))
            return _config.PclDirectory;

        // 2) exe 所在目录及上级
        var nearby = PclPaths.FindPclDirectory(AppContext.BaseDirectory)
                     ?? PclPaths.FindPclDirectory(Directory.GetCurrentDirectory());
        if (nearby is not null) return nearby;

        // 3) 桌面 / 下载 / 文档 / Program Files 等常见位置
        return PclPaths.ProbeForPclDirectory() ?? "";
    }

    private string ResolveInitialGameDir()
    {
        if (!string.IsNullOrWhiteSpace(_config.GameDirectory) && Directory.Exists(_config.GameDirectory))
            return _config.GameDirectory;

        var baseDir = string.IsNullOrEmpty(_pclDir) ? AppContext.BaseDirectory : _pclDir;
        return PclPaths.ResolveGameDir(baseDir);
    }

    private void UpdateHeader()
    {
        GameDirText.Text = _gameDir;
        PclStatusText.Text = string.IsNullOrEmpty(_pclDir)
            ? "未检测到 PCL2.exe —— 点左侧栏「定位 PCL」手动选择"
            : $"PCL：{_pclDir}";
    }

    private void UpdateGameDirHint()
    {
        if (GameDirHintText is null) return;
        GameDirHintText.Text = Directory.Exists(_gameDir)
            ? "会复用这个目录里已下载的库和资源。"
            : "目录还不存在，安装时会创建。";
    }

    // ================= 第一步：版本列表 =================

    private async Task LoadVersionsAsync()
    {
        SetVersionLoading(true);
        try
        {
            SetStatus("正在获取版本列表…");
            AppendLog("正在获取 Mojang 版本清单…");
            _allVersions = await _versionService.ListVersionsAsync(CancellationToken.None);
            AppendLog($"共 {_allVersions.Count} 个版本");
            EmptyText.Text = "没有匹配的版本";
            RetryButton.Visibility = Visibility.Collapsed;
            ApplyFilter();
            SetStatus($"共 {_allVersions.Count} 个版本");
        }
        catch (Exception ex)
        {
            AppendLog("获取版本列表失败：" + ex.Message);
            SetStatus("获取版本列表失败（检查网络）");
            CountText.Text = "—";
            EmptyText.Text = "版本清单获取失败，检查网络后重试";
            EmptyPanel.Visibility = Visibility.Visible;
            RetryButton.Visibility = Visibility.Visible;
            RetryButton.IsEnabled = true;
        }
        finally
        {
            SetVersionLoading(false);
        }
    }

    private async void RetryVersions_Click(object sender, RoutedEventArgs e)
    {
        await LoadVersionsAsync();
    }

    private void SetVersionLoading(bool loading)
    {
        if (LoadingPanel is null) return;

        LoadingPanel.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        if (!loading) return;

        EmptyPanel.Visibility = Visibility.Collapsed;
        RetryButton.Visibility = Visibility.Collapsed;
        RetryButton.IsEnabled = false;
        CountText.Text = "…";
    }

    private void ApplyFilter(bool preserveSelection = false)
    {
        if (!_ready) return;

        var keepId = preserveSelection ? (VersionList.SelectedItem as VersionListItem)?.Id : null;

        IEnumerable<VersionListItem> items;
        if (TabInstalled.IsChecked == true)
        {
            items = PclPaths.EnumerateInstalledVersions(_gameDir)
                .Select(id => new VersionListItem(id, "已安装", "installed"))
                .OrderByDescending(i => i.Id, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            var type = TabSnapshot.IsChecked == true ? "snapshot" : "release";
            items = _allVersions
                .Where(v => v.Type == type)
                .OrderByDescending(v => v.ReleaseTime)
                .Select(v => new VersionListItem(
                    v.Id,
                    v.ReleaseTime == default ? "" : v.ReleaseTime.ToLocalTime().ToString("yyyy-MM-dd"),
                    v.Type));
        }

        var keyword = SearchBox.Text?.Trim();
        if (!string.IsNullOrEmpty(keyword))
            items = items.Where(i => i.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        _suspendSelection = true;
        try
        {
            _view.Clear();
            foreach (var item in items.Take(400)) _view.Add(item);

            CountText.Text = $"{_view.Count} 个";
            EmptyPanel.Visibility = _view.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            VersionList.SelectedIndex = -1;

            if (keepId is not null)
            {
                var match = _view.FirstOrDefault(i => i.Id.Equals(keepId, StringComparison.OrdinalIgnoreCase));
                if (match is not null) VersionList.SelectedItem = match;
            }
            else if (_view.Count > 0)
            {
                // 首次加载 / 切标签 / 搜索时默认选中第一条，右侧配置栏不会空着
                VersionList.SelectedIndex = 0;
            }
        }
        finally
        {
            _suspendSelection = false;
        }

        if (VersionList.SelectedItem is VersionListItem selected)
            _ = UpdateSelectionAsync();
        else
            ResetSelectionUi();

        UpdateFooter();
    }

    private void ResetSelectionUi()
    {
        SelectedVersionText.Text = "—";
        KindText.Text = "—";
        JavaText.Text = "Java —";
        StateText.Text = "—";
        InstalledBox.Visibility = Visibility.Collapsed;
        RepairPanel.Visibility = Visibility.Collapsed;
        UpdateFooter();
    }

    private async void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        ApplyFilter();
        await Task.CompletedTask;
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        ApplyFilter();
    }

    private async void VersionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _suspendSelection) return;
        await UpdateSelectionAsync();
    }

    private async Task UpdateSelectionAsync()
    {
        if (VersionList.SelectedItem is not VersionListItem item)
        {
            ResetSelectionUi();
            return;
        }

        SelectedVersionText.Text = item.Id;

        if (item.Type == "installed")
        {
            InstalledBox.Visibility = Visibility.Visible;
            RepairPanel.Visibility = Visibility.Collapsed;
            RepairButton.IsEnabled = !_busy;
            HintText.Text = "点「校验 / 修复」检查文件完整性，缺什么补什么。";
            LoaderVersionBox.ItemsSource = null;
            LoaderVersionBox.IsEnabled = false;
            StateText.Text = "已安装";
            KindText.Text = "检测中…";
            JavaText.Text = "Java —";
            await FillInstalledDetailsAsync(item.Id);
            UpdateFooter();
            return;
        }

        if (!string.Equals(_currentVersionId, item.Id, StringComparison.OrdinalIgnoreCase))
        {
            _currentVersionId = item.Id;
            _nameTouched = false;
            if (_step == 1) _maxStep = 1;
            UpdateStepChrome();
        }

        InstalledBox.Visibility = Visibility.Collapsed;
        RepairPanel.Visibility = Visibility.Collapsed;
        KindText.Text = LoaderDisplayName(GetSelectedLoader());
        JavaText.Text = "Java 安装时确定";
        StateText.Text = "未安装";
        HintText.Text = "文件会写入 PCL 的游戏目录。完成后回到 PCL 刷新版本列表即可启动；如果没显示，重启 PCL。";
        LoaderVersionBox.ItemsSource = null;
        LoaderVersionBox.IsEnabled = false;
        LoaderVersionHint.Text = "进入下一步选择加载器后再获取版本列表";
        UpdateNameHint();
        UpdateFooter();
    }

    private async Task FillInstalledDetailsAsync(string versionId)
    {
        await Task.Yield();
        try
        {
            var chain = VersionChain.Load(_gameDir, versionId);
            if (chain.Json.Count == 0)
            {
                KindText.Text = "未知";
                JavaText.Text = "Java —";
                StateText.Text = "文件缺失";
                return;
            }

            var main = McJson.Str(chain.Json, "mainClass") ?? "";
            var libraries = chain.Json["libraries"]?.ToJsonString() ?? "";
            var kind = libraries.Contains("net.neoforged", StringComparison.OrdinalIgnoreCase) ? "NeoForge"
                : libraries.Contains("net.minecraftforge", StringComparison.OrdinalIgnoreCase) ? "Forge"
                : libraries.Contains("net.fabricmc", StringComparison.OrdinalIgnoreCase) ? "Fabric"
                : main.Contains("fabric", StringComparison.OrdinalIgnoreCase) ? "Fabric"
                : "原版";

            KindText.Text = kind;
            JavaText.Text = "Java " + McJson.JavaMajor(chain.Json);
            StateText.Text = "已安装";
        }
        catch
        {
            KindText.Text = "未知";
            JavaText.Text = "Java —";
            StateText.Text = "已安装";
        }
    }

    // ================= 步骤导航 =================

    private void GoToStep(int target)
    {
        if (_busy) return;
        if (target < 1 || target > LastStep) return;
        if (target > _step + 1) return;

        if (target < 5 && _completed)
        {
            _completed = false;
            _installStarted = false;
            _maxStep = Math.Max(1, target);
        }

        _step = target;
        _maxStep = Math.Max(_maxStep, target);
        ShowStep(target);
        UpdateStepChrome();
        UpdateFooter();
        _ = OnStepEnteredAsync(target);
    }

    private void ShowStep(int step)
    {
        var panels = new FrameworkElement[] { Step1Panel, Step2Panel, Step3Panel, Step4Panel, Step5Panel };
        for (var i = 0; i < panels.Length; i++)
        {
            var visible = i + 1 == step;
            panels[i].Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (!visible) panels[i].Opacity = 1;
        }

        var current = panels[step - 1];
        var translate = EnsureTranslate(current);
        current.Opacity = 0;
        translate.Y = 14;
        current.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = EaseOut });
        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = EaseOut });
        PulseStepDot(step - 1);
    }

    private async Task OnStepEnteredAsync(int step)
    {
        try
        {
            switch (step)
            {
                case 1:
                    ApplyFilter(preserveSelection: true);
                    SetStatus("选一个 Minecraft 版本");
                    break;

                case 2:
                    if (VersionList.SelectedItem is VersionListItem item && item.Type != "installed")
                    {
                        KindText.Text = LoaderDisplayName(GetSelectedLoader());
                        if (LoaderVersionBox.Items.Count <= 1)
                            await RefreshLoaderVersionsAsync(item.Id);
                        else
                            UpdateFooter();
                    }
                    SetStatus("选加载器；不确定就用默认的「自动」");
                    break;

                case 3:
                    LoadAdvancedSettings();
                    ResetScroll(Step3Scroll);
                    SetStatus("高级设置都有默认值，直接「继续」也可以");
                    break;

                case 4:
                    PrepareNameStep();
                    ResetScroll(Step4Scroll);
                    SetStatus("确认版本名，然后点「下载」");
                    break;

                case 5:
                    PrepareDownloadStep();
                    ResetScroll(Step5Scroll);
                    SetStatus("点「下载」开始安装");
                    break;
            }
        }
        catch (Exception ex)
        {
            AppendLog($"切换步骤失败：{ex.Message}");
        }
    }

    private void UpdateStepChrome()
    {
        var accentGradient = ThemeBrush("AccentGradient");
        var accentSoft = ThemeBrush("AccentSoft");
        var idle = ThemeBrush("StepDotIdle");
        var dotBorder = ThemeBrush("StepDotBorder");
        var glow = ThemeEffect("StepGlow");
        var accent = ThemeBrush("Accent");
        var muted = ThemeBrush("Muted");
        var success = ThemeBrush("Success");
        var ink = ThemeBrush("Ink");
        var inkSoft = ThemeBrush("InkSoft");
        var stepLine = ThemeBrush("StepLine");

        for (var i = 0; i < LastStep; i++)
        {
            var number = i + 1;
            var current = number == _step;
            var done = number < _step || (_completed && number == LastStep);
            var isSuccess = _completed && number == LastStep;

            _stepDots[i].Background = isSuccess || current ? accentGradient
                : done ? accentSoft
                : idle;
            _stepDots[i].BorderBrush = isSuccess || current ? Brushes.Transparent : dotBorder;
            _stepDots[i].Effect = current ? glow : null;

            _stepNumbers[i].Foreground = isSuccess || current ? Brushes.White : done ? accent : muted;
            _stepNumbers[i].Text = isSuccess || (done && !current) ? "✓" : number.ToString();

            _stepLabels[i].Foreground = isSuccess ? success : current ? ink : done ? inkSoft : muted;
            _stepLabels[i].FontWeight = current || isSuccess ? FontWeights.SemiBold : FontWeights.Normal;

            if (i < _stepLines.Length)
            {
                var passed = number < _step || (_completed && number < LastStep);
                _stepLines[i].Background = passed ? accentGradient : stepLine;
            }

            var reachable = !_busy && !_completed && number <= _maxStep;
            _stepButtons[i].IsEnabled = reachable;
            _stepButtons[i].ToolTip = reachable
                ? (number == _step ? "当前步骤" : "回到这一步")
                : "先按顺序完成前面的步骤";
        }
    }

    private void UpdateFooter()
    {
        BackButton.IsEnabled = !_busy && _step > 1;
        CancelButton.Visibility = _busy ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Visibility = _busy ? Visibility.Collapsed : Visibility.Visible;
        NextButton.IsEnabled = !_busy;

        switch (_step)
        {
            case 1:
                NextButton.Content = "继续";
                NextButton.IsEnabled = !_busy && VersionList.SelectedItem is VersionListItem { Type: not "installed" };
                break;

            case 2:
                NextButton.Content = "继续";
                NextButton.IsEnabled = !_busy && CanLeaveLoaderStep();
                break;

            case 3:
                NextButton.Content = "继续";
                NextButton.IsEnabled = !_busy;
                break;

            case 4:
                NextButton.Content = "下载";
                NextButton.IsEnabled = !_busy && (NameBox.Text?.Trim().Length ?? 0) > 0;
                break;

            case 5:
                if (_completed)
                {
                    NextButton.Content = "再装一个";
                    NextButton.IsEnabled = !_busy;
                }
                else if (_installStarted)
                {
                    NextButton.Content = "重试下载";
                    NextButton.IsEnabled = !_busy;
                }
                else
                {
                    NextButton.Content = "下载";
                    NextButton.IsEnabled = !_busy;
                }
                break;
        }

        UpdateRail();
    }

    private bool CanLeaveLoaderStep()
    {
        if (VersionList.SelectedItem is not VersionListItem item || item.Type == "installed") return false;
        if (GetSelectedLoader() == "vanilla") return true;
        if (LoaderVersionBox.SelectedItem is LoaderChoice { Version: "__none__" }) return false;
        return true;
    }

    private void StepBar_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _completed) return;
        if (sender is not Button button || button.Tag is not string tag || !int.TryParse(tag, out var target)) return;
        if (target == _step) return;

        if (target > _step)
        {
            SetStatus("往前走请点右下角的「继续」");
            return;
        }

        PersistAdvancedSettingsBeforeLeaving();
        GoToStep(target);
    }

    /// <summary>从第三步返回时，把能保存的高级设置先落盘；无效值还原，不拦着返回。</summary>
    private void PersistAdvancedSettingsBeforeLeaving()
    {
        if (_step != 3 || _busy) return;
        if (ApplyAdvancedSettings()) return;

        GameDirBox.Text = _gameDir;
        JavaPathBox.Text = _config.JavaPath ?? "";
        ThreadsSlider.Value = Math.Clamp(_config.Threads, 1, 32);
        FullVerifyBox.IsChecked = _config.FullVerify;
        SetStatus("高级设置有无效项，已还原为上次的配置");
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        switch (_step)
        {
            case 1:
                if (VersionList.SelectedItem is not VersionListItem item)
                {
                    SetStatus("先在左边选一个版本");
                    return;
                }
                if (item.Type == "installed")
                {
                    SetStatus("已安装版本请用左侧栏的「校验 / 修复」");
                    return;
                }
                GoToStep(2);
                break;

            case 2:
                if (!CanLeaveLoaderStep())
                {
                    SetStatus("这个版本没有可用的加载器，退回上一步换个版本");
                    return;
                }
                GoToStep(3);
                break;

            case 3:
                if (!ApplyAdvancedSettings()) return;
                GoToStep(4);
                break;

            case 4:
                GoToStep(5);
                break;

            case 5:
                if (_completed)
                {
                    ResetWizard();
                    return;
                }
                await StartInstallAsync();
                break;
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _step <= 1) return;
        PersistAdvancedSettingsBeforeLeaving();
        GoToStep(_step - 1);
    }

    private void ResetWizard()
    {
        _completed = false;
        _installStarted = false;
        _maxStep = 1;
        _nameTouched = false;
        _lastInstallVersionId = null;
        _step = 1;

        ResultBox.Visibility = Visibility.Collapsed;
        ResultBox.Opacity = 1;
        ResultText.Text = "";
        FeatureTips.Visibility = Visibility.Visible;
        StatusDot.Background = ThemeBrush("StatusIdle");
        PhaseText.Text = "准备就绪";
        PercentText.Text = "0%";
        ProgressBar.Value = 0;
        DownloadSpeedText.Text = "—";
        EtaText.Text = "";
        CurrentFileText.Text = "—";
        DetailText.Text = "点右下角「下载」开始；安装时会实时显示下载速度与当前文件。";

        ShowStep(1);
        UpdateStepChrome();
        UpdateFooter();
        ApplyFilter(preserveSelection: false);
        SetStatus("再装一个：重新选择版本");
    }

    // ================= 第二步：加载器 =================

    private string GetSelectedLoader()
    {
        if (LoaderFabric.IsChecked == true) return "fabric";
        if (LoaderForge.IsChecked == true) return "forge";
        if (LoaderNeoForge.IsChecked == true) return "neoforge";
        return "vanilla";
    }

    private static string LoaderDisplayName(string loader) => loader switch
    {
        "fabric" => "Fabric",
        "forge" => "Forge",
        "neoforge" => "NeoForge",
        _ => "原版",
    };

    private string LoaderVersionDisplay()
    {
        if (GetSelectedLoader() == "vanilla") return "不需要";
        var choice = LoaderVersionBox.SelectedItem as LoaderChoice;
        if (choice is null) return "自动";
        if (choice.Version == "__none__") return "无可用版本";
        return string.IsNullOrEmpty(choice.Version) ? "自动（推荐）" : choice.Version;
    }

    private async void Loader_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready || _busy) return;
        if (VersionList.SelectedItem is not VersionListItem item || item.Type == "installed") return;

        _nameTouched = false;
        _maxStep = Math.Min(_maxStep, Math.Max(_step, 2));
        KindText.Text = LoaderDisplayName(GetSelectedLoader());
        UpdateStepChrome();
        await RefreshLoaderVersionsAsync(item.Id);
    }

    private async Task RefreshLoaderVersionsAsync(string mcVersion)
    {
        _loaderCts?.Cancel();
        _loaderCts?.Dispose();
        _loaderCts = new CancellationTokenSource();
        var token = _loaderCts.Token;

        var loader = GetSelectedLoader();
        var choices = new List<LoaderChoice>();

        OptiFineCheck.IsEnabled = !_busy && loader == "forge";
        if (loader != "forge")
        {
            OptiFineCheck.IsChecked = false;
            OptiFineVersionBox.ItemsSource = null;
            OptiFineVersionBox.IsEnabled = false;
        }

        if (loader == "vanilla")
        {
            choices.Add(new LoaderChoice("不需要", null));
            LoaderVersionBox.ItemsSource = choices;
            LoaderVersionBox.SelectedIndex = 0;
            LoaderVersionBox.IsEnabled = false;
            LoaderVersionHint.Text = "原版不需要加载器版本";
            UpdateFooter();
            return;
        }

        choices.Add(new LoaderChoice("自动（推荐）", null));
        LoaderVersionBox.ItemsSource = choices;
        LoaderVersionBox.SelectedIndex = 0;
        LoaderVersionBox.IsEnabled = false;
        LoaderVersionHint.Text = $"正在获取 {LoaderDisplayName(loader)} 版本列表…";
        SetStatus($"正在获取 {LoaderDisplayName(loader)} 版本列表…");

        try
        {
            if (loader == "fabric")
            {
                var loaders = await _fabric.ListLoadersAsync(mcVersion, token);
                foreach (var version in loaders.Take(80))
                    choices.Add(new LoaderChoice(
                        version.Stable ? $"{version.Version}（稳定）" : version.Version, version.Version));
                LoaderVersionHint.Text = $"共 {loaders.Count} 个版本，默认自动选稳定版";
            }
            else
            {
                var installer = loader == "forge" ? _forge : _neoForge;
                var versions = await installer.ListVersionsAsync(mcVersion, token);
                if (versions.Count == 0)
                {
                    choices.Add(new LoaderChoice("（该版本没有对应加载器）", "__none__"));
                    AppendLog($"{loader} 没有支持 {mcVersion} 的版本");
                    LoaderVersionHint.Text = "这个 Minecraft 版本没有可用的加载器";
                }
                else
                {
                    foreach (var version in versions.Take(100))
                        choices.Add(new LoaderChoice(version, version));
                    LoaderVersionHint.Text = $"共 {versions.Count} 个版本";
                }
            }

            LoaderVersionBox.ItemsSource = null;
            LoaderVersionBox.ItemsSource = choices;
            LoaderVersionBox.SelectedIndex = 0;
            LoaderVersionBox.IsEnabled = choices.Count > 1 && !_busy;
            SetStatus($"{LoaderDisplayName(loader)}：{Math.Max(0, choices.Count - 1)} 个版本可选");
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            AppendLog($"获取 {loader} 版本列表失败：{ex.Message}");
            LoaderVersionHint.Text = "获取版本列表失败，可以继续用自动选择";
            SetStatus("获取加载器版本失败（仍可自动安装）");
            LoaderVersionBox.IsEnabled = false;
        }

        UpdateFooter();
    }

    private void LoaderVersion_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (!_nameTouched) UpdateNameHint();
        UpdateFooter();
    }

    private async void OptiFine_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready || _busy) return;
        if (GetSelectedLoader() != "forge") return;
        if (VersionList.SelectedItem is not VersionListItem item || item.Type == "installed") return;

        var choices = new List<LoaderChoice> { new("最新（推荐）", null) };
        OptiFineVersionBox.ItemsSource = choices;
        OptiFineVersionBox.SelectedIndex = 0;
        OptiFineVersionBox.IsEnabled = false;
        LoaderVersionHint.Text = "正在获取 OptiFine 版本…";
        SetStatus("正在获取 OptiFine 版本…");

        try
        {
            var versions = await _optiFine.ListVersionsAsync(item.Id, CancellationToken.None);
            foreach (var name in versions.Take(40))
            {
                var label = name.Replace($"OptiFine_{item.Id}_", "").Replace(".jar", "");
                choices.Add(new LoaderChoice(label, name));
            }

            if (versions.Count == 0)
                choices.Add(new LoaderChoice("（没有找到可选版本）", "__none__"));
            SetStatus($"OptiFine：{versions.Count} 个版本可选");
        }
        catch (Exception ex)
        {
            AppendLog("获取 OptiFine 版本失败：" + ex.Message);
            SetStatus("获取 OptiFine 版本失败");
        }

        OptiFineVersionBox.ItemsSource = null;
        OptiFineVersionBox.ItemsSource = choices;
        OptiFineVersionBox.SelectedIndex = 0;
        OptiFineVersionBox.IsEnabled = choices.Count > 1 && !_busy;

        _maxStep = Math.Min(_maxStep, Math.Max(_step, 2));
        UpdateStepChrome();
        UpdateFooter();
    }

    private void OptiFine_Unchecked(object sender, RoutedEventArgs e)
    {
        if (OptiFineVersionBox is not null) OptiFineVersionBox.IsEnabled = false;
        UpdateFooter();
    }

    // ================= 第三步：高级设置 =================

    private void LoadAdvancedSettings()
    {
        ThreadsSlider.Value = Math.Clamp(_config.Threads, 1, 32);
        ThreadsLabel.Text = ((int)ThreadsSlider.Value).ToString();
        FullVerifyBox.IsChecked = _config.FullVerify;
        JavaPathBox.Text = _config.JavaPath ?? "";
        GameDirBox.Text = _gameDir;
        UpdateGameDirHint();
        _ = RefreshJavaHintAsync();
    }

    private bool ApplyAdvancedSettings()
    {
        var dir = GameDirBox.Text?.Trim() ?? "";
        if (dir.Length == 0)
        {
            SetStatus("请填写安装位置");
            return false;
        }

        string full;
        try
        {
            full = Path.GetFullPath(dir);
        }
        catch
        {
            SetStatus("安装位置路径无效");
            return false;
        }

        var java = JavaPathBox.Text?.Trim() ?? "";
        if (java.Length > 0 && !File.Exists(java))
        {
            SetStatus("Java 路径不存在：" + java);
            AppendLog("Java 路径不存在：" + java);
            return false;
        }

        if (!string.Equals(full, _gameDir, StringComparison.OrdinalIgnoreCase))
        {
            _gameDir = full;
            _config.GameDirectory = full;
            GameDirText.Text = full;
            UpdateGameDirHint();
            ApplyFilter(preserveSelection: true);
        }

        _config.Threads = (int)ThreadsSlider.Value;
        _config.FullVerify = FullVerifyBox.IsChecked == true;
        _config.JavaPath = java.Length == 0 ? null : java;
        _config.Save();

        AppendLog($"高级设置：线程 {_config.Threads}，全量校验 {_config.FullVerify}，" +
                  $"Java {(string.IsNullOrEmpty(_config.JavaPath) ? "自动" : _config.JavaPath)}");
        return true;
    }

    private void Threads_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ThreadsLabel is not null) ThreadsLabel.Text = ((int)e.NewValue).ToString();
    }

    private async Task RefreshJavaHintAsync()
    {
        _javaDiscovery ??= Task.Run(() => new JavaLocator(_http).Discover());
        try
        {
            var list = await _javaDiscovery;
            if (!_ready) return;
            JavaHintText.Text = list.Count == 0
                ? "没有检测到 Java。Forge / NeoForge 需要 Java，缺失时会自动下载 JRE（约 45 MB）。"
                : "检测到 " + string.Join("，", list.Take(3).Select(j => $"Java {j.Major}")) + "（留空自动选择）";
        }
        catch
        {
            JavaHintText.Text = "Java 探测失败，留空会在需要时自动下载。";
        }
    }

    private void BrowseJava_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 java.exe",
            Filter = "Java 可执行文件 (java.exe)|java.exe|可执行文件 (*.exe)|*.exe",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() == true) JavaPathBox.Text = dialog.FileName;
    }

    // ================= 第四步：版本名 =================

    private void PrepareNameStep()
    {
        var loader = GetSelectedLoader();
        var canRename = loader is "vanilla" or "fabric";
        var installed = PclPaths.EnumerateInstalledVersions(_gameDir).ToList();

        if (!_nameTouched || string.IsNullOrWhiteSpace(NameBox.Text))
            SetNameProgrammatically(VersionNames.MakeUnique(SuggestName(), installed));

        NameBox.IsEnabled = canRename && !_busy;
        RenameButton.IsEnabled = canRename && !_busy;
        NameNoteBanner.Visibility = canRename ? Visibility.Collapsed : Visibility.Visible;
        NameNoteText.Text = canRename
            ? ""
            : $"{LoaderDisplayName(loader)} 的版本名由官方安装器生成，这里的名字不会生效。";

        UpdateNameHint();
        UpdateSummary();
        UpdateFooter();
    }

    private void SetNameProgrammatically(string name)
    {
        _settingName = true;
        try
        {
            NameBox.Text = name;
        }
        finally
        {
            _settingName = false;
        }
        UpdateNameHint();
    }

    private string SuggestName()
    {
        var mc = (VersionList.SelectedItem as VersionListItem)?.Id ?? "";
        var loader = GetSelectedLoader();
        var loaderVersion = (LoaderVersionBox.SelectedItem as LoaderChoice)?.Version;
        return VersionNames.Suggest(mc, loader == "vanilla" ? null : loader, loaderVersion);
    }

    private void UpdateNameHint()
    {
        if (NameHint is null) return;
        var installed = PclPaths.EnumerateInstalledVersions(_gameDir).ToList();
        var suggested = VersionNames.MakeUnique(SuggestName(), installed);
        var typed = NameBox?.Text?.Trim() ?? "";
        if (typed.Length == 0)
        {
            NameHint.Text = $"默认名称：{suggested}";
            return;
        }

        var conflict = installed.Contains(typed, StringComparer.OrdinalIgnoreCase);
        NameHint.Text = conflict
            ? $"「{typed}」已存在，将改用：{VersionNames.MakeUnique(typed, installed)}"
            : $"将使用：{typed}";
    }

    private void Name_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _settingName) return;
        _nameTouched = true;
        UpdateNameHint();
        UpdateFooter();
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        var installed = PclPaths.EnumerateInstalledVersions(_gameDir).ToList();
        var suggested = NameBox.Text?.Trim() is { Length: > 0 } typed
            ? typed
            : VersionNames.MakeUnique(SuggestName(), installed);

        var dialog = new RenameWindow(suggested, installed) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _nameTouched = true;
            SetNameProgrammatically(dialog.VersionName);
            _nameTouched = true;
            UpdateSummary();
            UpdateFooter();
        }
    }

    private void UpdateSummary()
    {
        var item = VersionList.SelectedItem as VersionListItem;
        var loader = GetSelectedLoader();
        var optifine = loader == "forge" && OptiFineCheck.IsChecked == true;
        var optifineChoice = OptiFineVersionBox.SelectedItem as LoaderChoice;
        var optifineText = !optifine
            ? "不安装"
            : string.IsNullOrEmpty(optifineChoice?.Version) || optifineChoice?.Version == "__none__"
                ? "最新版"
                : optifineChoice!.Version!.Replace("OptiFine_" + (item?.Id ?? "") + "_", "").Replace(".jar", "");

        var loaderText = loader == "vanilla"
            ? "原版（不装加载器）"
            : $"{LoaderDisplayName(loader)} · {LoaderVersionDisplay()}";

        var lines = new[]
        {
            $"Minecraft：{item?.Id ?? "—"}（{item?.Badge ?? "—"}）",
            $"加载器：{loaderText}",
            $"OptiFine：{optifineText}",
            $"线程：{(int)ThreadsSlider.Value}    全量校验：{(FullVerifyBox.IsChecked == true ? "开" : "关")}",
            $"Java：{(string.IsNullOrWhiteSpace(JavaPathBox.Text) ? "自动探测" : JavaPathBox.Text!.Trim())}",
            $"安装到：{_gameDir}",
        };
        SummaryText.Text = string.Join(Environment.NewLine, lines);
    }

    // ================= 第五步：下载与安装 =================

    private void PrepareDownloadStep()
    {
        var item = VersionList.SelectedItem as VersionListItem;
        var loader = GetSelectedLoader();
        var effectiveName = GetEffectiveVersionName(out var canRename);

        TargetSummaryText.Text = item is null
            ? "—"
            : $"{item.Id} · {LoaderDisplayName(loader)} → {(canRename ? effectiveName : "官方安装器命名")}";

        if (_installStarted) return;

        PhaseText.Text = "准备就绪";
        PercentText.Text = "0%";
        ProgressBar.Value = 0;
        DownloadSpeedText.Text = "—";
        EtaText.Text = "";
        CurrentFileText.Text = "—";
        DetailText.Text = "点右下角「下载」开始；安装时会实时显示下载速度与当前文件。";
        ResultBox.Visibility = Visibility.Collapsed;
        ResultBox.Opacity = 1;
        FeatureTips.Visibility = Visibility.Visible;
    }

    private string GetEffectiveVersionName(out bool canRename)
    {
        var loader = GetSelectedLoader();
        canRename = loader is "vanilla" or "fabric";
        var installed = PclPaths.EnumerateInstalledVersions(_gameDir).ToList();
        var raw = NameBox.Text?.Trim();
        if (string.IsNullOrEmpty(raw)) raw = SuggestName();
        return VersionNames.MakeUnique(raw, installed);
    }

    private async Task StartInstallAsync()
    {
        if (_busy) return;
        if (VersionList.SelectedItem is not VersionListItem item || item.Type == "installed")
        {
            SetStatus("请回到第一步选择要安装的版本");
            GoToStep(1);
            return;
        }

        var loader = GetSelectedLoader();
        var choice = LoaderVersionBox.SelectedItem as LoaderChoice;
        if (choice?.Version == "__none__")
        {
            ShowWarning("这个 Minecraft 版本没有可用的加载器版本。", "提示");
            return;
        }

        string? optifineVersion = null;
        if (loader == "forge" && OptiFineCheck.IsChecked == true)
        {
            var optifineChoice = OptiFineVersionBox.SelectedItem as LoaderChoice;
            if (optifineChoice?.Version == "__none__")
            {
                ShowWarning("这个版本没有可用的 OptiFine。", "提示");
                return;
            }
            optifineVersion = optifineChoice?.Version ?? "latest";
        }

        var installedNames = PclPaths.EnumerateInstalledVersions(_gameDir).ToList();
        var rawName = string.IsNullOrWhiteSpace(NameBox.Text) ? SuggestName() : NameBox.Text.Trim();
        var requestedName = VersionNames.MakeUnique(rawName, installedNames);
        // Forge / NeoForge 的版本名由官方安装器决定，这里无法改名
        var canRename = loader is "vanilla" or "fabric";
        var finalName = canRename ? requestedName : null;

        var options = new InstallOptions
        {
            Threads = Math.Clamp(_config.Threads, 1, 64),
            FullVerify = FullVerifyBox.IsChecked == true,
            Loader = loader == "vanilla" ? null : loader,
            LoaderVersion = choice?.Version,
            OptifineVersion = optifineVersion,
            JavaPath = _config.JavaPath,
            VersionName = finalName,
        };

        _opMode = OpMode.Install;
        _installStarted = true;
        _completed = false;
        _lastInstallVersionId = null;
        _operationCts = new CancellationTokenSource();
        ToggleBusy(true);

        TargetSummaryText.Text = $"{item.Id} · {LoaderDisplayName(loader)} → " +
                                 $"{(finalName is null ? "官方安装器命名" : finalName)}";
        PhaseText.Text = "准备中…";
        PercentText.Text = "0%";
        ProgressBar.Value = 0;
        DownloadSpeedText.Text = "—";
        EtaText.Text = "";
        CurrentFileText.Text = "—";
        DetailText.Text = "正在准备下载…";
        ResultBox.Visibility = Visibility.Collapsed;
        ResultBox.Opacity = 1;
        FeatureTips.Visibility = Visibility.Collapsed;

        AppendLog($"===== 开始安装 {item.Id}（{loader}）=====");
        if (!canRename) AppendLog($"{loader} 的版本名由官方安装器生成，改名设置不生效");
        var reporter = new UiReporter(this);
        StartProgressSheen(ProgressBar);

        try
        {
            var token = _operationCts.Token;
            var result = await Task.Run(
                () => _installer.InstallAsync(_gameDir, item.Id, options, reporter, token), token);

            _completed = true;
            _lastInstallVersionId = result.VersionId;
            AppendLog($"安装完成：{result.VersionId}（用时 {result.Elapsed.TotalSeconds:0.0}s）");
            PhaseText.Text = "完成";
            PercentText.Text = "100%";
            ProgressBar.Value = 1;
            DownloadSpeedText.Text = "—";
            EtaText.Text = "";
            CurrentFileText.Text = "—";
            DetailText.Text = $"库 {result.Libraries} 个，资源 {result.Assets} 个，共 " +
                              $"{MinecraftInstaller.FormatBytes(result.DownloadedBytes)}（已存在的文件自动复用）";
            ResultBox.Visibility = Visibility.Collapsed;
            ResultBox.Opacity = 1;
            ShowResult($"安装完成：{result.VersionId}\n目录：{result.VersionDirectory}\n\n" +
                       "回到 PCL 刷新版本列表即可启动；如果没显示，重启 PCL。");
            SetStatus("安装完成");
            if (TabInstalled.IsChecked == true) ApplyFilter();
        }
        catch (OperationCanceledException)
        {
            AppendLog("已取消");
            _installStarted = false;
            PhaseText.Text = "已取消";
            SetStatus("已取消");
            FeatureTips.Visibility = Visibility.Visible;
            ShowResult("已取消。已经下载的文件会保留，重新下载会接着用。");
        }
        catch (Exception ex)
        {
            AppendLog("安装失败：" + ex.Message);
            AppendLog(ex.ToString());
            PhaseText.Text = "安装失败";
            SetStatus("安装失败");
            ExpandLog();
            ShowResult("安装失败：" + ex.Message);
            ShowError("安装失败：\n" + ex.Message, "失败");
        }
        finally
        {
            StopProgressSheen(ProgressBar);
            ToggleBusy(false);
            _opMode = OpMode.None;
            _operationCts?.Dispose();
            _operationCts = null;
            UpdateFooter();
        }
    }

    private void ShowResult(string message)
    {
        ResultText.Text = message;
        if (ResultBox.Visibility != Visibility.Visible)
        {
            ResultBox.Visibility = Visibility.Visible;
            FadeSlideIn(ResultBox, 10, 240);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _operationCts?.Cancel();
        AppendLog("正在取消…");
        SetStatus("正在取消…");
    }

    private async void Repair_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || VersionList.SelectedItem is not VersionListItem item || item.Type != "installed") return;

        _opMode = OpMode.Repair;
        _operationCts = new CancellationTokenSource();
        ToggleBusy(true);

        RepairPanel.Visibility = Visibility.Visible;
        RepairResultText.Visibility = Visibility.Collapsed;
        RepairResultText.Opacity = 1;
        RepairResultText.Text = "";
        RepairProgressBar.Value = 0;
        RepairPhaseText.Text = "准备中…";
        RepairDetailText.Text = "正在检查文件";
        FadeSlideIn(RepairPanel, 8, 220);
        AppendLog($"===== 校验 / 修复 {item.Id} =====");
        RepairProgressBar.ApplyTemplate();
        StartProgressSheen(RepairProgressBar);

        var reporter = new UiReporter(this);
        try
        {
            // 这些值必须在 UI 线程取好：Task.Run 里再读控件会抛跨线程异常
            var token = _operationCts.Token;
            var gameDir = _gameDir;
            var versionId = item.Id;
            var threads = Math.Clamp(_config.Threads, 1, 64);
            var fullVerify = FullVerifyBox.IsChecked == true;

            var report = await Task.Run(
                () => _repair.RepairAsync(gameDir, versionId, threads, fullVerify, reporter, token), token);

            if (report.Success) RepairProgressBar.Value = 1;
            RepairPhaseText.Text = report.Success ? "文件完整" : "仍有缺失";
            if (report.Success) RepairDetailText.Text = "已检查完，所有文件都在。";
            RepairResultText.Text = report.Success
                ? $"校验通过，文件完整。\n（修复前：库缺 {report.MissingLibrariesBefore}，资源缺 {report.MissingAssetsBefore}）"
                : $"修复后仍有缺失：库 {report.MissingLibrariesAfter}，资源缺 {report.MissingAssetsAfter}\n" +
                  string.Join("\n", report.RemainingProblems.Take(6));
            RepairResultText.Visibility = Visibility.Visible;
            FadeIn(RepairResultText, 200);
            SetStatus(report.Success ? "校验通过" : "修复后仍有缺失");
            AppendLog(report.Success ? "校验通过，文件完整" : "修复后仍有缺失");
            if (report.Success) StateText.Text = "已安装";
        }
        catch (OperationCanceledException)
        {
            AppendLog("已取消");
            RepairPhaseText.Text = "已取消";
            RepairResultText.Text = "已取消，已下载的文件会保留。";
            RepairResultText.Visibility = Visibility.Visible;
            FadeIn(RepairResultText, 200);
        }
        catch (Exception ex)
        {
            AppendLog("修复失败：" + ex.Message);
            AppendLog(ex.ToString());
            RepairPhaseText.Text = "修复失败";
            RepairResultText.Text = "修复失败：" + ex.Message;
            RepairResultText.Visibility = Visibility.Visible;
            FadeIn(RepairResultText, 200);
            SetStatus("修复失败");
            ExpandLog();
            ShowError("修复失败：\n" + ex.Message, "失败");
        }
        finally
        {
            StopProgressSheen(RepairProgressBar);
            ToggleBusy(false);
            _opMode = OpMode.None;
            _operationCts?.Dispose();
            _operationCts = null;
            UpdateFooter();
        }
    }

    private void OpenVersionFolder_Click(object sender, RoutedEventArgs e)
    {
        var id = _lastInstallVersionId ?? (VersionList.SelectedItem as VersionListItem)?.Id;
        var dir = string.IsNullOrEmpty(id) ? _gameDir : PclPaths.VersionDir(_gameDir, id);
        OpenDirectory(dir);
    }

    // ================= 进度与日志 =================

    private void SetPhase(string phase)
    {
        AppendLog("== " + phase + " ==");
        if (_opMode == OpMode.Repair)
        {
            if (RepairPhaseText is not null) RepairPhaseText.Text = phase;
            return;
        }
        if (PhaseText is not null) PhaseText.Text = phase;
    }

    private void SetProgress(ProgressSnapshot snapshot)
    {
        if (_opMode == OpMode.Repair)
        {
            RepairProgressBar.Value = snapshot.Fraction;
            RepairDetailText.Text = DescribeProgress(snapshot);
            return;
        }

        ProgressBar.Value = snapshot.Fraction;
        PercentText.Text = (int)Math.Round(snapshot.Fraction * 100) + "%";
        DownloadSpeedText.Text = snapshot.BytesPerSecond > 1
            ? MinecraftInstaller.FormatBytes((long)snapshot.BytesPerSecond) + "/s"
            : "—";
        EtaText.Text = snapshot.Eta is { } eta ? "剩余 " + eta.ToString("mm\\:ss") : "";
        CurrentFileText.Text = string.IsNullOrEmpty(snapshot.CurrentFile)
            ? "—"
            : Truncate(snapshot.CurrentFile, 88);
        DetailText.Text = DescribeProgress(snapshot);
        if (!string.IsNullOrEmpty(snapshot.Phase)) PhaseText.Text = snapshot.Phase;
    }

    private static string DescribeProgress(ProgressSnapshot snapshot)
        => $"{snapshot.DoneFiles}/{snapshot.TotalFiles} 文件 · " +
           $"{MinecraftInstaller.FormatBytes(snapshot.DoneBytes)}/{MinecraftInstaller.FormatBytes(snapshot.TotalBytes)} · " +
           $"{MinecraftInstaller.FormatBytes((long)snapshot.BytesPerSecond)}/s";

    private void LogToggle_Changed(object sender, RoutedEventArgs e)
    {
        ApplyLogVisibility();
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        var text = LogBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("日志为空");
            return;
        }

        try
        {
            Clipboard.SetText(text);
            SetStatus("日志已复制");
        }
        catch (Exception ex)
        {
            SetStatus("复制日志失败");
            AppendLog("复制日志失败：" + ex.Message);
        }
    }

    private void SettingsOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (SettingsCard.IsMouseOver) return;
        HideSettingsOverlay();
    }

    private void ApplyLogVisibility()
    {
        if (LogRow is null || LogPanel is null) return;
        var show = LogToggle.IsChecked == true;

        if (show)
        {
            var alreadyVisible = LogPanel.Visibility == Visibility.Visible;
            LogRow.Height = new GridLength(210);
            LogPanel.Visibility = Visibility.Visible;
            if (!alreadyVisible) FadeSlideIn(LogPanel, 12, 220);
            return;
        }

        if (LogPanel.Visibility != Visibility.Visible)
        {
            LogRow.Height = new GridLength(0);
            return;
        }

        var fade = new DoubleAnimation(LogPanel.Opacity, 0, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = EaseOut,
        };
        fade.Completed += (_, _) =>
        {
            if (LogToggle.IsChecked == true) return;
            LogPanel.Visibility = Visibility.Collapsed;
            LogPanel.Opacity = 1;
            LogRow.Height = new GridLength(0);
        };
        LogPanel.BeginAnimation(OpacityProperty, fade);
    }

    private void ExpandLog()
    {
        if (LogToggle.IsChecked != true) LogToggle.IsChecked = true;
        ApplyLogVisibility();
    }

    private void SetStatus(string text)
    {
        if (StatusText is not null) StatusText.Text = text;
    }

    private void AppendLog(string line)
    {
        if (LogBox is null) return;
        if (++_logLines > 1500)
        {
            LogBox.Clear();
            _logLines = 1;
        }
        LogBox.AppendText(line + Environment.NewLine);
        LogBox.ScrollToEnd();
    }

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : "…" + text[^max..];

    private void ToggleBusy(bool busy)
    {
        _busy = busy;

        if (busy)
        {
            StatusDot.Background = ThemeBrush("Accent");
            StartStatusPulse();
        }
        else
        {
            StopStatusPulse();
            StatusDot.Background = _completed ? ThemeBrush("Success") : ThemeBrush("StatusIdle");
        }

        VersionList.IsEnabled = !busy;
        SearchBox.IsEnabled = !busy;
        TabRelease.IsEnabled = !busy;
        TabSnapshot.IsEnabled = !busy;
        TabInstalled.IsEnabled = !busy;

        LoaderVanilla.IsEnabled = !busy;
        LoaderFabric.IsEnabled = !busy;
        LoaderForge.IsEnabled = !busy;
        LoaderNeoForge.IsEnabled = !busy;
        LoaderVersionBox.IsEnabled = !busy && LoaderVersionBox.Items.Count > 1;
        OptiFineCheck.IsEnabled = !busy && GetSelectedLoader() == "forge";
        OptiFineVersionBox.IsEnabled = !busy && OptiFineCheck.IsChecked == true && OptiFineVersionBox.Items.Count > 1;

        ThreadsSlider.IsEnabled = !busy;
        FullVerifyBox.IsEnabled = !busy;
        JavaPathBox.IsEnabled = !busy;
        BrowseJavaButton.IsEnabled = !busy;
        GameDirBox.IsEnabled = !busy;
        BrowseGameDirButton.IsEnabled = !busy;
        NameBox.IsEnabled = !busy && GetSelectedLoader() is "vanilla" or "fabric";
        RenameButton.IsEnabled = NameBox.IsEnabled;
        RepairButton.IsEnabled = !busy;

        LocatePclButton.IsEnabled = !busy;
        OpenFolderButton.IsEnabled = !busy;
        ChangeDirButton.IsEnabled = !busy;
        SettingsButton.IsEnabled = !busy;

        UpdateStepChrome();
        UpdateFooter();
    }

    // ================= 设置 / 目录 / 关闭 =================

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        SettingsThreadsSlider.Value = Math.Clamp(_config.Threads, 1, 32);
        SettingsThreadsLabel.Text = ((int)SettingsThreadsSlider.Value).ToString();
        SettingsFullVerify.IsChecked = _config.FullVerify;
        SettingsJavaPathBox.Text = _config.JavaPath ?? "";
        ThemeBox.SelectedIndex = ThemeManager.Mode switch
        {
            AppTheme.Light => 1,
            AppTheme.Dark => 2,
            _ => 0,
        };
        ShowSettingsOverlay();
    }

    private void SettingsThreads_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SettingsThreadsLabel is not null) SettingsThreadsLabel.Text = ((int)e.NewValue).ToString();
    }

    private void BrowseSettingsJava_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 java.exe",
            Filter = "Java 可执行文件 (java.exe)|java.exe|可执行文件 (*.exe)|*.exe",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() == true) SettingsJavaPathBox.Text = dialog.FileName;
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _config.Threads = (int)SettingsThreadsSlider.Value;
        _config.FullVerify = SettingsFullVerify.IsChecked == true;
        var javaPath = SettingsJavaPathBox.Text?.Trim();
        _config.JavaPath = string.IsNullOrEmpty(javaPath) ? null : javaPath;

        var theme = ThemeBox.SelectedIndex switch
        {
            1 => AppTheme.Light,
            2 => AppTheme.Dark,
            _ => AppTheme.System,
        };
        _config.Theme = ThemeManager.ToConfig(theme);
        ThemeManager.SetMode(theme);
        _config.Save();

        ThreadsSlider.Value = Math.Clamp(_config.Threads, 1, 32);
        ThreadsLabel.Text = ((int)ThreadsSlider.Value).ToString();
        FullVerifyBox.IsChecked = _config.FullVerify;
        JavaPathBox.Text = _config.JavaPath ?? "";

        HideSettingsOverlay();
        AppendLog($"设置已保存：线程 {_config.Threads}，全量校验 {_config.FullVerify}");
        SetStatus("设置已保存");
    }

    private void CloseSettings_Click(object sender, RoutedEventArgs e)
        => HideSettingsOverlay();

    private void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var cache = Path.Combine(AppContext.BaseDirectory, "McInstall", "cache");
            var temp = Path.Combine(AppContext.BaseDirectory, "McInstall", "tmp");
            foreach (var dir in new[] { cache, temp })
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            AppendLog("缓存已清理");
            MessageBox.Show(this, "缓存已清理（下载中的 .part 文件不受影响）。", "清理缓存",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "清理失败：" + ex.Message, "清理缓存", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void LocatePcl_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 PCL2.exe",
            Filter = "PCL 启动器 (PCL*.exe)|PCL*.exe|可执行文件 (*.exe)|*.exe",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true) return;

        var pclDirectory = Path.GetDirectoryName(dialog.FileName);
        if (string.IsNullOrEmpty(pclDirectory)) return;

        _pclDir = pclDirectory;
        _gameDir = PclPaths.ResolveGameDir(_pclDir);
        _config.PclDirectory = _pclDir;
        _config.GameDirectory = _gameDir;
        _config.Save();

        UpdateHeader();
        GameDirBox.Text = _gameDir;
        UpdateGameDirHint();
        ApplyFilter(preserveSelection: true);

        AppendLog($"已定位 PCL：{_pclDir}");
        AppendLog($"游戏目录：{_gameDir}");
        SetStatus("已定位 PCL");
    }

    private void ChangeDir_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择 PCL 的 .minecraft 目录",
            InitialDirectory = Directory.Exists(_gameDir) ? _gameDir : _pclDir,
            Multiselect = false,
        };
        if (dialog.ShowDialog() != true) return;

        SetGameDirectory(dialog.FolderName);
    }

    private void SetGameDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;

        string full;
        try
        {
            full = Path.GetFullPath(directory);
        }
        catch
        {
            SetStatus("目录无效");
            return;
        }

        if (string.Equals(full, _gameDir, StringComparison.OrdinalIgnoreCase)) return;

        _gameDir = full;
        _config.GameDirectory = full;
        _config.Save();

        GameDirText.Text = full;
        GameDirBox.Text = full;
        UpdateGameDirHint();
        ApplyFilter(preserveSelection: true);

        AppendLog("游戏目录已切换为：" + full);
        SetStatus("已切换目录");
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) => OpenDirectory(_gameDir);

    private static void OpenDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
        }
        catch
        {
            // 打不开目录不影响主流程
        }
    }

    private void ShowWarning(string message, string title)
        => MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    private void ShowError(string message, string title)
        => MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _operationCts?.Cancel();
        _loaderCts?.Cancel();
    }

    // ---------- 供 reporter 调用（都在 UI 线程） ----------

    private sealed class UiReporter : IInstallReporter
    {
        private readonly MainWindow _owner;
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        public UiReporter(MainWindow owner) => _owner = owner;

        public void Log(string message) => Post(() => _owner.AppendLog($"[{_clock.Elapsed:mm\\:ss}] {message}"));
        public void Phase(string phase) => Post(() => _owner.SetPhase(phase));
        public void Progress(ProgressSnapshot snapshot) => Post(() => _owner.SetProgress(snapshot));

        private void Post(Action action)
        {
            if (_owner.Dispatcher.CheckAccess()) action();
            else _owner.Dispatcher.BeginInvoke(action);
        }
    }
}
