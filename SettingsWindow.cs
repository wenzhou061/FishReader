using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TextBox = System.Windows.Controls.TextBox;

namespace FishReader;

internal sealed class SettingsWindow : Window
{
    private static readonly SolidColorBrush WindowBrush = Brush("#1F1F1F");
    private static readonly SolidColorBrush CardBrush = Brush("#272727");
    private static readonly SolidColorBrush MutedBrush = Brush("#9B9B9B");

    private readonly ReaderApplication _app;
    private readonly TextBlock _fileState = new();
    private readonly TextBlock _progressState = new();
    private readonly TextBlock _themeState = new();
    private readonly TextBlock _preview = new();
    private readonly TextBlock _status = new();
    private readonly TextBlock _previewMeta = new();
    private readonly StackPanel _stylePreview = new();
    private readonly Border _previewFrame = new();
    private readonly TextBox _width = new();
    private readonly TextBox _rows = new();
    private readonly TextBox _fontSize = new();
    private readonly TextBox _rowGap = new();
    private readonly TextBox _opacity = new();
    private readonly TextBox _fontFamily = new();
    private readonly ComboBox _fontWeight = new() { ItemsSource = new[] { "常规", "中等", "半粗" } };
    private readonly CheckBox _fadePunctuation = new() { Content = "弱化行尾标点" };
    private readonly TextBox _darkColor = new();
    private readonly TextBox _lightColor = new();
    private readonly TextBox _percent = new();
    private readonly TextBox _search = new();
    private readonly HotKeyCaptureBox _bossHotKey = new("Alt+B");
    private readonly HotKeyCaptureBox _nextLineHotKey = new("Alt+Down");
    private readonly HotKeyCaptureBox _previousLineHotKey = new("Alt+Up");
    private readonly HotKeyCaptureBox _nextPageHotKey = new("Alt+PageDown");
    private readonly HotKeyCaptureBox _previousPageHotKey = new("Alt+PageUp");
    private readonly HotKeyCaptureBox _layoutHotKey = new("Alt+L");
    private readonly HotKeyCaptureBox _themeHotKey = new("Alt+T");
    private int _candidateOffset;

    public SettingsWindow(ReaderApplication app)
    {
        _app = app;
        Title = "FishReader";
        Width = 640;
        Height = 735;
        MinWidth = 600;
        MinHeight = 710;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = WindowBrush;
        Foreground = Brush("#E7E7E7");
        FontFamily = new FontFamily("Microsoft YaHei UI");
        FontSize = 13;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Icon = AppIcon.CreateWindowIcon();
        SourceInitialized += (_, _) => NativeMethods.ApplyDarkWindowChrome(new WindowInteropHelper(this).Handle);
        ApplyControlStyles();

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = CreateHeader();
        Grid.SetRow(header, 0);
        layout.Children.Add(header);

        var tabs = new TabControl
        {
            Margin = new Thickness(12, 10, 12, 8),
            Background = WindowBrush
        };
        tabs.Items.Add(new TabItem { Header = "阅读与定位", Content = BuildReadingTab() });
        tabs.Items.Add(new TabItem { Header = "外观", Content = BuildAppearanceTab() });
        tabs.Items.Add(new TabItem { Header = "快捷键", Content = BuildShortcutsTab() });
        Grid.SetRow(tabs, 1);
        layout.Children.Add(tabs);

        var footer = new Border
        {
            Background = Brush("#252525"),
            BorderBrush = Brush("#353535"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(16, 7, 16, 7)
        };
        _status.Text = "设置会保存在程序目录的 data 文件夹中。";
        _status.Foreground = MutedBrush;
        footer.Child = _status;
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);

        Content = layout;
        WireLivePreview();
        WireHotKeyCapture();
        RefreshFields();
        RefreshDocumentState();
        RefreshThemeState();
    }

    public void RefreshDocumentState()
    {
        var document = _app.Document;
        if (document is null)
        {
            _fileState.Text = "尚未打开 TXT 文件";
            _progressState.Text = "打开文件后可保存进度并进行定位";
            _candidateOffset = 0;
            _percent.Text = "0";
            _preview.Text = "打开 TXT 后可按百分比或文字定位。";
            return;
        }

        _fileState.Text = $"{Path.GetFileName(document.FilePath)}\n{document.FilePath}\n编码：{document.EncodingName}";
        _candidateOffset = _app.Config.CurrentOffset;
        var percent = _app.PercentFromOffset(_candidateOffset);
        _progressState.Text = $"第 {_app.CurrentLineNumber:N0} / {_app.TotalLineCount:N0} 行  ·  {percent:0.00}%";
        _percent.Text = percent.ToString("0.00", CultureInfo.CurrentCulture);
        _preview.Text = _app.PreviewAt(_candidateOffset);
    }

    public void RefreshThemeState()
    {
        _themeState.Text = _app.Config.UseDarkPageTheme ? "当前配色：深色页面" : "当前配色：浅色页面";
        UpdateStylePreview();
    }

    private UIElement CreateHeader()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "FishReader",
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White
        });
        panel.Children.Add(new TextBlock
        {
            Text = "轻量 TXT 悬浮阅读器",
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = MutedBrush
        });
        return new Border
        {
            Background = Brush("#252525"),
            BorderBrush = Brush("#353535"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 10, 16, 10),
            Child = panel
        };
    }

    private UIElement BuildReadingTab()
    {
        var root = TabStack();

        var fileCard = Card("当前文件", out var fileBody);
        _fileState.TextWrapping = TextWrapping.Wrap;
        _fileState.LineHeight = 21;
        fileBody.Children.Add(_fileState);
        _progressState.Foreground = MutedBrush;
        _progressState.Margin = new Thickness(0, 8, 0, 12);
        fileBody.Children.Add(_progressState);
        var open = new Button { Content = "打开 TXT", Width = 110, HorizontalAlignment = HorizontalAlignment.Left };
        open.Click += (_, _) => _app.OpenTextFile(this);
        fileBody.Children.Add(open);
        root.Children.Add(fileCard);

        var locateCard = Card("定位阅读位置", out var locateBody);
        var locateGrid = CreateFormGrid(170);
        AddFormRow(locateGrid, 0, "百分比（0～100）", _percent);
        AddFormRow(locateGrid, 1, "搜索章节名或正文", _search);
        locateBody.Children.Add(locateGrid);

        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 10) };
        buttons.Children.Add(ActionButton("预览百分比", (_, _) => PreviewPercent()));
        buttons.Children.Add(ActionButton("查找上一处", (_, _) => FindPrevious()));
        buttons.Children.Add(ActionButton("查找下一处", (_, _) => FindNext()));
        buttons.Children.Add(ActionButton("确认跳转", (_, _) => ApplyLocation(), primary: true));
        buttons.Children.Add(ActionButton("上一章", (_, _) => JumpChapter(next: false)));
        buttons.Children.Add(ActionButton("下一章", (_, _) => JumpChapter(next: true)));
        buttons.Children.Add(ActionButton("返回跳转前", (_, _) => ReturnToPreviousLocation()));
        locateBody.Children.Add(buttons);

        _preview.TextWrapping = TextWrapping.Wrap;
        _preview.TextTrimming = TextTrimming.CharacterEllipsis;
        _preview.LineHeight = 23;
        _preview.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        _preview.Padding = new Thickness(12);
        _preview.Background = Brush("#1D1D1D");
        _preview.Foreground = Brush("#D4D4D4");
        _preview.Height = 116;
        _preview.MaxHeight = 116;
        _preview.ClipToBounds = true;
        locateBody.Children.Add(_preview);
        root.Children.Add(locateCard);

        return new Border { Background = WindowBrush, Child = root };
    }

    private UIElement BuildAppearanceTab()
    {
        var root = TabStack();

        var previewCard = Card("效果预览", out var previewBody);
        _previewMeta.Foreground = MutedBrush;
        _previewMeta.Margin = new Thickness(0, 0, 0, 8);
        previewBody.Children.Add(_previewMeta);
        _previewFrame.Background = Brush("#202020");
        _previewFrame.BorderBrush = Brush("#3A3A3A");
        _previewFrame.BorderThickness = new Thickness(1);
        _previewFrame.CornerRadius = new CornerRadius(7);
        _previewFrame.Padding = new Thickness(12, 7, 12, 7);
        _previewFrame.HorizontalAlignment = HorizontalAlignment.Left;
        _previewFrame.Child = _stylePreview;
        previewBody.Children.Add(_previewFrame);
        root.Children.Add(previewCard);

        var displayCard = Card("显示参数", out var displayBody);
        var displayColumns = new Grid();
        displayColumns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        displayColumns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        displayColumns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var leftGrid = CreateFormGrid(128);
        AddFormRow(leftGrid, 0, "宽度", _width);
        AddFormRow(leftGrid, 1, "显示行数", _rows);
        AddFormRow(leftGrid, 2, "字号", _fontSize);
        AddFormRow(leftGrid, 3, "行间距", _rowGap);
        AddFormRow(leftGrid, 4, "透明度", _opacity);
        var rightGrid = CreateFormGrid(118);
        AddFormRow(rightGrid, 0, "深色文字", _darkColor);
        AddFormRow(rightGrid, 1, "浅色文字", _lightColor);
        AddFormRow(rightGrid, 2, "字体", _fontFamily);
        AddFormRow(rightGrid, 3, "字重", _fontWeight);
        Grid.SetColumn(leftGrid, 0);
        Grid.SetColumn(rightGrid, 2);
        displayColumns.Children.Add(leftGrid);
        displayColumns.Children.Add(rightGrid);
        displayBody.Children.Add(displayColumns);
        var punctuationRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 7, 0, 1) };
        _fadePunctuation.Foreground = MutedBrush;
        _fadePunctuation.VerticalAlignment = VerticalAlignment.Center;
        punctuationRow.Children.Add(_fadePunctuation);
        var punctuationHelpText = new TextBlock
        {
            Text = "?",
            Foreground = Brush("#AFAFAF"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var punctuationHelp = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Background = Brush("#303030"),
            BorderBrush = Brush("#484848"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(7, 0, 0, 0),
            Child = punctuationHelpText
        };
        var helpToolTip = new ToolTip
        {
            Background = Brush("#2A2A2A"),
            Foreground = Brush("#E4E4E4"),
            BorderBrush = Brush("#4A4A4A"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8),
            Placement = PlacementMode.Right,
            PlacementTarget = punctuationHelp,
            HasDropShadow = true,
            Content = new TextBlock
            {
                Text = "降低每个显示行末尾标点的透明度，\n仅改变显示效果，不会修改 TXT 原文。",
                Foreground = Brush("#E4E4E4"),
                LineHeight = 20
            }
        };
        punctuationHelp.ToolTip = helpToolTip;
        ToolTipService.SetInitialShowDelay(punctuationHelp, 180);
        ToolTipService.SetShowDuration(punctuationHelp, 30000);
        punctuationRow.Children.Add(punctuationHelp);
        displayBody.Children.Add(punctuationRow);

        var displayButtons = new WrapPanel { Margin = new Thickness(0, 10, 0, 2) };
        displayButtons.Children.Add(ActionButton("应用参数", (_, _) => ApplyDisplaySettings(), primary: true));
        displayButtons.Children.Add(ActionButton("Codex 侧栏预设", (_, _) => ApplyCodexPreset()));
        displayButtons.Children.Add(ActionButton("切换配色", (_, _) => _app.ToggleTheme()));
        _themeState.VerticalAlignment = VerticalAlignment.Center;
        _themeState.Foreground = MutedBrush;
        _themeState.Margin = new Thickness(4, 0, 0, 7);
        displayButtons.Children.Add(_themeState);
        displayBody.Children.Add(displayButtons);
        root.Children.Add(displayCard);

        return new Border { Background = WindowBrush, Child = root };
    }

    private UIElement BuildShortcutsTab()
    {
        var root = TabStack();
        var shortcutCard = Card("全局快捷键", out var body);
        var shortcutGrid = CreateFormGrid(160);
        AddFormRow(shortcutGrid, 0, "显示 / 隐藏（老板键）", _bossHotKey);
        AddFormRow(shortcutGrid, 1, "下一行", _nextLineHotKey);
        AddFormRow(shortcutGrid, 2, "上一行", _previousLineHotKey);
        AddFormRow(shortcutGrid, 3, "下一屏", _nextPageHotKey);
        AddFormRow(shortcutGrid, 4, "上一屏", _previousPageHotKey);
        AddFormRow(shortcutGrid, 5, "布局模式", _layoutHotKey);
        AddFormRow(shortcutGrid, 6, "切换配色", _themeHotKey);
        body.Children.Add(shortcutGrid);
        body.Children.Add(new TextBlock
        {
            Text = "点击任一快捷键框后直接按下新组合键；Esc 取消，Backspace 恢复该项默认值。修改成功后自动保存。",
            Foreground = MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 8)
        });
        body.Children.Add(ActionButton("恢复全部默认快捷键", (_, _) => ResetHotKeys()));
        body.Children.Add(new TextBlock
        {
            Text = "阅读器隐藏时只占用老板键；切换窗口后会自动隐藏，需要手动按老板键恢复。",
            Foreground = Brush("#C8C8C8"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0)
        });
        root.Children.Add(shortcutCard);
        return new Border { Background = WindowBrush, Child = root };
    }

    private void RefreshFields()
    {
        var config = _app.Config;
        _width.Text = config.Width.ToString("0", CultureInfo.CurrentCulture);
        _rows.Text = config.VisibleRows.ToString(CultureInfo.CurrentCulture);
        _fontSize.Text = config.FontSize.ToString("0.#", CultureInfo.CurrentCulture);
        _rowGap.Text = config.RowGap.ToString("0.#", CultureInfo.CurrentCulture);
        _opacity.Text = config.TextOpacity.ToString("0.##", CultureInfo.CurrentCulture);
        _fontFamily.Text = config.FontFamily;
        _fontWeight.SelectedIndex = config.FontWeightName switch { "Medium" => 1, "SemiBold" => 2, _ => 0 };
        _fadePunctuation.IsChecked = config.FadeTrailingPunctuation;
        _darkColor.Text = config.DarkPageTextColor;
        _lightColor.Text = config.LightPageTextColor;
        _bossHotKey.BindingText = config.BossHotKey;
        _nextLineHotKey.BindingText = config.NextLineHotKey;
        _previousLineHotKey.BindingText = config.PreviousLineHotKey;
        _nextPageHotKey.BindingText = config.NextPageHotKey;
        _previousPageHotKey.BindingText = config.PreviousPageHotKey;
        _layoutHotKey.BindingText = config.LayoutHotKey;
        _themeHotKey.BindingText = config.ThemeHotKey;
        UpdateStylePreview();
    }

    private void ApplyDisplaySettings()
    {
        if (!TryDouble(_width, out var width) || !int.TryParse(_rows.Text, out var rows) ||
            !TryDouble(_fontSize, out var fontSize) || !TryDouble(_rowGap, out var gap) ||
            !TryDouble(_opacity, out var opacity) || !IsColor(_darkColor.Text) || !IsColor(_lightColor.Text) ||
            string.IsNullOrWhiteSpace(_fontFamily.Text))
        {
            MessageBox.Show(this, "请检查显示参数。颜色请使用 #RRGGBB 格式。", "参数无效",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var fontWeight = FontWeightNameFromSelection();
        var config = _app.Config;
        config.Width = width;
        config.VisibleRows = rows;
        config.FontSize = fontSize;
        config.RowGap = gap;
        config.TextOpacity = opacity;
        config.FontFamily = _fontFamily.Text.Trim();
        config.FontWeightName = fontWeight;
        config.FadeTrailingPunctuation = _fadePunctuation.IsChecked == true;
        config.DarkPageTextColor = _darkColor.Text.Trim();
        config.LightPageTextColor = _lightColor.Text.Trim();
        _app.ApplyVisualSettings();
        RefreshFields();
        SetStatus("显示参数已应用并保存。", success: true);
    }

    private void WireHotKeyCapture()
    {
        foreach (var box in HotKeyInputs().Select(value => value.Box))
        {
            box.HotKeyCaptured += OnHotKeyCaptured;
            box.CaptureStarted += (_, _) => _app.SetHotKeyRecording(true);
            box.CaptureEnded += (_, _) => _app.SetHotKeyRecording(false);
        }
    }

    private void OnHotKeyCaptured(object? sender, HotKeyCapturedEventArgs e)
    {
        if (TryApplyHotKeySettings())
            return;
        if (sender is HotKeyCaptureBox box)
            box.Revert(e.OldValue);
    }

    private bool TryApplyHotKeySettings()
    {
        var inputs = HotKeyInputs();
        var parsed = new HotKeyBinding[inputs.Length];
        var unique = new Dictionary<(uint Modifiers, uint Key), string>();
        for (var i = 0; i < inputs.Length; i++)
        {
            if (!HotKeyBinding.TryParse(inputs[i].Box.BindingText, out parsed[i], out var error))
            {
                MessageBox.Show(this, $"“{inputs[i].Name}”快捷键无效：{error}。", "快捷键无效",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            var signature = (parsed[i].Modifiers, parsed[i].VirtualKey);
            if (unique.TryGetValue(signature, out var existing))
            {
                MessageBox.Show(this, $"“{inputs[i].Name}”与“{existing}”都使用 {parsed[i].Display}。请避免重复。",
                    "快捷键重复", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            unique.Add(signature, inputs[i].Name);
        }

        var config = _app.Config;
        config.BossHotKey = parsed[0].Display;
        config.NextLineHotKey = parsed[1].Display;
        config.PreviousLineHotKey = parsed[2].Display;
        config.NextPageHotKey = parsed[3].Display;
        config.PreviousPageHotKey = parsed[4].Display;
        config.LayoutHotKey = parsed[5].Display;
        config.ThemeHotKey = parsed[6].Display;
        _app.ApplyHotKeySettings();
        RefreshFields();
        SetStatus("快捷键已自动保存；如被其他程序占用，托盘会显示提示。", success: true);
        return true;
    }

    private void ResetHotKeys()
    {
        foreach (var input in HotKeyInputs())
            input.Box.BindingText = input.Box.DefaultBinding;
        TryApplyHotKeySettings();
    }

    private (string Name, HotKeyCaptureBox Box)[] HotKeyInputs() =>
    [
        ("显示 / 隐藏", _bossHotKey), ("下一行", _nextLineHotKey), ("上一行", _previousLineHotKey),
        ("下一屏", _nextPageHotKey), ("上一屏", _previousPageHotKey), ("布局模式", _layoutHotKey),
        ("切换配色", _themeHotKey)
    ];

    private void WireLivePreview()
    {
        foreach (var box in new[] { _width, _rows, _fontSize, _rowGap, _opacity, _fontFamily, _darkColor, _lightColor })
            box.TextChanged += (_, _) => UpdateStylePreview();
        _fontWeight.SelectionChanged += (_, _) => UpdateStylePreview();
        _fadePunctuation.Checked += (_, _) => UpdateStylePreview();
        _fadePunctuation.Unchecked += (_, _) => UpdateStylePreview();
    }

    private void ApplyCodexPreset()
    {
        var config = _app.Config;
        config.FontFamily = "Microsoft YaHei UI";
        config.FontWeightName = "Normal";
        config.FontSize = 16;
        config.RowGap = 10;
        config.TextOpacity = 0.94;
        config.DarkPageTextColor = "#DEDEDE";
        config.LightPageTextColor = "#333333";
        config.FadeTrailingPunctuation = false;
        _app.ApplyVisualSettings();
        RefreshFields();
        SetStatus("已恢复 Codex 侧栏预设；位置、宽度和行数未改变。", success: true);
    }

    private void PreviewPercent()
    {
        if (!TryDouble(_percent, out var percent))
        {
            MessageBox.Show(this, "请输入 0～100 之间的百分比。", "百分比无效",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _candidateOffset = _app.OffsetFromPercent(Math.Clamp(percent, 0, 100));
        UpdateLocationPreview();
        SetStatus("已更新预览，尚未改变阅读进度。");
    }

    private void FindNext()
    {
        if (string.IsNullOrWhiteSpace(_search.Text))
        {
            SetStatus("请先输入要查找的文字。");
            return;
        }
        var found = _app.FindText(_search.Text, Math.Min(_candidateOffset + 1, _app.Document?.Length ?? 0));
        if (found < 0 && _candidateOffset > 0)
            found = _app.FindText(_search.Text, 0);
        ShowSearchResult(found, "下一处");
    }

    private void FindPrevious()
    {
        if (string.IsNullOrWhiteSpace(_search.Text))
        {
            SetStatus("请先输入要查找的文字。");
            return;
        }
        var found = _app.FindPreviousText(_search.Text, _candidateOffset);
        if (found < 0 && _app.Document is { Length: > 0 } document)
            found = _app.FindPreviousText(_search.Text, document.Length);
        ShowSearchResult(found, "上一处");
    }

    private void ShowSearchResult(int found, string direction)
    {
        if (found < 0)
        {
            SetStatus("没有找到该文字。");
            return;
        }
        _candidateOffset = found;
        UpdateLocationPreview();
        SetStatus($"已找到{direction}，确认跳转前不会改变进度。", success: true);
    }

    private void ApplyLocation()
    {
        if (_app.Document is null)
        {
            SetStatus("请先打开 TXT 文件。");
            return;
        }
        _app.JumpToOffset(_candidateOffset);
        RefreshDocumentState();
        SetStatus("阅读位置已更新并保存。", success: true);
    }

    private void JumpChapter(bool next)
    {
        if (_app.Document is null)
        {
            SetStatus("请先打开 TXT 文件。");
            return;
        }

        var moved = next ? _app.JumpToNextChapter() : _app.JumpToPreviousChapter();
        if (!moved)
        {
            SetStatus(next ? "后面没有识别到章节标题。" : "前面没有识别到章节标题。");
            return;
        }

        RefreshDocumentState();
        SetStatus(next ? "已跳到下一章。" : "已跳到上一章。", success: true);
    }

    private void ReturnToPreviousLocation()
    {
        if (!_app.ReturnToPreviousLocation())
        {
            SetStatus("当前没有可返回的跳转位置。");
            return;
        }

        RefreshDocumentState();
        SetStatus("已返回跳转前的位置。", success: true);
    }

    private void UpdateLocationPreview()
    {
        _percent.Text = _app.PercentFromOffset(_candidateOffset).ToString("0.00", CultureInfo.CurrentCulture);
        _preview.Text = _app.PreviewAt(_candidateOffset);
    }

    private void UpdateStylePreview()
    {
        _stylePreview.Children.Clear();
        var config = _app.Config;
        var width = TryDouble(_width, out var pendingWidth) ? Math.Clamp(pendingWidth, 120, 900) : config.Width;
        var rows = int.TryParse(_rows.Text, out var pendingRows) ? Math.Clamp(pendingRows, 1, 8) : config.VisibleRows;
        var fontSize = TryDouble(_fontSize, out var pendingFontSize) ? Math.Clamp(pendingFontSize, 12, 28) : config.FontSize;
        var rowGap = TryDouble(_rowGap, out var pendingGap) ? Math.Clamp(pendingGap, 2, 32) : config.RowGap;
        var opacity = TryDouble(_opacity, out var pendingOpacity) ? Math.Clamp(pendingOpacity, 0.25, 1) : config.TextOpacity;
        var family = string.IsNullOrWhiteSpace(_fontFamily.Text) ? config.FontFamily : _fontFamily.Text.Trim();
        var weightName = FontWeightNameFromSelection();
        var pendingColor = config.UseDarkPageTheme ? _darkColor.Text : _lightColor.Text;
        var fallbackColor = config.UseDarkPageTheme ? config.DarkPageTextColor : config.LightPageTextColor;
        var color = TryBrush(pendingColor, out var brush) || TryBrush(fallbackColor, out brush)
            ? brush
            : Brush("#DEDEDE");
        _previewFrame.Width = Math.Clamp(width, 180, 510);
        _previewFrame.Background = Brush(config.UseDarkPageTheme ? "#202020" : "#F2F2F2");
        _previewMeta.Text = $"实时模拟  {width:0} px × {rows} 行  ·  当前{(config.UseDarkPageTheme ? "深色" : "浅色")}页面配色";
        var values = new[]
        {
            "评估项目进度与风险。", "整理本周工作记录，", "检查接口联调结果！", "确认下一阶段安排。",
            "同步当前任务状态。", "核对剩余问题？", "准备最终交付说明。", "记录阅读进度。"
        };
        foreach (var value in values.Take(Math.Min(rows, 3)))
        {
            var text = new TextBlock
            {
                FontFamily = new FontFamily(family),
                FontSize = fontSize,
                FontWeight = FontWeightFromName(weightName),
                Foreground = color,
                Opacity = opacity,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, rowGap / 2, 0, rowGap / 2)
            };
            TextOptions.SetTextFormattingMode(text, TextFormattingMode.Display);
            if (_fadePunctuation.IsChecked == true && value.Length > 1 && IsTrailingPunctuation(value[^1]))
            {
                var punctuation = color.Clone();
                punctuation.Opacity = 0.48;
                text.Inlines.Add(new System.Windows.Documents.Run(value[..^1]));
                text.Inlines.Add(new System.Windows.Documents.Run(value[^1].ToString()) { Foreground = punctuation });
            }
            else
            {
                text.Text = value;
            }
            _stylePreview.Children.Add(text);
        }
    }

    private void ApplyControlStyles()
    {
        var button = new Style(typeof(Button));
        button.Setters.Add(new Setter(BackgroundProperty, Brush("#343434")));
        button.Setters.Add(new Setter(ForegroundProperty, Brush("#EEEEEE")));
        button.Setters.Add(new Setter(BorderBrushProperty, Brush("#494949")));
        button.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(1)));
        button.Setters.Add(new Setter(PaddingProperty, new Thickness(12, 7, 12, 7)));
        button.Setters.Add(new Setter(CursorProperty, Cursors.Hand));
        button.Triggers.Add(new Trigger { Property = IsMouseOverProperty, Value = true, Setters = { new Setter(BackgroundProperty, Brush("#414141")) } });
        Resources[typeof(Button)] = button;

        var textBox = new Style(typeof(TextBox));
        textBox.Setters.Add(new Setter(BackgroundProperty, Brush("#1D1D1D")));
        textBox.Setters.Add(new Setter(ForegroundProperty, Brush("#E6E6E6")));
        textBox.Setters.Add(new Setter(BorderBrushProperty, Brush("#444444")));
        textBox.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(1)));
        textBox.Setters.Add(new Setter(PaddingProperty, new Thickness(8, 6, 8, 6)));
        textBox.Setters.Add(new Setter(TextBox.SelectionBrushProperty, Brush("#356A9A")));
        Resources[typeof(TextBox)] = textBox;

        var checkBox = new Style(typeof(CheckBox));
        checkBox.Setters.Add(new Setter(CheckBox.ForegroundProperty, Brush("#C8C8C8")));
        checkBox.Setters.Add(new Setter(CheckBox.CursorProperty, Cursors.Hand));
        checkBox.Setters.Add(new Setter(CheckBox.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        var checkTemplate = new ControlTemplate(typeof(CheckBox));
        var checkRoot = new FrameworkElementFactory(typeof(Grid));
        var checkBoxBorder = new FrameworkElementFactory(typeof(Border));
        checkBoxBorder.Name = "Box";
        checkBoxBorder.SetValue(Border.WidthProperty, 16d);
        checkBoxBorder.SetValue(Border.HeightProperty, 16d);
        checkBoxBorder.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        checkBoxBorder.SetValue(Border.VerticalAlignmentProperty, VerticalAlignment.Center);
        checkBoxBorder.SetValue(Border.BackgroundProperty, Brush("#1D1D1D"));
        checkBoxBorder.SetValue(Border.BorderBrushProperty, Brush("#5A5A5A"));
        checkBoxBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        checkBoxBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        checkRoot.AppendChild(checkBoxBorder);
        var checkMark = new FrameworkElementFactory(typeof(TextBlock));
        checkMark.Name = "Mark";
        checkMark.SetValue(TextBlock.TextProperty, "✓");
        checkMark.SetValue(TextBlock.ForegroundProperty, Brushes.White);
        checkMark.SetValue(TextBlock.FontSizeProperty, 11d);
        checkMark.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        checkMark.SetValue(TextBlock.WidthProperty, 16d);
        checkMark.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
        checkMark.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        checkMark.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        checkMark.SetValue(TextBlock.VisibilityProperty, Visibility.Collapsed);
        checkRoot.AppendChild(checkMark);
        var checkContent = new FrameworkElementFactory(typeof(ContentPresenter));
        checkContent.SetValue(ContentPresenter.MarginProperty, new Thickness(24, 0, 0, 0));
        checkContent.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        checkContent.SetValue(ContentPresenter.ContentSourceProperty, "Content");
        checkRoot.AppendChild(checkContent);
        checkTemplate.VisualTree = checkRoot;
        checkTemplate.Triggers.Add(new Trigger
        {
            Property = CheckBox.IsCheckedProperty,
            Value = true,
            Setters =
            {
                new Setter(Border.BackgroundProperty, Brush("#356A9A"), "Box"),
                new Setter(Border.BorderBrushProperty, Brush("#4B83B4"), "Box"),
                new Setter(TextBlock.VisibilityProperty, Visibility.Visible, "Mark")
            }
        });
        checkTemplate.Triggers.Add(new Trigger
        {
            Property = CheckBox.IsMouseOverProperty,
            Value = true,
            Setters = { new Setter(Border.BorderBrushProperty, Brush("#6F92AE"), "Box") }
        });
        checkBox.Setters.Add(new Setter(CheckBox.TemplateProperty, checkTemplate));
        Resources[typeof(CheckBox)] = checkBox;

        var comboItem = new Style(typeof(ComboBoxItem));
        comboItem.Setters.Add(new Setter(ComboBoxItem.ForegroundProperty, Brush("#E6E6E6")));
        comboItem.Setters.Add(new Setter(ComboBoxItem.BackgroundProperty, Brush("#252525")));
        comboItem.Setters.Add(new Setter(ComboBoxItem.PaddingProperty, new Thickness(9, 6, 9, 6)));
        comboItem.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        comboItem.Triggers.Add(new Trigger
        {
            Property = ComboBoxItem.IsHighlightedProperty,
            Value = true,
            Setters = { new Setter(ComboBoxItem.BackgroundProperty, Brush("#3A5870")) }
        });
        comboItem.Triggers.Add(new Trigger
        {
            Property = ComboBoxItem.IsSelectedProperty,
            Value = true,
            Setters = { new Setter(ComboBoxItem.BackgroundProperty, Brush("#356A9A")) }
        });
        Resources[typeof(ComboBoxItem)] = comboItem;
        Resources[typeof(ComboBox)] = CreateDarkComboBoxStyle();
        Resources[typeof(TabControl)] = CreateDarkTabControlStyle();

        var tab = new Style(typeof(TabItem));
        tab.Setters.Add(new Setter(ForegroundProperty, MutedBrush));
        tab.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
        tab.Setters.Add(new Setter(PaddingProperty, new Thickness(15, 9, 15, 9)));
        var tabTemplate = new ControlTemplate(typeof(TabItem));
        var tabBorder = new FrameworkElementFactory(typeof(Border));
        tabBorder.SetBinding(Border.BackgroundProperty, new Binding("Background")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        tabBorder.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        tabBorder.SetBinding(Border.PaddingProperty, new Binding("Padding")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        tabBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1, 1, 1, 0));
        tabBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(6, 6, 0, 0));
        var tabContent = new FrameworkElementFactory(typeof(ContentPresenter));
        tabContent.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        tabContent.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        tabContent.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        tabBorder.AppendChild(tabContent);
        tabTemplate.VisualTree = tabBorder;
        tab.Setters.Add(new Setter(TabItem.TemplateProperty, tabTemplate));
        tab.Setters.Add(new Setter(BorderBrushProperty, Brush("#444444")));
        tab.Triggers.Add(new Trigger
        {
            Property = TabItem.IsSelectedProperty,
            Value = true,
            Setters =
            {
                new Setter(ForegroundProperty, Brushes.White),
                new Setter(BackgroundProperty, Brush("#303030")),
                new Setter(Panel.ZIndexProperty, 2)
            }
        });
        Resources[typeof(TabItem)] = tab;
    }

    private static Style CreateDarkTabControlStyle()
    {
        var style = new Style(typeof(TabControl));
        style.Setters.Add(new Setter(TabControl.BackgroundProperty, WindowBrush));
        style.Setters.Add(new Setter(TabControl.BorderBrushProperty, Brush("#444444")));
        var template = new ControlTemplate(typeof(TabControl));
        var root = new FrameworkElementFactory(typeof(DockPanel));
        var headers = new FrameworkElementFactory(typeof(TabPanel));
        headers.SetValue(DockPanel.DockProperty, Dock.Top);
        headers.SetValue(Panel.IsItemsHostProperty, true);
        headers.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
        headers.SetValue(KeyboardNavigation.TabIndexProperty, 1);
        root.AppendChild(headers);
        var contentBorder = new FrameworkElementFactory(typeof(Border));
        contentBorder.SetValue(Border.BackgroundProperty, WindowBrush);
        contentBorder.SetValue(Border.BorderBrushProperty, Brush("#444444"));
        contentBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        contentBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(0, 6, 6, 6));
        contentBorder.SetValue(Border.MarginProperty, new Thickness(0, -1, 0, 0));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.ContentSourceProperty, "SelectedContent");
        content.SetValue(ContentPresenter.MarginProperty, new Thickness(0));
        contentBorder.AppendChild(content);
        root.AppendChild(contentBorder);
        template.VisualTree = root;
        style.Setters.Add(new Setter(TabControl.TemplateProperty, template));
        return style;
    }

    private static Style CreateDarkComboBoxStyle()
    {
        var style = new Style(typeof(ComboBox));
        style.Setters.Add(new Setter(ComboBox.ForegroundProperty, Brush("#E6E6E6")));
        style.Setters.Add(new Setter(ComboBox.BackgroundProperty, Brush("#1D1D1D")));
        style.Setters.Add(new Setter(ComboBox.BorderBrushProperty, Brush("#444444")));
        style.Setters.Add(new Setter(ComboBox.MinHeightProperty, 31d));

        var template = new ControlTemplate(typeof(ComboBox));
        var root = new FrameworkElementFactory(typeof(Grid));

        var toggle = new FrameworkElementFactory(typeof(ToggleButton));
        toggle.SetValue(ToggleButton.FocusableProperty, false);
        toggle.SetValue(ToggleButton.ClickModeProperty, ClickMode.Press);
        toggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding("IsDropDownOpen")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
            Mode = BindingMode.TwoWay
        });
        var toggleTemplate = new ControlTemplate(typeof(ToggleButton));
        var toggleBorder = new FrameworkElementFactory(typeof(Border));
        toggleBorder.SetBinding(Border.BackgroundProperty, new Binding("Background")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        toggleBorder.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        toggleBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        toggleBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        toggleTemplate.VisualTree = toggleBorder;
        toggle.SetValue(ToggleButton.TemplateProperty, toggleTemplate);
        toggle.SetBinding(ToggleButton.BackgroundProperty, new Binding("Background")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        toggle.SetBinding(ToggleButton.BorderBrushProperty, new Binding("BorderBrush")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        root.AppendChild(toggle);

        var selected = new FrameworkElementFactory(typeof(ContentPresenter));
        selected.SetValue(ContentPresenter.MarginProperty, new Thickness(9, 4, 30, 4));
        selected.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        selected.SetValue(ContentPresenter.IsHitTestVisibleProperty, false);
        selected.SetBinding(ContentPresenter.ContentProperty, new Binding("SelectionBoxItem")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        root.AppendChild(selected);

        var arrow = new FrameworkElementFactory(typeof(TextBlock));
        arrow.SetValue(TextBlock.TextProperty, "⌄");
        arrow.SetValue(TextBlock.FontSizeProperty, 16d);
        arrow.SetValue(TextBlock.ForegroundProperty, Brush("#BDBDBD"));
        arrow.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Right);
        arrow.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        arrow.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 9, 3));
        arrow.SetValue(TextBlock.IsHitTestVisibleProperty, false);
        root.AppendChild(arrow);

        var popup = new FrameworkElementFactory(typeof(Popup));
        popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
        popup.SetValue(Popup.AllowsTransparencyProperty, true);
        popup.SetValue(Popup.StaysOpenProperty, false);
        popup.SetValue(Popup.PopupAnimationProperty, PopupAnimation.Fade);
        popup.SetBinding(Popup.IsOpenProperty, new Binding("IsDropDownOpen")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
            Mode = BindingMode.TwoWay
        });
        popup.SetBinding(Popup.PlacementTargetProperty, new Binding
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        var popupBorder = new FrameworkElementFactory(typeof(Border));
        popupBorder.SetValue(Border.BackgroundProperty, Brush("#252525"));
        popupBorder.SetValue(Border.BorderBrushProperty, Brush("#4A4A4A"));
        popupBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        popupBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        popupBorder.SetValue(Border.PaddingProperty, new Thickness(2));
        popupBorder.SetBinding(Border.MinWidthProperty, new Binding("ActualWidth")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
        scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        var presenter = new FrameworkElementFactory(typeof(ItemsPresenter));
        scroll.AppendChild(presenter);
        popupBorder.AppendChild(scroll);
        popup.AppendChild(popupBorder);
        root.AppendChild(popup);

        template.VisualTree = root;
        style.Setters.Add(new Setter(ComboBox.TemplateProperty, template));
        return style;
    }

    private static Border Card(string title, out StackPanel body)
    {
        body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 8)
        });
        return new Border
        {
            Background = CardBrush,
            BorderBrush = Brush("#363636"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(13),
            Margin = new Thickness(0, 0, 0, 9),
            Child = body
        };
    }

    private static StackPanel TabStack() => new() { Margin = new Thickness(0, 6, 0, 0) };

    private static Grid CreateFormGrid(double labelWidth)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return grid;
    }

    private static void AddFormRow(Grid grid, int row, string label, FrameworkElement input)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var text = new TextBlock
        {
            Text = label,
            Foreground = Brush("#C8C8C8"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 3, 12, 3)
        };
        input.Margin = new Thickness(0, 2, 0, 2);
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        Grid.SetRow(input, row);
        Grid.SetColumn(input, 1);
        grid.Children.Add(text);
        grid.Children.Add(input);
    }

    private static Button ActionButton(string text, RoutedEventHandler handler, bool primary = false)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(0, 0, 8, 7),
            Background = primary ? Brush("#356A9A") : Brush("#343434"),
            BorderBrush = primary ? Brush("#4B83B4") : Brush("#494949")
        };
        button.Click += handler;
        return button;
    }

    private void SetStatus(string value, bool success = false)
    {
        _status.Text = value;
        _status.Foreground = success ? Brush("#77C58A") : MutedBrush;
    }

    private static bool TryDouble(TextBox box, out double value)
        => double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static bool IsColor(string value) => TryBrush(value, out _);

    private static bool TryBrush(string value, out SolidColorBrush brush)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(value)!;
            brush = new SolidColorBrush(color);
            return true;
        }
        catch
        {
            brush = Brush("#DEDEDE");
            return false;
        }
    }

    private static FontWeight FontWeightFromName(string value) => value switch
    {
        "SemiBold" => FontWeights.SemiBold,
        "Medium" => FontWeights.Medium,
        _ => FontWeights.Normal
    };

    private string FontWeightNameFromSelection() => _fontWeight.SelectedIndex switch
    {
        1 => "Medium",
        2 => "SemiBold",
        _ => "Normal"
    };

    private static bool IsTrailingPunctuation(char value)
        => value is '。' or '，' or '！' or '？' or '；' or '：' or ',' or '.' or '!' or '?' or ';' or ':';

    private static SolidColorBrush Brush(string value)
        => new((Color)ColorConverter.ConvertFromString(value)!);
}
