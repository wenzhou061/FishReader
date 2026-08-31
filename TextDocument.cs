using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FishReader;

internal readonly record struct TextLine(int Start, int End, string Text);

internal sealed class TextDocument
{
    private static readonly char[] StrongStops = ['。', '！', '？', '!', '?', '；', ';'];
    private static readonly char[] WeakStops = ['，', ',', '、', '：', ':'];

    private readonly string _text;
    private List<TextLine> _lines = [];

    private TextDocument(string path, string encodingName, string text)
    {
        FilePath = path;
        EncodingName = encodingName;
        _text = text;
    }

    public string FilePath { get; }
    public string EncodingName { get; }
    public int Length => _text.Length;
    public IReadOnlyList<TextLine> Lines => _lines;

    public static TextDocument Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var (encoding, preambleLength) = DetectEncoding(bytes);
        var text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        return new TextDocument(path, encoding.WebName, text.Replace('\0', ' '));
    }

    public void Reflow(double availableWidth, double fontSize, string fontFamily = "Segoe UI")
    {
        var width = Math.Max(80, availableWidth);
        var lines = new List<TextLine>();
        var position = 0;

        while (position < _text.Length)
        {
            position = SkipLeadingWhitespace(position);
            if (position >= _text.Length)
                break;

            var paragraphEnd = FindLineBreak(position);
            var hardLimit = paragraphEnd >= 0 ? paragraphEnd : _text.Length;
            while (position < hardLimit)
            {
                var fitEnd = FindFittingEnd(position, hardLimit, width, fontSize);
                if (fitEnd <= position)
                    fitEnd = Math.Min(position + 1, hardLimit);

                var chosenEnd = ChooseSemanticBreak(position, fitEnd);
                if (chosenEnd <= position)
                    chosenEnd = fitEnd;

                var display = CollapseWhitespace(_text[position..chosenEnd]).Trim();
                if (display.Length > 0)
                    lines.Add(new TextLine(position, chosenEnd, display));

                position = chosenEnd;
            }

            if (paragraphEnd >= 0 && position >= paragraphEnd)
                position = SkipLineBreak(position);
        }

        _lines = lines;
    }

    public int FindLineIndexAtOffset(int offset)
    {
        if (_lines.Count == 0)
            return 0;

        offset = Math.Clamp(offset, 0, _text.Length);
        var low = 0;
        var high = _lines.Count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var line = _lines[middle];
            if (offset < line.Start)
                high = middle - 1;
            else if (offset >= line.End)
                low = middle + 1;
            else
                return middle;
        }

        return Math.Clamp(low, 0, _lines.Count - 1);
    }

    public int OffsetFromPercent(double percent)
        => (int)Math.Round(Math.Clamp(percent, 0, 100) / 100d * _text.Length);

    public double PercentFromOffset(int offset)
        => _text.Length == 0 ? 0 : Math.Clamp(offset, 0, _text.Length) * 100d / _text.Length;

    public int Find(string value, int startOffset)
    {
        if (string.IsNullOrWhiteSpace(value))
            return -1;
        return _text.IndexOf(value, Math.Clamp(startOffset, 0, _text.Length), StringComparison.CurrentCultureIgnoreCase);
    }

    public int FindPrevious(string value, int beforeOffset)
    {
        if (string.IsNullOrWhiteSpace(value) || _text.Length == 0)
            return -1;
        var start = Math.Clamp(beforeOffset - 1, 0, _text.Length - 1);
        return _text.LastIndexOf(value, start, StringComparison.CurrentCultureIgnoreCase);
    }

    public string PreviewAt(int offset, int radius = 90)
    {
        if (_text.Length == 0)
            return string.Empty;
        var start = Math.Max(0, Math.Clamp(offset, 0, _text.Length) - radius);
        var end = Math.Min(_text.Length, offset + radius);
        return CollapseWhitespace(_text[start..end]).Trim();
    }

    private int SkipLeadingWhitespace(int position)
    {
        while (position < _text.Length && char.IsWhiteSpace(_text[position]))
            position++;
        return position;
    }

    private int FindLineBreak(int start)
    {
        for (var i = start; i < _text.Length; i++)
        {
            if (_text[i] is '\r' or '\n')
                return i;
        }
        return -1;
    }

    private int SkipLineBreak(int position)
    {
        while (position < _text.Length && _text[position] is '\r' or '\n')
            position++;
        return position;
    }

    private int FindFittingEnd(int start, int limit, double width, double fontSize)
    {
        var capacity = width / Math.Max(fontSize, 1);
        var used = 0d;
        var position = start;
        while (position < limit)
        {
            var next = CharacterWidthUnits(_text[position]);
            if (position > start && used + next > capacity)
                break;
            used += next;
            position++;
        }
        return position;
    }

    private static double CharacterWidthUnits(char value)
    {
        if (char.IsWhiteSpace(value))
            return 0.35;
        if (value <= 0x7F)
        {
            if (char.IsLetterOrDigit(value))
                return char.IsUpper(value) ? 0.68 : 0.56;
            return 0.45;
        }
        return 1.0;
    }

    private int ChooseSemanticBreak(int start, int fitEnd)
    {
        var length = fitEnd - start;
        if (length < 4)
            return fitEnd;

        var strongFloor = start + (int)(length * 0.5);
        for (var i = fitEnd - 1; i >= strongFloor; i--)
        {
            if (StrongStops.Contains(_text[i]))
                return i + 1;
        }

        var weakFloor = start + (int)(length * 0.68);
        for (var i = fitEnd - 1; i >= weakFloor; i--)
        {
            if (WeakStops.Contains(_text[i]) || char.IsWhiteSpace(_text[i]))
                return i + 1;
        }

        return fitEnd;
    }

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }
            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }
            builder.Append(c);
        }
        return builder.ToString();
    }

    private static (Encoding Encoding, int PreambleLength) DetectEncoding(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble))
            return (new UTF8Encoding(false, true), Encoding.UTF8.Preamble.Length);
        if (bytes.AsSpan().StartsWith(Encoding.UTF32.Preamble))
            return (Encoding.UTF32, Encoding.UTF32.Preamble.Length);
        var utf32BigEndian = new UTF32Encoding(true, true, true);
        if (bytes.AsSpan().StartsWith(utf32BigEndian.Preamble))
            return (utf32BigEndian, utf32BigEndian.Preamble.Length);
        if (bytes.AsSpan().StartsWith(Encoding.Unicode.Preamble))
            return (Encoding.Unicode, Encoding.Unicode.Preamble.Length);
        if (bytes.AsSpan().StartsWith(Encoding.BigEndianUnicode.Preamble))
            return (Encoding.BigEndianUnicode, Encoding.BigEndianUnicode.Preamble.Length);
        try
        {
            _ = new UTF8Encoding(false, true).GetString(bytes);
            return (new UTF8Encoding(false), 0);
        }
        catch (DecoderFallbackException)
        {
            return (Encoding.GetEncoding("GB18030"), 0);
        }
    }
}
