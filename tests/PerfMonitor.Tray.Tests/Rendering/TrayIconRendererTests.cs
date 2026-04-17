using FluentAssertions;
using PerfMonitor.Tray.Rendering;
using System.Drawing;

namespace PerfMonitor.Tray.Tests.Rendering;

public class TrayIconRendererTests
{
    [Fact]
    public void Render_Load50_ProducesDifferentPixelsThanLoad0()
    {
        using var r = new TrayIconRenderer();
        using var b0 = r.RenderLoadBitmap(0, Color.Orange);
        using var b50 = r.RenderLoadBitmap(50, Color.Orange);
        HashBitmap(b0).Should().NotBe(HashBitmap(b50));
    }

    [Fact]
    public void Render_SameInputTwice_IsDeterministic()
    {
        using var r = new TrayIconRenderer();
        using var a = r.RenderLoadBitmap(42, Color.Cyan);
        using var b = r.RenderLoadBitmap(42, Color.Cyan);
        HashBitmap(a).Should().Be(HashBitmap(b));
    }

    private static string HashBitmap(Bitmap bmp)
    {
        var bytes = new byte[bmp.Width * bmp.Height * 4];
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try { System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length); }
        finally { bmp.UnlockBits(data); }
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    }
}
