using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MortarHUD.Core.Ballistics;
using MortarHUD.Core.Configuration;
using MortarHUD.Localization;
using MortarHUD.Platform.Windows.Mouse;
using MortarHUD.Platform.Windows.WindowStyles;

namespace MortarHUD.App.Views;

/// <summary>
/// เมนูด่วน：按热键在游戏上方弹出的小面板，改武器 / HUD / 准星，不用切出游戏（ซู-ปืนครก V0.3）。
/// </summary>
/// <remarks>
/// <para>
/// 每个控件改动都直接写进传入的 <see cref="MortarHudSettings"/> 并回调 <c>onChanged</c>，
/// 由 App 立即应用到 HUD 和准星；关闭时再统一保存，避免拖动滑块时反复写盘。
/// </para>
/// <para>
/// 这里刻意只放「打游戏时会想调的」几项。OCR、ROI、主题这类低频设置仍在完整设置窗口里。
/// </para>
/// </remarks>
public sealed class QuickMenuWindow : Window
{
    private static readonly string[] Palette =
        ["#00FF66", "#FF3B3B", "#FFE14D", "#3BD8FF", "#FFFFFF", "#FF5CF0"];

    private readonly MortarHudSettings _settings;
    private readonly Action _onChanged;
    private readonly Action<string> _onWeaponSelected;
    private readonly Action _onClearTarget;
    private readonly Action _onOpenAllSettings;
    private readonly Func<string> _hotkeyText;

    private readonly Dictionary<string, ToggleButton> _weaponButtons = new(StringComparer.Ordinal);
    private readonly CheckBox _hudVisible = new();
    private readonly CheckBox _showMil = new();
    private readonly Slider _hudFontSize = MakeSlider(12, 48, 1);
    private readonly Slider _hudOpacity = MakeSlider(0.2, 1, 0.05);
    private readonly CheckBox _crosshairEnabled = new();
    private readonly ComboBox _crosshairStyle = new() { MinWidth = 170 };
    private readonly WrapPanel _colorSwatches = new();
    private readonly Slider _crosshairSize = MakeSlider(CrosshairSettings.MinSize, CrosshairSettings.MaxSize, 1);
    private readonly Slider _crosshairThickness = MakeSlider(CrosshairSettings.MinThickness, CrosshairSettings.MaxThickness, 1);
    private readonly Slider _crosshairGap = MakeSlider(CrosshairSettings.MinGap, CrosshairSettings.MaxGap, 1);
    private readonly Slider _crosshairOpacity = MakeSlider(CrosshairSettings.MinOpacity, CrosshairSettings.MaxOpacity, 0.05);
    private readonly CheckBox _crosshairOutline = new();
    private readonly Slider _crosshairOffsetX = MakeSlider(-CrosshairSettings.MaxOffset, CrosshairSettings.MaxOffset, 1);
    private readonly Slider _crosshairOffsetY = MakeSlider(-CrosshairSettings.MaxOffset, CrosshairSettings.MaxOffset, 1);
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };

    private IntPtr _handle;
    private IntPtr _returnFocusTo;
    private bool _loading;

    public QuickMenuWindow(
        MortarHudSettings settings,
        Action onChanged,
        Action<string> onWeaponSelected,
        Action onClearTarget,
        Action onOpenAllSettings,
        Func<string> hotkeyText)
    {
        _settings = settings;
        _onChanged = onChanged;
        _onWeaponSelected = onWeaponSelected;
        _onClearTarget = onClearTarget;
        _onOpenAllSettings = onOpenAllSettings;
        _hotkeyText = hotkeyText;

        Title = Loc.T("QuickMenuTitle");
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = new FontFamily("Segoe UI, Leelawadee UI, Microsoft YaHei UI");
        FontSize = 13;
        Foreground = Resource("TextBrush", Brushes.WhiteSmoke);

        Content = BuildLayout();

        // 失去焦点时不自动关闭：用户可能只是点了一下游戏又切回来。
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public bool IsOpen => IsVisible;

    // ================================================================ 打开 / 关闭

    /// <summary>在 <paramref name="gameWindow"/> 所在位置中央打开并抢到焦点；关闭后焦点还给它。</summary>
    public void Open(IntPtr gameWindow)
    {
        _returnFocusTo = CaptureContext.IsExternal(gameWindow) ? gameWindow : IntPtr.Zero;
        LoadFromSettings();

        Show();
        UpdateLayout();
        CenterOn(gameWindow);

        OverlayWindowController.SetTopmost(_handle, true);
        if (!ForegroundWindow.Activate(_handle))
        {
            Activate();
        }

        Focus();
    }

    /// <summary>关闭并把焦点还给游戏。</summary>
    public void CloseMenu()
    {
        if (!IsVisible)
        {
            return;
        }

        Hide();
        MenuClosed?.Invoke(this, EventArgs.Empty);

        if (_returnFocusTo != IntPtr.Zero)
        {
            ForegroundWindow.Activate(_returnFocusTo);
        }
    }

    /// <summary>菜单被关闭（隐藏）后触发，App 借此保存设置。</summary>
    public event EventHandler? MenuClosed;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Alt+F4 也只是隐藏，窗口实例复用。程序退出时由 App 调用 ForceClose。
        if (!_forceClose)
        {
            e.Cancel = true;
            CloseMenu();
        }

        base.OnClosing(e);
    }

    private bool _forceClose;

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseMenu();
            e.Handled = true;
        }
    }

    private void CenterOn(IntPtr gameWindow)
    {
        System.Drawing.Rectangle area;
        if (gameWindow != IntPtr.Zero && CaptureContext.TryGetClientBounds(gameWindow, out var client))
        {
            area = client;
        }
        else
        {
            area = OverlayWindowController.GetMonitorWorkArea(_handle);
        }

        if (area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var width = (int)Math.Round(ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Round(ActualHeight * dpi.DpiScaleY);

        OverlayWindowController.MoveToPhysical(
            _handle,
            area.X + Math.Max(0, (area.Width - width) / 2),
            area.Y + Math.Max(0, (area.Height - height) / 2));
    }

    // ================================================================ 布局

    private UIElement BuildLayout()
    {
        var root = new StackPanel { Width = 400 };

        root.Children.Add(new TextBlock
        {
            Text = Loc.T("QuickMenuTitle"),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = Resource("AccentBrush", Brushes.MediumAquamarine),
        });
        _hint.Foreground = Resource("SubtleTextBrush", Brushes.Gray);
        root.Children.Add(_hint);

        // ---- อาวุธ
        root.Children.Add(Header("WeaponSection"));
        var weapons = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var weapon in WeaponCatalog.All)
        {
            var button = new ToggleButton
            {
                Content = weapon.ShortName,
                MinWidth = 90,
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 8, 0),
            };
            var id = weapon.Id;
            button.Click += (_, _) =>
            {
                if (_loading) return;
                SelectWeaponButton(id);
                _onWeaponSelected(id);
            };
            _weaponButtons[id] = button;
            weapons.Children.Add(button);
        }

        root.Children.Add(weapons);

        // ---- HUD
        root.Children.Add(Header("HudSection"));
        root.Children.Add(Check(_hudVisible, "ShowHUD", v => _settings.Hud.Visible = v));
        root.Children.Add(Check(_showMil, "ShowMil", v => _settings.Hud.ShowMil = v));
        root.Children.Add(Row("HudFontSize", _hudFontSize, v => _settings.Hud.CurrentTheme.FontSize = v, "0"));
        root.Children.Add(Row("HudOpacity", _hudOpacity, v => _settings.Hud.CurrentTheme.Opacity = v, "0.00"));

        // ---- เป้ากลางจอ
        root.Children.Add(Header("CrosshairSection"));
        root.Children.Add(Check(_crosshairEnabled, "CrosshairEnable", v => _settings.Crosshair.Enabled = v));

        foreach (var style in Enum.GetValues<CrosshairStyle>())
        {
            _crosshairStyle.Items.Add(new ComboBoxItem { Content = Loc.T("CrosshairStyle" + style), Tag = style });
        }

        _crosshairStyle.SelectionChanged += (_, _) =>
        {
            if (_loading || _crosshairStyle.SelectedItem is not ComboBoxItem { Tag: CrosshairStyle style }) return;
            _settings.Crosshair.Style = style;
            _onChanged();
        };
        root.Children.Add(LabeledRow("CrosshairStyle", _crosshairStyle));

        foreach (var hex in Palette)
        {
            // 用 Border 而不是 Button：全局按钮样式会覆盖边框颜色，选中状态就看不出来了。
            var swatch = new Border
            {
                Width = 30,
                Height = 24,
                Margin = new Thickness(0, 0, 6, 0),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(HudRenderer.ParseColor(hex)),
                Tag = hex,
                ToolTip = hex,
                Cursor = Cursors.Hand,
            };
            swatch.MouseLeftButtonUp += (_, _) =>
            {
                _settings.Crosshair.Color = hex;
                HighlightSwatch();
                _onChanged();
            };
            _colorSwatches.Children.Add(swatch);
        }

        root.Children.Add(LabeledRow("CrosshairColor", _colorSwatches));
        root.Children.Add(Row("CrosshairSize", _crosshairSize, v => _settings.Crosshair.Size = v, "0"));
        root.Children.Add(Row("CrosshairThickness", _crosshairThickness, v => _settings.Crosshair.Thickness = v, "0"));
        root.Children.Add(Row("CrosshairGap", _crosshairGap, v => _settings.Crosshair.Gap = v, "0"));
        root.Children.Add(Row("CrosshairOpacity", _crosshairOpacity, v => _settings.Crosshair.Opacity = v, "0.00"));
        root.Children.Add(Check(_crosshairOutline, "CrosshairOutline", v => _settings.Crosshair.Outline = v));
        root.Children.Add(Row("CrosshairOffsetX", _crosshairOffsetX, v => _settings.Crosshair.OffsetX = v, "0"));
        root.Children.Add(Row("CrosshairOffsetY", _crosshairOffsetY, v => _settings.Crosshair.OffsetY = v, "0"));

        var resetCrosshair = new Button { Content = Loc.T("CrosshairReset"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        resetCrosshair.Click += (_, _) =>
        {
            var enabled = _settings.Crosshair.Enabled;
            _settings.Crosshair = CrosshairSettings.CreateDefault();
            _settings.Crosshair.Enabled = enabled;
            LoadFromSettings();
            _onChanged();
        };
        root.Children.Add(resetCrosshair);
        root.Children.Add(new TextBlock
        {
            Text = Loc.T("CrosshairHint"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Resource("SubtleTextBrush", Brushes.Gray),
            Margin = new Thickness(0, 6, 0, 0),
        });

        // ---- ปุ่มล่าง
        var actions = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        var clear = new Button { Content = Loc.T("ClearTarget"), Margin = new Thickness(0, 0, 8, 0) };
        clear.Click += (_, _) => _onClearTarget();
        var all = new Button { Content = Loc.T("AllSettings"), Margin = new Thickness(0, 0, 8, 0) };
        all.Click += (_, _) =>
        {
            CloseMenu();
            _onOpenAllSettings();
        };
        var close = new Button { Content = Loc.T("Close") };
        close.Click += (_, _) => CloseMenu();
        actions.Children.Add(clear);
        actions.Children.Add(all);
        actions.Children.Add(close);
        root.Children.Add(actions);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x14, 0x1A, 0x1F)),
            BorderBrush = Resource("BorderStrongBrush", Brushes.DimGray),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18, 14, 18, 16),
            Child = root,
        };
    }

    // ================================================================ 数据

    /// <summary>把当前设置填进控件（不触发回调）。</summary>
    public void LoadFromSettings()
    {
        _loading = true;
        try
        {
            _settings.Crosshair ??= new CrosshairSettings();
            _settings.Crosshair.Clamp();

            _hint.Text = Loc.F("QuickMenuHint", _hotkeyText());
            SelectWeaponButton(WeaponCatalog.Find(_settings.General.WeaponId).Id);

            _hudVisible.IsChecked = _settings.Hud.Visible;
            _showMil.IsChecked = _settings.Hud.ShowMil;
            _hudFontSize.Value = Math.Clamp(_settings.Hud.CurrentTheme.FontSize, _hudFontSize.Minimum, _hudFontSize.Maximum);
            _hudOpacity.Value = Math.Clamp(_settings.Hud.CurrentTheme.Opacity, _hudOpacity.Minimum, _hudOpacity.Maximum);

            var c = _settings.Crosshair;
            _crosshairEnabled.IsChecked = c.Enabled;
            _crosshairStyle.SelectedItem = _crosshairStyle.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, c.Style));
            _crosshairSize.Value = c.Size;
            _crosshairThickness.Value = c.Thickness;
            _crosshairGap.Value = c.Gap;
            _crosshairOpacity.Value = c.Opacity;
            _crosshairOutline.IsChecked = c.Outline;
            _crosshairOffsetX.Value = c.OffsetX;
            _crosshairOffsetY.Value = c.OffsetY;
            HighlightSwatch();
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>F7 等在菜单外切换了武器时同步按钮状态。</summary>
    public void SelectWeaponButton(string id)
    {
        foreach (var (weaponId, button) in _weaponButtons)
        {
            button.IsChecked = string.Equals(weaponId, id, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void HighlightSwatch()
    {
        foreach (var swatch in _colorSwatches.Children.OfType<Border>())
        {
            var selected = string.Equals((string)swatch.Tag, _settings.Crosshair.Color, StringComparison.OrdinalIgnoreCase);
            swatch.BorderThickness = new Thickness(selected ? 3 : 1);
            swatch.BorderBrush = selected ? Brushes.White : Brushes.Black;
        }
    }

    // ================================================================ 小工具

    private static Slider MakeSlider(double min, double max, double step) => new()
    {
        Minimum = min,
        Maximum = max,
        SmallChange = step,
        LargeChange = step * 5,
        TickFrequency = step,
        IsSnapToTickEnabled = true,
        MinWidth = 170,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private TextBlock Header(string key) => new()
    {
        Text = Loc.T(key),
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 14, 0, 6),
        Foreground = Resource("MutedTextBrush", Brushes.LightGray),
    };

    private UIElement Check(CheckBox box, string key, Action<bool> apply)
    {
        box.Content = Loc.T(key);
        box.Margin = new Thickness(0, 2, 0, 2);
        box.Click += (_, _) =>
        {
            if (_loading) return;
            apply(box.IsChecked == true);
            _onChanged();
        };
        return box;
    }

    private UIElement Row(string key, Slider slider, Action<double> apply, string format)
    {
        var value = new TextBlock { Width = 44, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        void ShowValue() => value.Text = slider.Value.ToString(format, CultureInfo.InvariantCulture);

        slider.ValueChanged += (_, _) =>
        {
            ShowValue();
            if (_loading) return;
            apply(slider.Value);
            _onChanged();
        };
        ShowValue();

        var panel = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        var label = new TextBlock { Text = Loc.T(key), Width = 140, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(label, Dock.Left);
        DockPanel.SetDock(value, Dock.Right);
        panel.Children.Add(label);
        panel.Children.Add(value);
        panel.Children.Add(slider);
        return panel;
    }

    private static UIElement LabeledRow(string key, FrameworkElement control)
    {
        var panel = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        var label = new TextBlock { Text = Loc.T(key), Width = 140, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(label, Dock.Left);
        panel.Children.Add(label);
        panel.Children.Add(control);
        return panel;
    }

    private static Brush Resource(string key, Brush fallback)
        => Application.Current?.TryFindResource(key) as Brush ?? fallback;
}
