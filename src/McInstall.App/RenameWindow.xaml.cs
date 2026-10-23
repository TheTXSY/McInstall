using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using McInstall.Core;

namespace McInstall.App;

public partial class RenameWindow : Window
{
    private readonly List<string> _existing;

    public string VersionName { get; private set; } = "";

    public RenameWindow(string suggested, IEnumerable<string> existing)
    {
        InitializeComponent();
        _existing = new List<string>(existing);
        NameBox.Text = suggested;
        UpdateHint();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try { DragMove(); } catch { /* 忽略 */ }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        DialogResult = false;
        e.Handled = true;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        WindowTheme.Apply(this, ThemeManager.IsDark);
        Card.Opacity = 0;
        var scale = new ScaleTransform(0.97, 0.97);
        Card.RenderTransform = scale;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(200);
        Card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.97, 1, duration) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.97, 1, duration) { EasingFunction = ease });

        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void NameBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateHint();

    private void UpdateHint()
    {
        if (Hint is null) return;
        var typed = NameBox.Text?.Trim() ?? "";
        if (typed.Length == 0)
        {
            Hint.Text = "名字不能为空";
            return;
        }

        Hint.Text = _existing.Exists(x => x.Equals(typed, System.StringComparison.OrdinalIgnoreCase))
            ? $"「{typed}」已存在，将改用：{VersionNames.MakeUnique(typed, _existing)}"
            : $"将使用：{typed}";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var typed = NameBox.Text?.Trim() ?? "";
        if (typed.Length == 0) return;

        // 确认时再兜一次底，保证最终名字不重复
        VersionName = VersionNames.MakeUnique(typed, _existing);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
