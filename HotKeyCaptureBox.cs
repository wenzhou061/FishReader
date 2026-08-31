using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FishReader;

internal sealed class HotKeyCapturedEventArgs(string oldValue, string newValue) : EventArgs
{
    public string OldValue { get; } = oldValue;
    public string NewValue { get; } = newValue;
}

internal sealed class HotKeyCaptureBox : Border
{
    private readonly TextBlock _text = new();
    private string _bindingText = string.Empty;
    private string _beforeCapture = string.Empty;
    private bool _capturing;

    public HotKeyCaptureBox(string defaultBinding)
    {
        DefaultBinding = defaultBinding;
        Background = Brush("#1D1D1D");
        BorderBrush = Brush("#444444");
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(4);
        Padding = new Thickness(9, 5, 9, 5);
        Cursor = Cursors.Hand;
        Focusable = true;
        MinHeight = 29;
        _text.Foreground = Brush("#E6E6E6");
        _text.FontFamily = new FontFamily("Consolas");
        _text.VerticalAlignment = VerticalAlignment.Center;
        Child = _text;
        ToolTip = "点击后按下新的组合键；Esc 取消，Backspace 恢复默认。";

        PreviewMouseLeftButtonDown += (_, e) =>
        {
            BeginCapture();
            e.Handled = true;
        };
        PreviewKeyDown += OnPreviewKeyDown;
        LostKeyboardFocus += (_, _) => CancelCapture();
    }

    public event EventHandler<HotKeyCapturedEventArgs>? HotKeyCaptured;
    public event EventHandler? CaptureStarted;
    public event EventHandler? CaptureEnded;

    public string DefaultBinding { get; }

    public string BindingText
    {
        get => _bindingText;
        set
        {
            _bindingText = value;
            if (!_capturing)
                _text.Text = value;
        }
    }

    public void Revert(string value)
    {
        _capturing = false;
        BindingText = value;
        BorderBrush = Brush("#444444");
    }

    private void BeginCapture()
    {
        _beforeCapture = BindingText;
        _capturing = true;
        CaptureStarted?.Invoke(this, EventArgs.Empty);
        _text.Text = "请按组合键…";
        _text.Foreground = Brush("#8DC7F4");
        BorderBrush = Brush("#4B83B4");
        Keyboard.Focus(this);
    }

    private void CancelCapture()
    {
        if (!_capturing)
            return;
        _capturing = false;
        _text.Text = BindingText;
        _text.Foreground = Brush("#E6E6E6");
        BorderBrush = Brush("#444444");
        CaptureEnded?.Invoke(this, EventArgs.Empty);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturing)
            return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            CancelCapture();
            Keyboard.ClearFocus();
            return;
        }
        if (key == Key.Back)
        {
            CompleteCapture(DefaultBinding);
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;
        if (!TryKeyName(key, out var keyName))
        {
            _text.Text = "不支持，请重试";
            return;
        }

        var modifiers = Keyboard.Modifiers;
        var names = new List<string>(4);
        if ((modifiers & ModifierKeys.Control) != 0)
            names.Add("Ctrl");
        if ((modifiers & ModifierKeys.Alt) != 0)
            names.Add("Alt");
        if ((modifiers & ModifierKeys.Shift) != 0)
            names.Add("Shift");
        names.Add(keyName);
        var value = string.Join("+", names);
        if (!HotKeyBinding.TryParse(value, out var parsed, out _))
        {
            _text.Text = "需要组合键，请重试";
            return;
        }
        if (parsed.Display == "Alt+F4")
        {
            _text.Text = "Alt+F4 不可用";
            return;
        }
        CompleteCapture(parsed.Display);
    }

    private void CompleteCapture(string value)
    {
        var oldValue = _beforeCapture;
        _capturing = false;
        BindingText = value;
        _text.Foreground = Brush("#E6E6E6");
        BorderBrush = Brush("#444444");
        Keyboard.ClearFocus();
        CaptureEnded?.Invoke(this, EventArgs.Empty);
        if (!string.Equals(oldValue, value, StringComparison.OrdinalIgnoreCase))
            HotKeyCaptured?.Invoke(this, new HotKeyCapturedEventArgs(oldValue, value));
    }

    private static bool TryKeyName(Key key, out string value)
    {
        if (key is >= Key.A and <= Key.Z)
        {
            value = key.ToString();
            return true;
        }
        if (key is >= Key.D0 and <= Key.D9)
        {
            value = ((int)key - (int)Key.D0).ToString();
            return true;
        }
        if (key is >= Key.F1 and <= Key.F12)
        {
            value = key.ToString();
            return true;
        }
        value = key switch
        {
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Home => "Home",
            Key.End => "End",
            _ => string.Empty
        };
        return value.Length > 0;
    }

    private static SolidColorBrush Brush(string value)
        => new((Color)ColorConverter.ConvertFromString(value)!);
}
