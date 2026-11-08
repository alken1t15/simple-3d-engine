using System.Runtime.InteropServices;
using Engine3D.Core;
using Engine3D.OpenGL;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Engine3D.Tests.GraphicsTestSupport;

namespace Engine3D.Tests;

[TestClass]
[GraphicsTest]
public sealed class WindowIconWindowsTests
{
    public const string Scenario = "window-icon-windows";
    public static int Run()
    {
        try { new WindowIconWindowsTests().Native_big_and_small_icons_contain_the_supplied_pixels(); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    [TestMethod]
    public void Native_big_and_small_icons_contain_the_supplied_pixels()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Requires Windows WM_GETICON/GDI.");
        AllowTestRunnerThreads();
        const int size = 32;
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            var color = Color(x < 16, y < 16);
            int at = (y * size + x) * 4;
            pixels[at] = color.R; pixels[at + 1] = color.G; pixels[at + 2] = color.B; pixels[at + 3] = 255;
        }
        string title = $"Engine icon test {Guid.NewGuid()}";
        using var engine = Engine.Create(new EngineOptions { Title = title, Width = 128, Height = 96,
            Position = (-10000, -10000), FocusOnShow = false, VSync = false });
        Assert.IsTrue(engine.SetWindowIcon(new TextureData(size, size, pixels)));
        bool verified = false;
        engine.Run(new Scene(), new Camera(), _ =>
        {
            nint window = FindWindow(null, title);
            Assert.AreNotEqual(nint.Zero, window);
            foreach (int kind in new[] { 0, 1 }) // ICON_SMALL / ICON_BIG: title bar and taskbar.
            {
                nint icon = SendMessage(window, 0x007f, kind, 0);
                Assert.AreNotEqual(nint.Zero, icon);
                Assert.IsTrue(GetIconInfo(icon, out var info));
                try
                {
                    Assert.AreNotEqual(0, GetObject(info.Color, Marshal.SizeOf<Bitmap>(), out var bitmap));
                    var data = new byte[bitmap.Width * bitmap.Height * 4];
                    var dib = new BitmapInfo { Size = 40, Width = bitmap.Width, Height = -bitmap.Height, Planes = 1, Bits = 32 };
                    nint dc = GetDC(0);
                    try { Assert.AreEqual((uint)bitmap.Height, GetDIBits(dc, info.Color, 0, (uint)bitmap.Height, data, ref dib, 0)); }
                    finally { ReleaseDC(0, dc); }
                    foreach (var (left, top) in new[] { (true, true), (false, true), (true, false), (false, false) })
                    {
                        int x = bitmap.Width * (left ? 1 : 3) / 4, y = bitmap.Height * (top ? 1 : 3) / 4;
                        int at = (y * bitmap.Width + x) * 4;
                        var color = Color(left, top);
                        Assert.AreEqual(color.R, data[at + 2]);
                        Assert.AreEqual(color.G, data[at + 1]);
                        Assert.AreEqual(color.B, data[at]);
                    }
                }
                finally { DeleteObject(info.Color); DeleteObject(info.Mask); }
            }
            verified = true;
            engine.RequestClose();
        });
        Assert.IsTrue(verified);
        Console.WriteLine("PASS: native WM_GETICON big/small pixels match RGBA input");
    }

    private static (byte R, byte G, byte B) Color(bool left, bool top) => (left, top) switch
    { (true, true) => (45, 160, 245), (false, true) => (70, 220, 180), (true, false) => (250, 150, 65), _ => (245, 220, 115) };
    [StructLayout(LayoutKind.Sequential)] private struct IconInfo
    { [MarshalAs(UnmanagedType.Bool)] public bool IsIcon; public uint X, Y; public nint Mask, Color; }
    [StructLayout(LayoutKind.Sequential)] private struct Bitmap
    { public int Type, Width, Height, Stride; public ushort Planes, Bits; public nint Data; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    { public uint Size; public int Width, Height; public ushort Planes, Bits; public uint Compression, SizeImage; public int XPels, YPels; public uint Colors, Important; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string? className, string title);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(nint icon, out IconInfo info);
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")] private static extern int GetObject(nint handle, int size, out Bitmap bitmap);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] private static extern uint GetDIBits(nint dc, nint bitmap, uint start, uint lines, [Out] byte[] data, ref BitmapInfo info, uint usage);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint handle);
}
