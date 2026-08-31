using Microsoft.Win32;
using System.Drawing;
using System.Windows;
using System.Windows.Threading;
using System.Threading;
using System.Threading.Tasks;
using Forms = System.Windows.Forms;

namespace FishReader;

internal sealed class ReaderApplication : System.Windows.Application
{
    private readonly ConfigStore _store = new();
    private readonly EventWaitHandle _activationEvent;
    private readonly DispatcherTimer _foregroundTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private AppConfig _config = null!;
    private OverlayWindow _overlay = null!;
    private Forms.NotifyIcon _tray = null!;
    private Icon? _trayIcon;
    private TextDocument? _document;
    private SettingsWindow? _settings;
    private IntPtr _boundWindow;
    private int _firstVisibleLine;
    private int? _returnOffset;
    private bool _exiting;
    private readonly List<string> _pendingNotices = [];
    private RegisteredWaitHandle? _activationWait;

    public ReaderApplication(EventWaitHandle activationEvent)
    {
        _activationEvent = activationEvent;
    }

    public AppConfig Config => _config;
    public TextDocument? Document => _document;
    public int CurrentLineNumber => _document is { Lines.Count: > 0 } ? _firstVisibleLine + 1 : 0;
    public int TotalLineCount => _document?.Lines.Count ?? 0;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _config = _store.Load();
        _overlay = new OverlayWindow(_config, HandleHotKey, ShowNotice, ApplyVisualSettings);
        _overlay.InitializeNativeWindow();
        _overlay.Hide();
        MainWindow = _overlay;

        TryRestoreDocument();
        CreateTrayIcon();
        _activationWait = ThreadPool.RegisterWaitForSingleObject(_activationEvent, (_, _) =>
            Dispatcher.BeginInvoke(ShowSettings), null, Timeout.Infinite, false);
        _foregroundTimer.Tick += CheckBoundWindow;
        _foregroundTimer.Start();
        _saveTimer.Tick += (_, _) => SaveState();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!_exiting)
            SaveState();
        _activationWait?.Unregister(null);
        _saveTimer.Stop();
        _tray?.Dispose();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }

    public void OpenTextFile(Window? owner = null)
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开 TXT 文件",
            Filter = "TXT 文本文件 (*.txt)|*.txt",
            DefaultExt = ".txt",
            CheckFileExists = true,
            Multiselect = false,
            RestoreDirectory = true,
            InitialDirectory = GetInitialTextDirectory()
        };
        if (!string.IsNullOrWhiteSpace(_config.FilePath) && File.Exists(_config.FilePath))
            dialog.FileName = Path.GetFileName(_config.FilePath);

        if (dialog.ShowDialog(owner) != true)
            return;

        try
        {
            var document = TextDocument.Load(dialog.FileName);
            _document = document;
            _config.FilePath = document.FilePath;
            _config.EncodingName = document.EncodingName;
            _config.CurrentOffset = 0;
            _returnOffset = null;
            ReflowDocument(0);
            SaveState();
            ShowNotice($"已打开 {Path.GetFileName(dialog.FileName)}（{document.EncodingName}）");
            _settings?.RefreshDocumentState();
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, $"无法导入该 TXT：\n\n{ex.Message}", "打开失败",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void ShowSettings()
    {
        HideOverlay();
        if (_settings is { IsVisible: true })
        {
            _settings.Activate();
            return;
        }

        _settings = new SettingsWindow(this);
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show();
        _settings.Activate();
    }

    public void ApplyVisualSettings()
    {
        var offset = CurrentOffset;
        ConfigStore.Normalize(_config);
        _overlay.ApplyGeometry();
        ReflowDocument(offset);
        SaveState();
    }

    public void ApplyHotKeySettings()
    {
        ConfigStore.Normalize(_config);
        _overlay.ReloadHotKeys();
        SaveState();
    }

    public void SetHotKeyRecording(bool recording)
    {
        if (recording)
            _overlay.SuspendHotKeys();
        else
            _overlay.ReloadHotKeys();
    }

    public void ToggleTheme()
    {
        _config.UseDarkPageTheme = !_config.UseDarkPageTheme;
        RenderOverlay();
        SaveState();
        _settings?.RefreshThemeState();
    }

    public int FindText(string value, int startOffset)
        => _document?.Find(value, startOffset) ?? -1;

    public int FindPreviousText(string value, int beforeOffset)
        => _document?.FindPrevious(value, beforeOffset) ?? -1;

    public int OffsetFromPercent(double percent)
        => _document?.OffsetFromPercent(percent) ?? 0;

    public double PercentFromOffset(int offset)
        => _document?.PercentFromOffset(offset) ?? 0;

    public string PreviewAt(int offset)
        => _document?.PreviewAt(offset) ?? "尚未打开 TXT 文件。";

    public void JumpToOffset(int offset)
        => JumpToOffset(offset, rememberCurrent: true);

    public bool ReturnToPreviousLocation()
    {
        if (_returnOffset is not { } target || _document is null)
            return false;
        _returnOffset = CurrentOffset;
        JumpToOffset(target, rememberCurrent: false);
        return true;
    }

    public bool JumpToNextChapter()
    {
        var currentLineEnd = _document is { Lines.Count: > 0 }
            ? _document.Lines[Math.Clamp(_firstVisibleLine, 0, _document.Lines.Count - 1)].End
            : CurrentOffset;
        var target = _document?.FindNextChapter(currentLineEnd) ?? -1;
        if (target < 0)
            return false;
        JumpToOffset(target);
        return true;
    }

    public bool JumpToPreviousChapter()
    {
        var target = _document?.FindPreviousChapter(CurrentOffset) ?? -1;
        if (target < 0)
            return false;
        JumpToOffset(target);
        return true;
    }

    private void JumpToOffset(int offset, bool rememberCurrent)
    {
        if (_document is null || _document.Lines.Count == 0)
            return;
        if (rememberCurrent)
            _returnOffset = CurrentOffset;
        _firstVisibleLine = _document.FindLineIndexAtOffset(offset);
        PersistProgress();
        RenderOverlay();
        SaveState();
        _settings?.RefreshDocumentState();
    }

    public void ExitApplication()
    {
        _exiting = true;
        SaveState();
        _foregroundTimer.Stop();
        _tray.Visible = false;
        _overlay.ShutdownWindow();
        Shutdown();
    }

    private int CurrentOffset
        => _document is { Lines.Count: > 0 }
            ? _document.Lines[Math.Clamp(_firstVisibleLine, 0, _document.Lines.Count - 1)].Start
            : _config.CurrentOffset;

    private void TryRestoreDocument()
    {
        if (string.IsNullOrWhiteSpace(_config.FilePath) || !File.Exists(_config.FilePath))
            return;
        try
        {
            _document = TextDocument.Load(_config.FilePath);
            _config.EncodingName = _document.EncodingName;
            ReflowDocument(_config.CurrentOffset);
        }
        catch (Exception ex)
        {
            WriteLog($"恢复文件失败：{ex}");
            _document = null;
        }
    }

    private void ReflowDocument(int preserveOffset)
    {
        if (_document is null)
        {
            _firstVisibleLine = 0;
            RenderOverlay();
            return;
        }

        _document.Reflow(_config.Width - 16, _config.FontSize, _config.FontFamily, _config.FontWeightName);
        _firstVisibleLine = _document.FindLineIndexAtOffset(preserveOffset);
        PersistProgress();
        RenderOverlay();
    }

    private void HandleHotKey(ReaderHotKey hotKey)
    {
        switch (hotKey)
        {
            case ReaderHotKey.Boss:
                ToggleOverlay();
                break;
            case ReaderHotKey.NextLine:
                MoveLines(1);
                break;
            case ReaderHotKey.PreviousLine:
                MoveLines(-1);
                break;
            case ReaderHotKey.NextPage:
                MoveLines(Math.Max(1, _config.VisibleRows - 1));
                break;
            case ReaderHotKey.PreviousPage:
                MoveLines(-Math.Max(1, _config.VisibleRows - 1));
                break;
            case ReaderHotKey.Layout:
                ToggleLayoutMode();
                break;
            case ReaderHotKey.Theme:
                ToggleTheme();
                break;
        }
    }

    private void ToggleOverlay()
    {
        if (_overlay.IsVisible)
        {
            HideOverlay();
            return;
        }

        if (_document is null || _document.Lines.Count == 0)
        {
            ShowNotice("尚未打开 TXT。请从托盘菜单选择“打开 TXT”。");
            return;
        }

        _boundWindow = NativeMethods.GetForegroundWindow();
        if (_boundWindow == IntPtr.Zero || _boundWindow == _overlay.Handle)
            return;

        RenderOverlay();
        _overlay.SetReadingHotKeysEnabled(true);
        _overlay.ShowWithoutActivation();
    }

    private void HideOverlay()
    {
        if (_overlay.IsLayoutMode)
            _overlay.SetLayoutMode(false);
        _overlay.Hide();
        _overlay.SetReadingHotKeysEnabled(false);
        _boundWindow = IntPtr.Zero;
        SaveState();
    }

    private void ToggleLayoutMode()
    {
        if (!_overlay.IsVisible)
            return;
        var enabled = !_overlay.IsLayoutMode;
        _overlay.SetLayoutMode(enabled);
        if (!enabled)
        {
            _config.WindowLeft = _overlay.Left;
            _config.WindowTop = _overlay.Top;
            SaveState();
            if (_boundWindow != IntPtr.Zero)
                NativeMethods.SetForegroundWindow(_boundWindow);
        }
    }

    private void MoveLines(int delta)
    {
        if (!_overlay.IsVisible || _document is null || _document.Lines.Count == 0)
            return;
        _firstVisibleLine = Math.Clamp(_firstVisibleLine + delta, 0, _document.Lines.Count - 1);
        PersistProgress();
        RenderOverlay();
        ScheduleSave();
    }

    private void RenderOverlay()
    {
        if (_document is null || _document.Lines.Count == 0)
        {
            _overlay.Render([]);
            return;
        }

        var lines = _document.Lines
            .Skip(_firstVisibleLine)
            .Take(_config.VisibleRows)
            .Select(line => line.Text)
            .ToArray();
        _overlay.Render(lines);
    }

    private void PersistProgress()
    {
        _config.CurrentOffset = CurrentOffset;
    }

    private void CheckBoundWindow(object? sender, EventArgs e)
    {
        if (!_overlay.IsVisible || _boundWindow == IntPtr.Zero)
            return;

        var foreground = NativeMethods.GetForegroundWindow();
        if (_overlay.IsLayoutMode && foreground == _overlay.Handle)
            return;

        if (NativeMethods.IsIconic(_boundWindow) || foreground != _boundWindow)
            HideOverlay();
    }

    private void CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开 TXT", null, (_, _) => Dispatcher.Invoke(() => OpenTextFile()));
        menu.Items.Add("显示到当前窗口（Alt+B）", null, async (_, _) =>
        {
            await Task.Delay(220);
            await Dispatcher.InvokeAsync(() =>
            {
                if (!_overlay.IsVisible)
                    ToggleOverlay();
            });
        });
        menu.Items.Add("设置", null, (_, _) => Dispatcher.Invoke(ShowSettings));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        _trayIcon = AppIcon.CreateTrayIcon();
        _tray = new Forms.NotifyIcon
        {
            Icon = _trayIcon,
            Text = "FishReader",
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowSettings);
        foreach (var notice in _pendingNotices)
            ShowNotice(notice);
        _pendingNotices.Clear();
    }

    private string GetInitialTextDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_config.FilePath))
        {
            var directory = Path.GetDirectoryName(_config.FilePath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                return directory;
        }
        return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    }

    private void SaveState()
    {
        _saveTimer.Stop();
        try
        {
            PersistProgress();
            _store.Save(_config);
        }
        catch (Exception ex)
        {
            WriteLog($"保存配置失败：{ex}");
        }
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void ShowNotice(string message)
    {
        if (_tray is null)
        {
            _pendingNotices.Add(message);
            WriteLog(message);
            return;
        }
        _tray.BalloonTipTitle = "FishReader";
        _tray.BalloonTipText = message;
        _tray.ShowBalloonTip(2500);
    }

    private void WriteLog(string value)
    {
        CrashLogger.Write(value);
    }
}
