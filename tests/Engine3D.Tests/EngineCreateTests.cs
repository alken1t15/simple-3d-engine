using Engine3D.OpenGL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class EngineCreateTests
{
    [TestMethod]
    public void Options_defaults_match_contract()
    {
        var options = new EngineOptions();

        Assert.AreEqual(1280, options.Width);
        Assert.AreEqual(720, options.Height);
        Assert.AreEqual("Engine3D Demo", options.Title);
        Assert.IsTrue(options.VSync);
    }

    [TestMethod]
    public void Create_rejects_null_options()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => Engine.Create(null!));
    }

    [TestMethod]
    [DataRow(0, 720)]
    [DataRow(-1, 720)]
    [DataRow(1280, 0)]
    [DataRow(1280, -720)]
    public void Create_rejects_invalid_size_before_creating_a_window(int width, int height)
    {
        // Проверка до обращения к GLFW: тест проходит и без дисплея.
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Engine.Create(new EngineOptions { Width = width, Height = height }));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Create_rejects_empty_title_before_creating_a_window(string title)
    {
        Assert.ThrowsExactly<ArgumentException>(() => Engine.Create(new EngineOptions { Title = title }));
    }

    [TestMethod]
    [WithoutDisplay]
    public void Create_without_display_reports_InvalidOperationException_and_can_be_retried()
    {
        // Ошибку создания окна сообщает GLFW из нативного колбэка; Engine не возвращается, процесс не падает,
        // а повторная попытка дает ту же понятную ошибку, а не следствие прошлой.
        GraphicsTestSupport.AllowTestRunnerThreads();

        for (int attempt = 0; attempt < 2; attempt++)
        {
            var exception = Assert.ThrowsExactly<InvalidOperationException>(() => Engine.Create(new EngineOptions()));
            Assert.StartsWith("Failed to create an OpenGL 3.3 core window: GLFW reported ", exception.Message);
        }
    }
}
