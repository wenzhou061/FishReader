namespace FishReader;

internal readonly record struct HotKeyBinding(uint Modifiers, uint VirtualKey, string Display)
{
    public static bool TryParse(string? value, out HotKeyBinding binding, out string error)
    {
        binding = default;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "不能为空";
            return false;
        }

        var parts = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        uint modifiers = 0;
        string? keyName = null;
        foreach (var part in parts)
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                if ((modifiers & NativeMethods.ModControl) != 0)
                    return Fail("Ctrl 重复", out binding, out error);
                modifiers |= NativeMethods.ModControl;
            }
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                if ((modifiers & NativeMethods.ModAlt) != 0)
                    return Fail("Alt 重复", out binding, out error);
                modifiers |= NativeMethods.ModAlt;
            }
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                if ((modifiers & NativeMethods.ModShift) != 0)
                    return Fail("Shift 重复", out binding, out error);
                modifiers |= NativeMethods.ModShift;
            }
            else if (keyName is null)
            {
                keyName = part;
            }
            else
            {
                return Fail("只能包含一个按键", out binding, out error);
            }
        }

        if (keyName is null || !TryKey(keyName, out var virtualKey, out var normalizedKey, out var functionKey))
            return Fail("按键无效", out binding, out error);
        if (modifiers == 0 && !functionKey)
            return Fail("字母、数字和导航键至少需要 Ctrl、Alt 或 Shift 之一", out binding, out error);

        var names = new List<string>(4);
        if ((modifiers & NativeMethods.ModControl) != 0)
            names.Add("Ctrl");
        if ((modifiers & NativeMethods.ModAlt) != 0)
            names.Add("Alt");
        if ((modifiers & NativeMethods.ModShift) != 0)
            names.Add("Shift");
        names.Add(normalizedKey);
        binding = new HotKeyBinding(modifiers, virtualKey, string.Join("+", names));
        return true;
    }

    private static bool TryKey(string value, out uint virtualKey, out string normalized, out bool functionKey)
    {
        virtualKey = 0;
        normalized = string.Empty;
        functionKey = false;
        var key = value.Trim();
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]))
        {
            normalized = char.ToUpperInvariant(key[0]).ToString();
            virtualKey = normalized[0];
            return true;
        }

        var names = new Dictionary<string, (uint Key, string Name)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Up"] = (0x26, "Up"),
            ["↑"] = (0x26, "Up"),
            ["Down"] = (0x28, "Down"),
            ["↓"] = (0x28, "Down"),
            ["Left"] = (0x25, "Left"),
            ["←"] = (0x25, "Left"),
            ["Right"] = (0x27, "Right"),
            ["→"] = (0x27, "Right"),
            ["PageUp"] = (0x21, "PageUp"),
            ["PgUp"] = (0x21, "PageUp"),
            ["PageDown"] = (0x22, "PageDown"),
            ["PgDn"] = (0x22, "PageDown"),
            ["Home"] = (0x24, "Home"),
            ["End"] = (0x23, "End")
        };
        if (names.TryGetValue(key, out var match))
        {
            virtualKey = match.Key;
            normalized = match.Name;
            return true;
        }

        if (key.Length is 2 or 3 && key[0] is 'F' or 'f' &&
            int.TryParse(key[1..], out var number) && number is >= 1 and <= 12)
        {
            virtualKey = (uint)(0x70 + number - 1);
            normalized = $"F{number}";
            functionKey = true;
            return true;
        }
        return false;
    }

    private static bool Fail(string message, out HotKeyBinding binding, out string error)
    {
        binding = default;
        error = message;
        return false;
    }
}
