using System.Numerics;
using Engine3D.Core;
using Engine3D.OpenGL;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTK.Graphics.OpenGL4;
using static Engine3D.Tests.GraphicsTestSupport;

namespace Engine3D.Tests;

[TestClass]
[GraphicsTest]
public sealed class OverlayRenderingTests
{
    private static readonly ColorRgba White = new(1, 1, 1);

    [TestMethod]
    public void Panels_use_painter_order_alpha_and_screen_clipping()
    {
        using var engine = CreateEngine(nameof(Panels_use_painter_order_alpha_and_screen_clipping));
        Frame? last = null;
        engine.FrameRendered = (w, h, pixels) => last = new Frame(w, h, pixels);
        engine.SetOverlay(new OverlayFrame([
            new(new UiRect(-10, -20, 110, 120), new ColorRgba(1, 0, 0)),
            new(new UiRect(20, 20, 40, 40), new ColorRgba(0, 0, 1, 0.5f)),
            new(new UiRect(130, 10, 40, 40), new ColorRgba(1, 1, 1, 0.5f))], [], []));
        RunFrames(engine, new Scene(), new Camera(), 2);
        Assert.IsNotNull(last);
        AssertColor(255, 0, 0, At(last, engine, 2, 2), "negative rect clipped at top-left");
        AssertColor(128, 0, 128, At(last, engine, 30, 30), "second panel over first");
        AssertColor(140, 153, 166, At(last, engine, 140, 20), "straight alpha over 3D clear color");
        AssertColor(Level(0.1f), Level(0.2f), Level(0.3f), At(last, engine, 110, 110), "outside panel");
    }

    [TestMethod]
    public void Images_keep_top_down_orientation_tint_and_order_over_panels()
    {
        using var engine = CreateEngine(nameof(Images_keep_top_down_orientation_tint_and_order_over_panels));
        // Строки TextureData сверху вниз: red/green, blue/white.
        var texture = new TextureData(2, 2, [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255]);
        engine.SetOverlay(new OverlayFrame([new(new UiRect(0, 0, 400, 300), new ColorRgba(1, 0, 1))],
            [new(new UiRect(10, 10, 80, 80), texture, White),
             new(new UiRect(110, 10, 80, 80), texture, new ColorRgba(0.5f, 1f, 1f))], []));
        Frame? last = null;
        engine.FrameRendered = (w, h, pixels) => last = new Frame(w, h, pixels);
        RunFrames(engine, new Scene(), new Camera(), 2);
        Assert.IsNotNull(last);
        AssertColor(255, 0, 0, At(last, engine, 15, 15), "image top-left");
        AssertColor(0, 255, 0, At(last, engine, 85, 15), "image top-right");
        AssertColor(0, 0, 255, At(last, engine, 15, 85), "image bottom-left");
        AssertColor(255, 255, 255, At(last, engine, 85, 85), "image bottom-right");
        AssertColor(128, 0, 0, At(last, engine, 115, 15), "image tint");
        Assert.AreEqual(1, engine.OverlayRenderer!.ImageTextureCount);
    }

    [TestMethod]
    public void Text_samples_embedded_glyph_coverage_over_images()
    {
        using var engine = CreateEngine(nameof(Text_samples_embedded_glyph_coverage_over_images));
        var green = new TextureData(1, 1, [0, 255, 0, 255]);
        engine.SetOverlay(new OverlayFrame([new(new UiRect(0, 0, 100, 100), new ColorRgba(1, 0, 0))],
            [new(new UiRect(0, 0, 100, 100), green, White)], [new(new Vector2(10, 10), "M", 63, White)]));
        Frame? last = null;
        engine.FrameRendered = (w, h, pixels) => last = new Frame(w, h, pixels);
        RunFrames(engine, new Scene(), new Camera(), 2);
        Assert.IsNotNull(last);
        var font = BitmapFont.Default;
        int glyph = font.GlyphIndex('M');
        bool checkedSolid = false, checkedCoverage = false;
        for (int y = 1; y < font.CellHeight - 1; y++)
        {
            for (int x = 1; x < font.CellWidth - 1; x++)
            {
                int alpha = font.Texture.Pixels[((glyph / font.Columns * font.CellHeight + y) * font.Texture.Width
                    + glyph % font.Columns * font.CellWidth + x) * 4 + 3];
                if (alpha == 255 && !checkedSolid)
                {
                    AssertColor(255, 255, 255, At(last, engine, x + 10, y + 10), "solid glyph above image");
                    checkedSolid = true;
                }
                if (alpha is > 40 and < 210 && !checkedCoverage)
                {
                    AssertColor(alpha, 255, alpha, At(last, engine, x + 10, y + 10), "glyph coverage blends over image");
                    checkedCoverage = true;
                }
            }
        }
        Assert.IsTrue(checkedSolid && checkedCoverage);
        AssertColor(0, 255, 0, At(last, engine, 10, 10), "transparent glyph padding preserves image");
    }

    [TestMethod]
    public void Clearing_overlay_restores_exact_3D_frame_and_keeps_depth_on_later_frames()
    {
        using var engine = CreateEngine(nameof(Clearing_overlay_restores_exact_3D_frame_and_keeps_depth_on_later_frames));
        var scene = new Scene();
        scene.Lighting.AmbientColor = Vector3.One;
        scene.Lighting.DirectionalColor = Vector3.Zero;
        var mesh = PrimitiveFactory.CreateCube();
        AddObject(scene, mesh, new ColorRgba(0, 1, 0), new Vector3(0, 0, 1));
        AddObject(scene, mesh, new ColorRgba(1, 0, 0), new Vector3(0, 0, -1), new Vector3(2));
        var camera = new Camera { Position = new Vector3(0, 0, 5), Target = Vector3.Zero };
        var overlay = new OverlayFrame([new(new UiRect(0, 0, 400, 300), new ColorRgba(0, 0, 1))], [], []);
        Dictionary<int, Frame> frames = [];
        int stage = 0;
        engine.FrameRendered = (w, h, pixels) => frames[stage] = new Frame(w, h, pixels);
        RunFrames(engine, scene, camera, 4, i =>
        {
            stage = i;
            engine.SetOverlay(i is 1 or 3 ? overlay : null);
        });
        AssertColor(0, 255, 0, frames[0].At(camera, Vector3.Zero), "baseline depth");
        AssertColor(0, 0, 255, frames[1].At(camera, Vector3.Zero), "overlay ignores scene depth");
        CollectionAssert.AreEqual(frames[0].Pixels, frames[2].Pixels, "null restores unchanged 3D, including depth");
        AssertColor(0, 0, 255, frames[3].At(camera, Vector3.Zero), "overlay can return");
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public void Logical_coordinates_scale_once_to_physical_framebuffer(int scale)
    {
        using var engine = CreateEngine(nameof(Logical_coordinates_scale_once_to_physical_framebuffer));
        var renderer = new OverlayRenderer();
        try
        {
            var physical = engine.FramebufferSize;
            GL.ClearColor(0, 0, 0, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            renderer.Render(new OverlayFrame([new(new UiRect(10, 10, 20, 20), White)], [], []),
                physical.Width / scale, physical.Height / scale, physical.Width, physical.Height);
            var pixels = new byte[physical.Width * physical.Height * 4];
            GL.ReadPixels(0, 0, physical.Width, physical.Height, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            var frame = new Frame(physical.Width, physical.Height, pixels);
            AssertColor(255, 255, 255, frame.At(15 * scale, physical.Height - 15 * scale - 1), "scaled logical interior");
            AssertColor(0, 0, 0, frame.At(5 * scale, physical.Height - 15 * scale - 1), "before scaled rect");
            AssertColor(0, 0, 0, frame.At(35 * scale, physical.Height - 15 * scale - 1), "after scaled rect");
        }
        finally { Release(renderer); }
    }

    [TestMethod]
    public void Overlay_restores_GL_state_even_during_lazy_upload_and_failed_image_upload()
    {
        using var engine = CreateEngine(nameof(Overlay_restores_GL_state_even_during_lazy_upload_and_failed_image_upload));
        var renderer = new OverlayRenderer();
        int vao = GL.GenVertexArray(), buffer = GL.GenBuffer(), texture = GL.GenTexture(), sampler = GL.GenSampler();
        try
        {
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, buffer);
            GL.BindBuffer(BufferTarget.PixelUnpackBuffer, buffer);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.BindSampler(0, sampler);
            GL.ActiveTexture(TextureUnit.Texture3);
            GL.Enable(EnableCap.DepthTest);
            GL.DepthMask(false);
            GL.DepthFunc(DepthFunction.Gequal);
            GL.Enable(EnableCap.Blend);
            GL.Enable(EnableCap.CullFace);
            GL.Enable(EnableCap.StencilTest);
            GL.Enable(EnableCap.ScissorTest);
            GL.Scissor(3, 4, 5, 6);
            GL.ColorMask(false, true, false, true);
            GL.BlendFuncSeparate(BlendingFactorSrc.DstAlpha, BlendingFactorDest.One,
                BlendingFactorSrc.OneMinusDstAlpha, BlendingFactorDest.Zero);
            GL.BlendEquationSeparate(BlendEquationMode.FuncReverseSubtract, BlendEquationMode.Max);
            GL.Viewport(7, 8, 90, 100);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 8);
            GL.PixelStore(PixelStoreParameter.UnpackRowLength, 7);
            GL.PixelStore(PixelStoreParameter.UnpackSkipRows, 2);
            GL.PixelStore(PixelStoreParameter.UnpackSkipPixels, 1);
            var before = State();
            var valid = new OverlayFrame([new(new UiRect(0, 0, 30, 30), White)], [],
                [new(Vector2.Zero, "Coins 3/10", 20, White)]);
            renderer.Render(valid, 400, 300, 400, 300);
            CollectionAssert.AreEqual(before, State(), "first pass restores state including uploads");
            renderer.Render(valid, 400, 300, 400, 300);
            CollectionAssert.AreEqual(before, State(), "warm pass restores state");

            int width = GL.GetInteger(GetPName.MaxTextureSize) + 1;
            var tooLarge = new TextureData(width, 1, new byte[width * 4]);
            var invalid = new OverlayFrame([], [new(new UiRect(0, 0, 30, 30), tooLarge, White)], []);
            Assert.ThrowsExactly<InvalidOperationException>(() => renderer.Render(invalid, 400, 300, 400, 300));
            CollectionAssert.AreEqual(before, State(), "failed upload restores state");
            Assert.AreEqual(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            Release(renderer);
            GL.BindSampler(0, 0);
            GL.DeleteSampler(sampler);
            GL.DeleteTexture(texture);
            GL.DeleteBuffer(buffer);
            GL.DeleteVertexArray(vao);
        }
    }

    [TestMethod]
    public void Buffers_font_and_images_are_reused_and_deleted_before_context_disposal()
    {
        using var engine = CreateEngine(nameof(Buffers_font_and_images_are_reused_and_deleted_before_context_disposal));
        var renderer = new OverlayRenderer();
        var texture = new TextureData(1, 1, [255, 255, 255, 255]);
        try
        {
            for (int i = 0; i < 50; i++)
                renderer.Render(new OverlayFrame([], [new(new UiRect(0, 0, 10, 10), texture, White)],
                    [new(Vector2.Zero, $"Coins {i}/50", 20, White)]), 400, 300, 400, 300);
            Assert.AreEqual(1, renderer.ImageTextureCount);
            int vao = renderer.VertexArray, buffer = renderer.VertexBuffer;
            int font = renderer.FontTexture, white = renderer.WhiteTexture;
            Assert.IsTrue(GL.IsVertexArray(vao) && GL.IsBuffer(buffer) && GL.IsTexture(font) && GL.IsTexture(white));
            Release(renderer);
            Assert.IsFalse(GL.IsVertexArray(vao) || GL.IsBuffer(buffer) || GL.IsTexture(font) || GL.IsTexture(white));
            Assert.AreEqual(0, renderer.ImageTextureCount);
            Release(renderer);
        }
        finally { Release(renderer); }
    }

    [TestMethod]
    public void SetOverlay_enforces_owner_thread_update_scope_and_disposal()
    {
        var engine = CreateEngine(nameof(SetOverlay_enforces_owner_thread_update_scope_and_disposal));
        try
        {
            engine.SetOverlay(new OverlayFrame([], [], []));
            engine.SetOverlay(null);
            Assert.ThrowsExactly<InvalidOperationException>(() => Task.Run(() => engine.SetOverlay(null)).GetAwaiter().GetResult());
            engine.FrameRendered = (_, _, _) => Assert.ThrowsExactly<InvalidOperationException>(() => engine.SetOverlay(null));
            RunFrames(engine, new Scene(), new Camera(), 2, _ => engine.SetOverlay(null));
            Assert.ThrowsExactly<InvalidOperationException>(() => engine.SetOverlay(null));
        }
        finally { engine.Dispose(); }
        engine.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => engine.SetOverlay(null));
    }

    private static Rgb At(Frame frame, Engine engine, int x, int y)
    {
        var logical = engine.WindowBounds;
        return frame.At((int)((x + 0.5) / logical.Width * frame.Width),
            frame.Height - 1 - (int)((y + 0.5) / logical.Height * frame.Height));
    }

    private static int[] State()
    {
        GetPName[] names = [GetPName.CurrentProgram, GetPName.VertexArrayBinding, GetPName.ArrayBufferBinding,
            GetPName.ActiveTexture, GetPName.PixelUnpackBufferBinding, GetPName.UnpackAlignment, GetPName.UnpackRowLength,
            GetPName.UnpackSkipRows, GetPName.UnpackSkipPixels, GetPName.DepthWritemask, GetPName.DepthFunc,
            GetPName.BlendSrcRgb, GetPName.BlendDstRgb, GetPName.BlendSrcAlpha, GetPName.BlendDstAlpha,
            GetPName.BlendEquationRgb, GetPName.BlendEquationAlpha];
        var result = names.Select(GL.GetInteger).ToList();
        foreach (var cap in new[] { EnableCap.DepthTest, EnableCap.Blend, EnableCap.CullFace, EnableCap.ScissorTest, EnableCap.StencilTest })
            result.Add(GL.IsEnabled(cap) ? 1 : 0);
        foreach (var name in new[] { GetPName.Viewport, GetPName.ScissorBox, GetPName.ColorWritemask })
        {
            int[] values = new int[4];
            GL.GetInteger(name, values);
            result.AddRange(values);
        }
        int active = GL.GetInteger(GetPName.ActiveTexture);
        GL.ActiveTexture(TextureUnit.Texture0);
        result.Add(GL.GetInteger(GetPName.TextureBinding2D));
        result.Add(GL.GetInteger(GetPName.SamplerBinding));
        GL.ActiveTexture((TextureUnit)active);
        return result.ToArray();
    }

    private static void Release(OverlayRenderer renderer)
    {
        List<Exception> errors = [];
        renderer.Release(errors);
        Assert.HasCount(0, errors);
    }
}
