using System.Numerics;
using Engine3D.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class SceneTests
{
    private static MeshData Triangle() => new(
        [
            new Vertex(Vector3.Zero, Vector2.Zero, Vector3.UnitZ),
            new Vertex(Vector3.UnitX, Vector2.UnitX, Vector3.UnitZ),
            new Vertex(Vector3.UnitY, Vector2.UnitY, Vector3.UnitZ),
        ],
        [0u, 1u, 2u]);

    private static SceneObject NewObject(MeshData? mesh = null) => new(mesh ?? Triangle(), new MaterialData());

    [TestMethod]
    public void New_scene_is_empty_and_has_default_lighting()
    {
        var scene = new Scene();

        Assert.IsEmpty(scene.Objects);
        Assert.IsNotNull(scene.Lighting);
    }

    [TestMethod]
    public void Add_and_remove_change_membership_and_keep_order()
    {
        var scene = new Scene();
        var first = NewObject();
        var second = NewObject();
        var third = NewObject();

        scene.Add(first);
        scene.Add(second);
        scene.Add(third);
        CollectionAssert.AreEqual(new[] { first, second, third }, scene.Objects.ToArray());

        Assert.IsTrue(scene.Remove(second));
        CollectionAssert.AreEqual(new[] { first, third }, scene.Objects.ToArray());
        Assert.IsFalse(scene.Remove(second));
    }

    [TestMethod]
    public void Removed_object_can_be_added_again()
    {
        var scene = new Scene();
        var item = NewObject();

        scene.Add(item);
        scene.Remove(item);
        scene.Add(item);

        Assert.AreSame(item, Assert.ContainsSingle(scene.Objects));
    }

    [TestMethod]
    public void Rejects_duplicate_and_null()
    {
        var scene = new Scene();
        var item = NewObject();
        scene.Add(item);

        Assert.ThrowsExactly<ArgumentException>(() => scene.Add(item));
        Assert.ThrowsExactly<ArgumentNullException>(() => scene.Add(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => scene.Remove(null!));
        Assert.HasCount(1, scene.Objects);
    }

    [TestMethod]
    public void Objects_cannot_be_modified_through_the_list()
    {
        var scene = new Scene();

        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<SceneObject>)scene.Objects).Add(NewObject()));
    }

    [TestMethod]
    public void One_mesh_is_shared_by_many_objects_and_survives_removal_of_one()
    {
        var mesh = Triangle();
        var scene = new Scene();
        for (int i = 0; i < 1000; i++)
        {
            var item = NewObject(mesh);
            item.Transform.Position = new Vector3(i, 0f, 0f);
            scene.Add(item);
        }

        Assert.IsTrue(scene.Remove(scene.Objects[0]));

        Assert.HasCount(999, scene.Objects);
        foreach (var item in scene.Objects)
            Assert.AreSame(mesh, item.Mesh);
        Assert.AreEqual(1, mesh.TriangleCount);
        CollectionAssert.AreEqual(new[] { 0u, 1u, 2u }, mesh.Indices.ToArray());
    }

    [TestMethod]
    public void Object_may_belong_to_several_scenes()
    {
        var item = NewObject();
        var first = new Scene();
        var second = new Scene();

        first.Add(item);
        second.Add(item);
        first.Remove(item);

        Assert.IsEmpty(first.Objects);
        Assert.HasCount(1, second.Objects);
    }
}
