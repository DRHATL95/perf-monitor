using PerfMonitor.Tray.Interop;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace PerfMonitor.Tray.Rendering;

public sealed class TrayIconRenderer : IDisposable
{
    public int Size { get; init; } = 16;

    public Bitmap RenderLoadBitmap(float loadPercent, Color accent)
    {
        var bmp = new Bitmap(Size, Size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        using var bg = new SolidBrush(Color.FromArgb(200, 20, 23, 42));
        g.FillRectangle(bg, 0, 0, Size, Size);

        using var fill = new SolidBrush(accent);
        var filled = (int)Math.Round(Size * Math.Clamp(loadPercent, 0, 100) / 100f);
        g.FillRectangle(fill, 0, Size - filled, Size, filled);

        using var text = new SolidBrush(Color.White);
        using var font = new Font("Segoe UI", 7.5f, FontStyle.Bold, GraphicsUnit.Pixel);
        var label = loadPercent >= 100 ? "MX" : $"{(int)loadPercent}";
        var size = g.MeasureString(label, font);
        g.DrawString(label, font, text,
            (Size - size.Width) / 2,
            (Size - size.Height) / 2);
        return bmp;
    }

    public Icon ToIcon(Bitmap bmp)
    {
        var handle = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { User32.DestroyIcon(handle); }
    }

    public void Dispose() { }
}
