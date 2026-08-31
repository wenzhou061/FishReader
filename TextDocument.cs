using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace FishReader;

internal readonly record struct TextLine(int Start, int End, string Text);

internal sealed class TextDocument
{
    private static readonly char[] StrongStops = ['。', '！', '？', '!', '?', '；', ';'];
    private static readonly char[] WeakStops = ['，', ',', '、', '：', ':'];
    private const string ForbiddenAtLineStart = "、。，．？！；：,.;:!?)]}）〕］】〉》」』〗〙〛’”〞々…—～";
    private const string ForbiddenAtLineEnd = "([{（〔［【〈《「『〖〘〚‘“";
    private static readonly Regex ChapterPattern = new(
        @"^[\t \u3000]{0,4}(?:第[零〇一二三四五六七八九十百千万两\d]{1,12}[章回卷节部篇]|序章|楔子|后记)(?:[^\r\n]{0,40})?\r?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private readonly string _text;
    private readonly int[] _chapterOffsets;
    private List<TextLine> _lines = [];

    private TextDocument(string path, string encodingName, string text)
    {
        FilePath = path;
        EncodingName = encodingName;
        _text = text;
        _chapterOffsets = ChapterPattern.Matches(text).Select(match => match.Index).ToArray();
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

    public void Reflow(double availableWidth, double fontSize, string fontFamily = "Segoe UI",
        string fontWeightName = "Normal")
    {
        var width = Math.Max(80, availableWidth) - 2;
        var measurer = new GlyphWidthMeasurer(fontFamily, fontWeightName, fontSize);
        var lines = new List<TextLine>();
        var position = 0;

        while (position < _text.Length)
        {
            position = SkipLeadingWhitespace(position);
            if (position >= _text.Length)
                break;

            var fitEnd = FindFittingEnd(position, _text.Length, width, measurer);
            if (fitEnd <= position)
                fitEnd = Math.Min(position + 1, _text.Length);

            var chosenEnd = ChooseSemanticBreak(position, fitEnd, width, measurer);
            chosenEnd = ApplyChineseLineBreakRules(position, chosenEnd, _text.Length);
            if (chosenEnd <= position)
                chosenEnd = fitEnd;

            var display = CollapseWhitespace(_text[position..chosenEnd]).Trim();
            if (display.Length > 0)
                lines.Add(new TextLine(position, chosenEnd, display));

            position = chosenEnd;
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

    public int FindNextChapter(int afterOffset)
    {
        var index = Array.BinarySearch(_chapterOffsets, Math.Clamp(afterOffset + 1, 0, _text.Length));
        if (index < 0)
            index = ~index;
        return index < _chapterOffsets.Length ? _chapterOffsets[index] : -1;
    }

    public int FindPreviousChapter(int beforeOffset)
    {
        var index = Array.BinarySearch(_chapterOffsets, Math.Clamp(beforeOffset - 1, 0, _text.Length));
        if (index < 0)
            index = ~index - 1;
        return index >= 0 ? _chapterOffsets[index] : -1;
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

    private int FindFittingEnd(int start, int limit, double width, GlyphWidthMeasurer measurer)
    {
        var used = 0d;
        var position = start;
        while (position < limit)
        {
            if (char.IsWhiteSpace(_text[position]))
            {
                var whitespaceEnd = FindWhitespaceEnd(position, limit, out var containsLineBreak,
                    out var containsVisibleSpace);
                var previous = PreviousNonWhitespace(position - 1, start);
                var next = whitespaceEnd < limit ? _text[whitespaceEnd] : '\0';
                var whitespaceWidth = ShouldDisplaySpace(previous >= start ? _text[previous] : '\0', next,
                    containsLineBreak, containsVisibleSpace) ? measurer.SpaceWidth : 0;
                if (position > start && used + whitespaceWidth > width)
                    break;
                used += whitespaceWidth;
                position = whitespaceEnd;
                continue;
            }

            var nextWidth = measurer.WidthOf(_text[position]);
            if (position > start && used + nextWidth > width)
                break;
            used += nextWidth;
            position++;
        }
        return position;
    }

    private int ChooseSemanticBreak(int start, int fitEnd, double width, GlyphWidthMeasurer measurer)
    {
        if (fitEnd - start < 4)
            return fitEnd;

        var strongFloor = FindWidthPosition(start, fitEnd, width * 0.82, measurer);
        for (var i = fitEnd - 1; i >= strongFloor; i--)
        {
            if (StrongStops.Contains(_text[i]))
                return i + 1;
        }

        var weakFloor = FindWidthPosition(start, fitEnd, width * 0.90, measurer);
        for (var i = fitEnd - 1; i >= weakFloor; i--)
        {
            if (WeakStops.Contains(_text[i]) || char.IsWhiteSpace(_text[i]))
                return i + 1;
        }

        return fitEnd;
    }

    private int FindWidthPosition(int start, int limit, double targetWidth, GlyphWidthMeasurer measurer)
    {
        var used = 0d;
        var position = start;
        while (position < limit)
        {
            if (char.IsWhiteSpace(_text[position]))
            {
                position = FindWhitespaceEnd(position, limit, out _, out _);
                continue;
            }
            used += measurer.WidthOf(_text[position]);
            if (used >= targetWidth)
                return position;
            position++;
        }
        return position;
    }

    private int ApplyChineseLineBreakRules(int start, int end, int limit)
    {
        var next = NextNonWhitespace(end, limit);
        if (next < limit && ForbiddenAtLineStart.Contains(_text[next]))
        {
            var previous = PreviousNonWhitespace(end - 1, start);
            while (previous > start && ForbiddenAtLineStart.Contains(_text[previous]))
                previous = PreviousNonWhitespace(previous - 1, start);
            if (previous > start)
                end = previous;
        }

        var last = PreviousNonWhitespace(end - 1, start);
        if (last > start && ForbiddenAtLineEnd.Contains(_text[last]))
            end = last;
        return end;
    }

    private int FindWhitespaceEnd(int position, int limit, out bool containsLineBreak,
        out bool containsVisibleSpace)
    {
        containsLineBreak = false;
        containsVisibleSpace = false;
        while (position < limit && char.IsWhiteSpace(_text[position]))
        {
            containsLineBreak |= _text[position] is '\r' or '\n';
            containsVisibleSpace |= _text[position] is ' ' or '\t';
            position++;
        }
        return position;
    }

    private int PreviousNonWhitespace(int position, int floor)
    {
        while (position >= floor && char.IsWhiteSpace(_text[position]))
            position--;
        return position;
    }

    private int NextNonWhitespace(int position, int limit)
    {
        while (position < limit && char.IsWhiteSpace(_text[position]))
            position++;
        return position;
    }

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (!char.IsWhiteSpace(c))
            {
                builder.Append(c);
                continue;
            }

            var containsLineBreak = false;
            var containsVisibleSpace = false;
            while (i < value.Length && char.IsWhiteSpace(value[i]))
            {
                containsLineBreak |= value[i] is '\r' or '\n';
                containsVisibleSpace |= value[i] is ' ' or '\t';
                i++;
            }
            var next = i < value.Length ? value[i] : '\0';
            var previous = builder.Length > 0 ? builder[^1] : '\0';
            if (ShouldDisplaySpace(previous, next, containsLineBreak, containsVisibleSpace))
                builder.Append(' ');
            i--;
        }
        return builder.ToString();
    }

    private static bool ShouldDisplaySpace(char previous, char next, bool containsLineBreak,
        bool containsVisibleSpace)
    {
        if (previous == '\0' || next == '\0' || ForbiddenAtLineStart.Contains(next))
            return false;
        if (!containsLineBreak)
            return containsVisibleSpace;
        return IsAsciiWord(previous) && IsAsciiWord(next);
    }

    private static bool IsAsciiWord(char value)
        => value <= 0x7F && (char.IsLetterOrDigit(value) || value is '_' or '-' or '\'' or '"');

    private sealed class GlyphWidthMeasurer
    {
        private readonly GlyphTypeface? _glyphTypeface;
        private readonly double _fontSize;
        private readonly Dictionary<char, double> _cache = [];

        public GlyphWidthMeasurer(string fontFamily, string fontWeightName, double fontSize)
        {
            _fontSize = Math.Max(1, fontSize);
            var weight = fontWeightName switch
            {
                "Medium" => FontWeights.Medium,
                "SemiBold" => FontWeights.SemiBold,
                _ => FontWeights.Normal
            };
            try
            {
                var typeface = new Typeface(new FontFamily(fontFamily), FontStyles.Normal, weight,
                    FontStretches.Normal);
                if (typeface.TryGetGlyphTypeface(out var glyphTypeface))
                    _glyphTypeface = glyphTypeface;
            }
            catch
            {
                _glyphTypeface = null;
            }
        }

        public double SpaceWidth => WidthOf(' ');

        public double WidthOf(char value)
        {
            if (_cache.TryGetValue(value, out var width))
                return width;

            if (_glyphTypeface is not null && _glyphTypeface.CharacterToGlyphMap.TryGetValue(value, out var glyph))
                width = _glyphTypeface.AdvanceWidths[glyph] * _fontSize;
            else if (value <= 0x7F)
                width = _fontSize * (char.IsLetterOrDigit(value) ? 0.58 : 0.45);
            else
                width = _fontSize;

            width = Math.Max(0, width);
            _cache[value] = width;
            return width;
        }
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
