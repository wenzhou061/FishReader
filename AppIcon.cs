using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

namespace FishReader;

internal static class AppIcon
{
    public static Drawing.Icon CreateTrayIcon()
    {
        using var bitmap = CreateBitmap(32);
        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Drawing.Icon.FromHandle(handle);
            return (Drawing.Icon)temporary.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    public static System.Windows.Media.ImageSource CreateWindowIcon()
    {
        using var icon = CreateTrayIcon();
        var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }

    private static Drawing.Bitmap CreateBitmap(int size)
    {
        var bitmap = new Drawing.Bitmap(size, size, Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Drawing.Color.Transparent);
        using var background = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 52, 52, 52));
        graphics.FillRoundedRectangle(background, new Drawing.RectangleF(2, 2, size - 4, size - 4), 6);
        using var line = new Drawing.Pen(Drawing.Color.FromArgb(235, 222, 222, 222), 2.2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawLine(line, 8, 10, 24, 10);
        graphics.DrawLine(line, 8, 16, 21, 16);
        graphics.DrawLine(line, 8, 22, 18, 22);
        return bitmap;
    }

    private static void FillRoundedRectangle(this Drawing.Graphics graphics, Drawing.Brush brush,
        Drawing.RectangleF bounds, float radius)
    {
        using var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }
}
