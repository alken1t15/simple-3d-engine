using System.Numerics;
using Engine3D.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class SceneObjectTests
{
    private static readonly MeshData Mesh = new(
        [
            new Vertex(Vector3.Zero, Vector2.Zero, Vector3.UnitZ),
            new Vertex(Vector3.UnitX, Vector2.UnitX, Vector3.UnitZ),
            new Vertex(Vector3.UnitY, Vector2.UnitY, Vector3.UnitZ),
        ],
        [0u, 1u, 2u]);

    [TestMethod]
    public void Rejects_null_mesh_and_material()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new SceneObject(null!, new MaterialData()));
        Assert.ThrowsExactly<ArgumentNullException>(() => new SceneObject(Mesh, null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => new SceneObject(Mesh, new MaterialData()).Material = null!);
    }

    [TestMethod]
    public void Objects_on_one_mesh_have_independent_transforms_and_materials()
    {
        var first = new SceneObject(Mesh, new MaterialData());
        var second = new SceneObject(Mesh, new MaterialData());

        first.Transform.Position = new Vector3(5f, 0f, 0f);
        second.Material = new MaterialData { BaseColor = new ColorRgba(1f, 0f, 0f) };

        Assert.AreSame(first.Mesh, second.Mesh);
        Assert.AreNotSame(first.Transform, second.Transform);
        Assert.AreEqual(Vector3.Zero, second.Transform.Position);
        Assert.AreEqual(new ColorRgba(1f, 1f, 1f, 1f), first.Material.BaseColor);
    }
}
