using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using FishReader;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Assert(HotKeyBinding.TryParse("ctrl + alt + b", out var normalizedHotKey, out _) &&
       normalizedHotKey.Display == "Ctrl+Alt+B", "hotkey normalization");
Assert(HotKeyBinding.TryParse("F8", out _, out _), "function-key hotkey");
Assert(!HotKeyBinding.TryParse("B", out _, out _), "unsafe bare letter rejected");
Assert(!HotKeyBinding.TryParse("Alt+B+C", out _, out _), "multiple keys rejected");
var testDirectory = Path.Combine(Path.GetTempPath(), "FishReader-SmokeTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testDirectory);

try
{
    var sample = "第一章 绯红\r\n枪械、大炮、巨舰、飞空艇，差分机、魔药、占卜、诅咒，倒吊人、封印物……光明依旧照耀。\r\n神秘从未远离，这是下一段内容。";
    var utf8Path = Path.Combine(testDirectory, "utf8.txt");
    File.WriteAllText(utf8Path, sample, new UTF8Encoding(false));
    var utf8 = TextDocument.Load(utf8Path);
    utf8.Reflow(260, 16);
    Assert(utf8.EncodingName == "utf-8", "UTF-8 detection");
    Assert(utf8.Lines.Count >= 3, "semantic reflow");
    Assert(utf8.Lines.All(line => !string.IsNullOrWhiteSpace(line.Text)), "no empty lines");
    Assert(utf8.Find("光明依旧照耀", 0) >= 0, "text search");
    Assert(utf8.PreviewAt(utf8.OffsetFromPercent(50)).Length > 0, "percent preview");

    var gbPath = Path.Combine(testDirectory, "gb18030.txt");
    File.WriteAllBytes(gbPath, Encoding.GetEncoding("GB18030").GetBytes(sample));
    var gb = TextDocument.Load(gbPath);
    gb.Reflow(260, 16);
    Assert(gb.EncodingName.Equals("gb18030", StringComparison.OrdinalIgnoreCase), "GB18030 fallback");
    Assert(gb.Lines.Any(line => line.Text.Contains("神秘从未远离")), "GB18030 content");

    foreach (var (name, encoding) in new (string, Encoding)[]
             {
                 ("utf32-le", new UTF32Encoding(false, true, true)),
                 ("utf32-be", new UTF32Encoding(true, true, true))
             })
    {
        var path = Path.Combine(testDirectory, name + ".txt");
        File.WriteAllBytes(path, encoding.GetPreamble().Concat(encoding.GetBytes(sample)).ToArray());
        var document = TextDocument.Load(path);
        document.Reflow(260, 16);
        Assert(document.Lines.Any(line => line.Text.Contains("神秘从未远离")), name + " BOM detection");
    }

    Assert(utf8.FindPrevious("神秘", utf8.Length) >= 0, "reverse search");

    var longPath = Path.Combine(testDirectory, "long-single-line.txt");
    File.WriteAllText(longPath, string.Concat(Enumerable.Repeat("这是一段没有换行符的长文本，", 12000)), new UTF8Encoding(false));
    var stopwatch = Stopwatch.StartNew();
    var longDocument = TextDocument.Load(longPath);
    longDocument.Reflow(260, 16);
    stopwatch.Stop();
    Assert(longDocument.Lines.Count > 5000, "long single-line reflow");
    Assert(stopwatch.Elapsed < TimeSpan.FromSeconds(10), "long single-line performance");

    if (args.Length > 0)
    {
        stopwatch.Restart();
        var external = TextDocument.Load(args[0]);
        external.Reflow(284, 16);
        stopwatch.Stop();
        Console.WriteLine($"EXTERNAL: {external.Length} chars, {external.Lines.Count} lines in {stopwatch.ElapsedMilliseconds} ms, " +
                          $"encoding={external.EncodingName}");
    }

    Console.WriteLine($"PASS: {utf8.Lines.Count} UTF-8 lines, {gb.Lines.Count} GB18030 lines, " +
                      $"{longDocument.Lines.Count} long-text lines in {stopwatch.ElapsedMilliseconds} ms");
    return 0;
}
finally
{
    foreach (var file in Directory.EnumerateFiles(testDirectory))
        File.Delete(file);
    Directory.Delete(testDirectory);
}

static void Assert(bool condition, string name)
{
    if (!condition)
        throw new InvalidOperationException("FAIL: " + name);
}
