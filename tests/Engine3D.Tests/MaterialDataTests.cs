using Engine3D.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class MaterialDataTests
{
    [TestMethod]
    public void Defaults_to_opaque_white_without_texture()
    {
        var material = new MaterialData();

        Assert.AreEqual(new ColorRgba(1f, 1f, 1f, 1f), material.BaseColor);
        Assert.IsNull(material.Texture);
        material.Validate();
    }

    [TestMethod]
    public void Color_alpha_defaults_to_one()
    {
        Assert.AreEqual(1f, new ColorRgba(0.5f, 0.5f, 0.5f).A);
    }

    [TestMethod]
    [DataRow(1.5f, 0f, 0f, 1f)]
    [DataRow(0f, -0.1f, 0f, 1f)]
    [DataRow(0f, 0f, float.NaN, 1f)]
    [DataRow(0f, 0f, 0f, float.PositiveInfinity)]
    public void Invalid_color_can_be_assigned_but_fails_validation(float r, float g, float b, float a)
    {
        var material = new MaterialData { BaseColor = new ColorRgba(r, g, b, a) };

        Assert.ThrowsExactly<InvalidOperationException>(material.Validate);
    }

    [TestMethod]
    public void One_texture_can_be_shared_by_materials()
    {
        var texture = new TextureData(1, 1, new byte[4]);
        var first = new MaterialData { Texture = texture };
        var second = new MaterialData { Texture = texture, BaseColor = new ColorRgba(1f, 0.5f, 0.2f) };

        Assert.AreSame(first.Texture, second.Texture);
    }
}
