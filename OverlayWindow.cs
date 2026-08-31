using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace FishReader;

internal sealed class OverlayWindow : Window
{
    private readonly AppConfig _config;
    private readonly Grid _root = new();
    private readonly Grid _contentGrid = new();
    private readonly StackPanel _rows = new();
    private readonly Border _layoutBorder = new();
    private readonly Thumb _rightResize = new();
    private readonly Thumb _bottomResize = new();
    private readonly Thumb _cornerResize = new();
    private readonly Border _rightGuide = new();
    private readonly Border _bottomGuide = new();
    private readonly Border _cornerGuide = new();
    private readonly TextBlock _sizeText = new();
    private readonly Border _sizeBadge = new();
    private readonly List<TextBlock> _textRows = [];
    private readonly Action<ReaderHotKey> _hotKeyHandler;
    private readonly Action<string> _registrationError;
    private readonly Action _layoutChanged;
    private HwndSource? _source;
    private IntPtr _handle;
    private bool _layoutMode;
    private bool _allowClose;
    private bool _readingHotKeysRegistered;
    private double _rowDragPixels;
    private int _rowDragStartRows;

    public OverlayWindow(AppConfig config, Action<ReaderHotKey> hotKeyHandler, Action<string> registrationError,
        Action layoutChanged)
    {
        _config = config;
        _hotKeyHandler = hotKeyHandler;
        _registrationError = registrationError;
        _layoutChanged = layoutChanged;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        Focusable = false;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Left = config.WindowLeft;
        Top = config.WindowTop;

        _root.UseLayoutRounding = true;
        _root.SnapsToDevicePixels = true;
        _rows.UseLayoutRounding = true;
        _rows.SnapsToDevicePixels = true;

        _layoutBorder.BorderThickness = new Thickness(1);
        _layoutBorder.BorderBrush = Brushes.Transparent;
        _layoutBorder.Background = Brushes.Transparent;
        _layoutBorder.Padding = new Thickness(4);
        _contentGrid.Children.Add(_rows);
        ConfigureResizeHandles();
        _contentGrid.Children.Add(_rightGuide);
        _contentGrid.Children.Add(_bottomGuide);
        _contentGrid.Children.Add(_cornerGuide);
        _contentGrid.Children.Add(_rightResize);
        _contentGrid.Children.Add(_bottomResize);
        _contentGrid.Children.Add(_cornerResize);
        _contentGrid.Children.Add(_sizeBadge);
        _layoutBorder.Child = _contentGrid;
        _root.Children.Add(_layoutBorder);
        Content = _root;

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject origin && FindVisualParent<Thumb>(origin) is not null)
                return;
            if (_layoutMode && e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        };
        LocationChanged += (_, _) =>
        {
            if (_layoutMode)
            {
                _config.WindowLeft = Left;
                _config.WindowTop = Top;
            }
        };
        Closing += (_, e) =>
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                Hide();
            }
        };
        DpiChanged += (_, _) => ApplyGeometry();

        ApplyGeometry();
    }

    public IntPtr Handle => _handle;
    public bool IsLayoutMode => _layoutMode;

    public void InitializeNativeWindow()
    {
        _handle = new WindowInteropHelper(this).EnsureHandle();
        _source = HwndSource.FromHwnd(_handle);
        _source.AddHook(WindowProc);
        ApplyExtendedStyles();
        ApplyGeometry();
        Register(ReaderHotKey.Boss, _config.BossHotKey);
    }

    public void ApplyGeometry()
    {
        var scale = Math.Max(1, VisualTreeHelper.GetDpi(this).DpiScaleX);
        var lineHeight = SnapToPixel(_config.FontSize * 1.48, scale);
        _config.Width = SnapToPixel(_config.Width, scale);
        Width = _config.Width;
        Height = Math.Max(36, SnapToPixel(8 + (_config.VisibleRows * (lineHeight + _config.RowGap)), scale));
        var maximumLeft = SystemParameters.VirtualScreenLeft + Math.Max(0, SystemParameters.VirtualScreenWidth - Width);
        var maximumTop = SystemParameters.VirtualScreenTop + Math.Max(0, SystemParameters.VirtualScreenHeight - Height);
        Left = SnapToPixel(Math.Clamp(_config.WindowLeft, SystemParameters.VirtualScreenLeft, maximumLeft), scale);
        Top = SnapToPixel(Math.Clamp(_config.WindowTop, SystemParameters.VirtualScreenTop, maximumTop), scale);
        _config.WindowLeft = Left;
        _config.WindowTop = Top;
    }

    public void Render(IReadOnlyList<string> lines)
    {
        EnsureTextRows();
        var color = ParseBrush(_config.UseDarkPageTheme ? _config.DarkPageTextColor : _config.LightPageTextColor);
        var scale = Math.Max(1, VisualTreeHelper.GetDpi(this).DpiScaleX);
        var lineHeight = SnapToPixel(_config.FontSize * 1.48, scale);

        for (var index = 0; index < _textRows.Count; index++)
        {
            var text = _textRows[index];
            if (index >= lines.Count)
            {
                text.Visibility = Visibility.Collapsed;
                text.Text = string.Empty;
                continue;
            }

            var line = lines[index];
            text.Visibility = Visibility.Visible;
            text.FontFamily = new FontFamily(_config.FontFamily);
            text.FontSize = _config.FontSize;
            text.FontWeight = ParseFontWeight(_config.FontWeightName);
            text.Foreground = color;
            text.Opacity = _config.TextOpacity;
            text.LineHeight = lineHeight;
            text.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            text.Margin = new Thickness(2, _config.RowGap / 2, 2, _config.RowGap / 2);
            text.Inlines.Clear();
            TextOptions.SetTextFormattingMode(text, TextFormattingMode.Display);
            TextOptions.SetTextHintingMode(text, TextHintingMode.Fixed);
            if (_config.FadeTrailingPunctuation && line.Length > 1 && IsTrailingPunctuation(line[^1]))
            {
                var punctuationBrush = color.Clone();
                punctuationBrush.Opacity = 0.48;
                text.Inlines.Add(new Run(line[..^1]));
                text.Inlines.Add(new Run(line[^1].ToString()) { Foreground = punctuationBrush });
            }
            else
            {
                text.Text = line;
            }
        }
    }

    private void EnsureTextRows()
    {
        while (_textRows.Count < _config.VisibleRows)
        {
            var text = new TextBlock
            {
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.None,
                IsHitTestVisible = false,
                UseLayoutRounding = true,
                SnapsToDevicePixels = true
            };
            _textRows.Add(text);
            _rows.Children.Add(text);
        }

        while (_textRows.Count > _config.VisibleRows)
        {
            var last = _textRows[^1];
            _rows.Children.Remove(last);
            _textRows.RemoveAt(_textRows.Count - 1);
        }
    }

    private static double SnapToPixel(double value, double scale)
        => Math.Round(value * scale) / scale;

    public void SetLayoutMode(bool enabled)
    {
        _layoutMode = enabled;
        _layoutBorder.BorderBrush = enabled ? new SolidColorBrush(Color.FromArgb(175, 92, 92, 92)) : Brushes.Transparent;
        _layoutBorder.Background = Brushes.Transparent;
        _layoutBorder.Cursor = enabled ? Cursors.SizeAll : Cursors.Arrow;
        var handleVisibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        _rightResize.Visibility = handleVisibility;
        _bottomResize.Visibility = handleVisibility;
        _cornerResize.Visibility = handleVisibility;
        _rightGuide.Visibility = handleVisibility;
        _bottomGuide.Visibility = handleVisibility;
        _cornerGuide.Visibility = handleVisibility;
        _sizeBadge.Visibility = Visibility.Collapsed;
        Focusable = enabled;
        ShowActivated = enabled;
        ApplyExtendedStyles();
        if (enabled)
        {
            Show();
            Activate();
        }
    }

    public void ShowWithoutActivation()
    {
        if (_layoutMode)
            SetLayoutMode(false);
        Show();
        Topmost = true;
    }

    public void SetReadingHotKeysEnabled(bool enabled)
    {
        if (enabled == _readingHotKeysRegistered)
            return;

        if (enabled)
        {
            Register(ReaderHotKey.NextLine, _config.NextLineHotKey);
            Register(ReaderHotKey.PreviousLine, _config.PreviousLineHotKey);
            Register(ReaderHotKey.NextPage, _config.NextPageHotKey);
            Register(ReaderHotKey.PreviousPage, _config.PreviousPageHotKey);
            Register(ReaderHotKey.Layout, _config.LayoutHotKey);
            Register(ReaderHotKey.Theme, _config.ThemeHotKey);
        }
        else
        {
            for (var id = (int)ReaderHotKey.NextLine; id <= (int)ReaderHotKey.Theme; id++)
                NativeMethods.UnregisterHotKey(_handle, id);
        }
        _readingHotKeysRegistered = enabled;
    }

    public void ReloadHotKeys()
    {
        var restoreReadingKeys = _readingHotKeysRegistered;
        for (var id = (int)ReaderHotKey.Boss; id <= (int)ReaderHotKey.Theme; id++)
            NativeMethods.UnregisterHotKey(_handle, id);
        _readingHotKeysRegistered = false;
        Register(ReaderHotKey.Boss, _config.BossHotKey);
        if (restoreReadingKeys)
            SetReadingHotKeysEnabled(true);
    }

    public void SuspendHotKeys()
    {
        for (var id = (int)ReaderHotKey.Boss; id <= (int)ReaderHotKey.Theme; id++)
            NativeMethods.UnregisterHotKey(_handle, id);
        _readingHotKeysRegistered = false;
    }

    public void ShutdownWindow()
    {
        _allowClose = true;
        for (var id = 1; id <= 7; id++)
            NativeMethods.UnregisterHotKey(_handle, id);
        _source?.RemoveHook(WindowProc);
        Close();
    }

    private void ApplyExtendedStyles()
    {
        if (_handle == IntPtr.Zero)
            return;
        var style = NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GwlExStyle).ToInt64();
        style |= NativeMethods.WsExToolWindow;
        if (_layoutMode)
        {
            style &= ~NativeMethods.WsExTransparent;
            style &= ~NativeMethods.WsExNoActivate;
        }
        else
        {
            style |= NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate;
        }
        NativeMethods.SetWindowLongPtr(_handle, NativeMethods.GwlExStyle, new IntPtr(style));
    }

    private void ConfigureResizeHandles()
    {
        var transparent = Brushes.Transparent;
        var guideBrush = new SolidColorBrush(Color.FromArgb(180, 105, 105, 105));

        _rightGuide.Width = 1;
        _rightGuide.HorizontalAlignment = HorizontalAlignment.Right;
        _rightGuide.VerticalAlignment = VerticalAlignment.Stretch;
        _rightGuide.Margin = new Thickness(0, 4, 5, 4);
        _rightGuide.Background = guideBrush;
        _rightGuide.IsHitTestVisible = false;
        _rightGuide.Visibility = Visibility.Collapsed;

        _bottomGuide.Height = 1;
        _bottomGuide.HorizontalAlignment = HorizontalAlignment.Stretch;
        _bottomGuide.VerticalAlignment = VerticalAlignment.Bottom;
        _bottomGuide.Margin = new Thickness(4, 0, 4, 5);
        _bottomGuide.Background = guideBrush;
        _bottomGuide.IsHitTestVisible = false;
        _bottomGuide.Visibility = Visibility.Collapsed;

        _cornerGuide.Width = 9;
        _cornerGuide.Height = 9;
        _cornerGuide.HorizontalAlignment = HorizontalAlignment.Right;
        _cornerGuide.VerticalAlignment = VerticalAlignment.Bottom;
        _cornerGuide.Margin = new Thickness(0, 0, 3, 3);
        _cornerGuide.BorderBrush = guideBrush;
        _cornerGuide.BorderThickness = new Thickness(0, 0, 1, 1);
        _cornerGuide.IsHitTestVisible = false;
        _cornerGuide.Visibility = Visibility.Collapsed;

        _sizeText.Foreground = Brushes.White;
        _sizeText.FontFamily = new FontFamily("Microsoft YaHei UI");
        _sizeText.FontSize = 11;
        _sizeBadge.Background = new SolidColorBrush(Color.FromArgb(225, 38, 38, 38));
        _sizeBadge.BorderBrush = new SolidColorBrush(Color.FromArgb(210, 80, 80, 80));
        _sizeBadge.BorderThickness = new Thickness(1);
        _sizeBadge.CornerRadius = new CornerRadius(4);
        _sizeBadge.Padding = new Thickness(7, 3, 7, 3);
        _sizeBadge.Margin = new Thickness(0, 6, 8, 0);
        _sizeBadge.HorizontalAlignment = HorizontalAlignment.Right;
        _sizeBadge.VerticalAlignment = VerticalAlignment.Top;
        _sizeBadge.IsHitTestVisible = false;
        _sizeBadge.Visibility = Visibility.Collapsed;
        _sizeBadge.Child = _sizeText;
        Panel.SetZIndex(_sizeBadge, 5);

        _rightResize.Width = 12;
        _rightResize.HorizontalAlignment = HorizontalAlignment.Right;
        _rightResize.VerticalAlignment = VerticalAlignment.Stretch;
        _rightResize.Cursor = Cursors.SizeWE;
        _rightResize.Background = transparent;
        _rightResize.Visibility = Visibility.Collapsed;
        _rightResize.MouseEnter += (_, _) => SetGuideHighlight(true);
        _rightResize.MouseLeave += (_, _) => SetGuideHighlight(false);
        _rightResize.DragStarted += (_, _) => ShowSizeBadge();
        _rightResize.DragDelta += (_, e) =>
        {
            _config.Width = Math.Clamp(_config.Width + e.HorizontalChange, 120, 900);
            Width = _config.Width;
            UpdateSizeBadge();
            e.Handled = true;
        };
        _rightResize.DragCompleted += (_, _) => CompleteResize();

        _bottomResize.Height = 12;
        _bottomResize.HorizontalAlignment = HorizontalAlignment.Stretch;
        _bottomResize.VerticalAlignment = VerticalAlignment.Bottom;
        _bottomResize.Cursor = Cursors.SizeNS;
        _bottomResize.Background = transparent;
        _bottomResize.Visibility = Visibility.Collapsed;
        _bottomResize.MouseEnter += (_, _) => SetGuideHighlight(true);
        _bottomResize.MouseLeave += (_, _) => SetGuideHighlight(false);
        _bottomResize.DragStarted += (_, _) =>
        {
            BeginRowDrag();
            ShowSizeBadge();
        };
        _bottomResize.DragDelta += (_, e) =>
        {
            ResizeRows(e.VerticalChange);
            UpdateSizeBadge();
            e.Handled = true;
        };
        _bottomResize.DragCompleted += (_, _) => CompleteResize();

        _cornerResize.Width = 18;
        _cornerResize.Height = 18;
        _cornerResize.HorizontalAlignment = HorizontalAlignment.Right;
        _cornerResize.VerticalAlignment = VerticalAlignment.Bottom;
        _cornerResize.Cursor = Cursors.SizeNWSE;
        _cornerResize.Background = transparent;
        _cornerResize.Visibility = Visibility.Collapsed;
        Panel.SetZIndex(_cornerResize, 2);
        _cornerResize.MouseEnter += (_, _) => SetGuideHighlight(true);
        _cornerResize.MouseLeave += (_, _) => SetGuideHighlight(false);
        _cornerResize.DragStarted += (_, _) =>
        {
            BeginRowDrag();
            ShowSizeBadge();
        };
        _cornerResize.DragDelta += (_, e) =>
        {
            _config.Width = Math.Clamp(_config.Width + e.HorizontalChange, 120, 900);
            ResizeRows(e.VerticalChange);
            Width = _config.Width;
            UpdateSizeBadge();
            e.Handled = true;
        };
        _cornerResize.DragCompleted += (_, _) => CompleteResize();
    }

    private void ShowSizeBadge()
    {
        SetGuideHighlight(true);
        UpdateSizeBadge();
        _sizeBadge.Visibility = Visibility.Visible;
    }

    private void UpdateSizeBadge()
        => _sizeText.Text = $"{_config.Width:0} px  ·  {_config.VisibleRows} 行";

    private void CompleteResize()
    {
        _sizeBadge.Visibility = Visibility.Collapsed;
        SetGuideHighlight(false);
        _layoutChanged();
    }

    private void SetGuideHighlight(bool highlighted)
    {
        var color = highlighted
            ? new SolidColorBrush(Color.FromArgb(235, 75, 131, 180))
            : new SolidColorBrush(Color.FromArgb(180, 105, 105, 105));
        _rightGuide.Background = color;
        _bottomGuide.Background = color;
        _cornerGuide.BorderBrush = color;
    }

    private void BeginRowDrag()
    {
        _rowDragPixels = 0;
        _rowDragStartRows = _config.VisibleRows;
    }

    private void ResizeRows(double verticalChange)
    {
        _rowDragPixels += verticalChange;
        var rowHeight = Math.Max(1, (_config.FontSize * 1.48) + _config.RowGap);
        _config.VisibleRows = Math.Clamp(_rowDragStartRows + (int)Math.Round(_rowDragPixels / rowHeight), 1, 8);
        Height = Math.Max(36, 8 + (_config.VisibleRows * rowHeight));
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        var current = child;
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void Register(ReaderHotKey hotKey, string value)
    {
        if (!HotKeyBinding.TryParse(value, out var binding, out var error))
        {
            _registrationError($"快捷键 {value} 无效：{error}。");
            return;
        }
        if (!NativeMethods.RegisterHotKey(_handle, (int)hotKey,
                binding.Modifiers | NativeMethods.ModNoRepeat, binding.VirtualKey))
            _registrationError($"快捷键 {binding.Display} 注册失败，可能已被其他程序占用。");
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotKey)
        {
            handled = true;
            _hotKeyHandler((ReaderHotKey)wParam.ToInt32());
        }
        return IntPtr.Zero;
    }

    private static System.Windows.Media.Brush ParseBrush(string value)
    {
        try
        {
            return (System.Windows.Media.Brush)new BrushConverter().ConvertFromString(value)!;
        }
        catch
        {
            return Brushes.LightGray;
        }
    }

    private static FontWeight ParseFontWeight(string value) => value switch
    {
        "SemiBold" => FontWeights.SemiBold,
        "Medium" => FontWeights.Medium,
        _ => FontWeights.Normal
    };

    private static bool IsTrailingPunctuation(char value)
        => value is '。' or '，' or '！' or '？' or '；' or '：' or ',' or '.' or '!' or '?' or ';' or ':';
}
