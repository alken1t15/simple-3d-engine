using System.Numerics;
using Engine3D.Core;
using Engine3D.Demo;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class DemoTests
{
    [TestMethod]
    public void Default_launch_uses_packaged_texture_and_continuous_mvp()
    {
        var options = DemoOptions.Parse([]);
        Assert.AreEqual(DemoScenario.Mvp, options.Scenario);
        Assert.AreEqual(Path.Combine(AppContext.BaseDirectory, "Assets", "control.png"), options.TexturePath);
        Assert.IsTrue(File.Exists(options.TexturePath));
        Assert.IsFalse(options.Static);
        Assert.IsFalse(options.Help);
        Assert.IsNull(options.FrameLimit);
    }

    [TestMethod]
    public void Options_accept_paths_with_spaces_and_positive_frame_limit()
    {
        var options = DemoOptions.Parse(["--scenario", "roundtrip", "--texture", "my assets/control.png",
            "--scene", "my scenes/scene.json", "--frames", "1"]);
        Assert.AreEqual(DemoScenario.RoundTrip, options.Scenario);
        Assert.AreEqual("my assets/control.png", options.TexturePath);
        Assert.AreEqual("my scenes/scene.json", options.ScenePath);
        Assert.AreEqual(1L, options.FrameLimit);

        var load = DemoOptions.Parse(["--load", "my scenes/scene.json", "--frames", "60"]);
        Assert.AreEqual("my scenes/scene.json", load.LoadPath);
        Assert.AreEqual(60L, load.FrameLimit);
        var save = DemoOptions.Parse(["--static", "--save", "my scenes/scene.json"]);
        Assert.IsTrue(save.Static);
        Assert.AreEqual("my scenes/scene.json", save.SavePath);
    }

    [TestMethod]
    [DataRow("--frames", "0")]
    [DataRow("--frames", "-1")]
    [DataRow("--frames", "1.5")]
    [DataRow("--frames", "9223372036854775808")]
    [DataRow("--scenario", "unknown")]
    [DataRow("--texture", "assets/")]
    [DataRow("--load", "")]
    [DataRow("--save", "scene.json")]
    [DataRow("--scene", "scene.json")]
    public void Invalid_values_fail_before_window_creation(string name, string value)
    {
        var exception = Assert.ThrowsExactly<ArgumentException>(() => DemoOptions.Parse([name, value]));
        Assert.IsFalse(string.IsNullOrWhiteSpace(exception.Message));
    }

    [TestMethod]
    public void Unknown_missing_duplicate_and_incompatible_options_are_rejected()
    {
        string[][] invalid =
        [
            ["--unknown"], ["--frames"], ["--texture", "--frames", "2"],
            ["--frames", "1", "--frames", "2"], ["--static", "--static"],
            ["--load", "scene.json", "--texture", "control.png"],
            ["--load", "scene.json", "--scenario", "mvp"],
            ["--scenario", "lighting", "--static"],
            ["--scenario", "roundtrip", "--static", "--save", "scene.json"],
        ];
        foreach (var args in invalid)
            Assert.ThrowsExactly<ArgumentException>(() => DemoOptions.Parse(args), string.Join(' ', args));
        Assert.IsTrue(DemoOptions.Parse(["--help"]).Help);
    }

    [TestMethod]
    public void Scene_has_two_shared_cubes_distinct_transforms_texture_and_floor_below_them()
    {
        var texture = Texture();
        var snapshot = DemoScene.Create(texture).Snapshot;
        var objects = snapshot.Scene.Objects;
        Assert.HasCount(3, objects);
        Assert.AreSame(objects[0].Mesh, objects[1].Mesh);
        Assert.AreEqual(12, objects[0].Mesh.TriangleCount);
        Assert.AreEqual(2, objects[2].Mesh.TriangleCount);
        Assert.AreSame(texture, objects[0].Material.Texture);
        Assert.IsNull(objects[1].Material.Texture);
        Assert.AreNotEqual(objects[0].Material.BaseColor, objects[1].Material.BaseColor);
        Assert.AreNotEqual(objects[0].Transform.Position, objects[1].Transform.Position);
        Assert.AreNotEqual(objects[1].Transform.Scale.X, objects[1].Transform.Scale.Y);

        float floorY = objects[2].Transform.Position.Y;
        foreach (var cube in objects.Take(2))
        {
            foreach (var vertex in cube.Mesh.Vertices)
                Assert.IsGreaterThan(floorY, Vector3.Transform(vertex.Position, cube.Transform.GetModelMatrix()).Y);
        }
        snapshot.Camera.Validate();
        snapshot.Scene.Lighting.Validate();
    }

    [TestMethod]
    public void Mvp_rotates_cube_and_views_scene_from_opposite_side_without_changing_light()
    {
        var demo = DemoScene.Create(Texture());
        var camera = demo.Snapshot.Camera;
        var initial = camera.Position - camera.Target;
        var direction = demo.Snapshot.Scene.Lighting.Direction;
        var fixedCubeMatrix = demo.Snapshot.Scene.Objects[1].Transform.GetModelMatrix();

        demo.Update(DemoScenario.Mvp, 1.5f); // Четверть шестисекундного оборота куба.
        var rotatedX = Vector3.Transform(Vector3.UnitX, demo.Snapshot.Scene.Objects[0].Transform.Rotation);
        // Геометрический эталон: исходная и повернутая оси перпендикулярны. Допуск — несколько ulp float.
        Assert.AreEqual(0f, Vector3.Dot(Vector3.UnitX, rotatedX), 2e-6f);
        Assert.AreNotEqual(camera.Position - camera.Target, initial);
        demo.Update(DemoScenario.Mvp, 10.5f); // Половина 24-секундного облёта.
        var opposite = camera.Position - camera.Target;
        // 2e-5 мировых единиц покрывают округление quaternion-преобразования при координатах порядка 10.
        Assert.AreEqual(-initial.X, opposite.X, 2e-5f);
        Assert.AreEqual(initial.Y, opposite.Y, 2e-5f);
        Assert.AreEqual(-initial.Z, opposite.Z, 2e-5f);
        Assert.AreEqual(initial.Length(), opposite.Length(), 2e-5f);
        Assert.AreEqual(direction, demo.Snapshot.Scene.Lighting.Direction);
        Assert.AreEqual(fixedCubeMatrix, demo.Snapshot.Scene.Objects[1].Transform.GetModelMatrix());
    }

    [TestMethod]
    public void Lighting_changes_illuminated_faces_while_geometry_and_camera_stay_fixed()
    {
        var demo = DemoScene.Create(Texture());
        var scene = demo.Snapshot.Scene;
        var initialDirection = scene.Lighting.GetNormalizedDirection();
        var view = demo.Snapshot.Camera.GetViewMatrix();
        var models = scene.Objects.Select(item => item.Transform.GetModelMatrix()).ToArray();
        demo.Update(DemoScenario.Lighting, 6f); // Свет с противоположной стороны.
        var changedDirection = scene.Lighting.GetNormalizedDirection();
        Assert.IsTrue(initialDirection.Z < 0f && changedDirection.Z > 0f);
        Assert.IsTrue(initialDirection.Y < 0f && changedDirection.Y < 0f); // Пол продолжает получать свет сверху.
        Assert.AreEqual(view, demo.Snapshot.Camera.GetViewMatrix());
        CollectionAssert.AreEqual(models, scene.Objects.Select(item => item.Transform.GetModelMatrix()).ToArray());
        scene.Lighting.Validate();
    }

    [TestMethod]
    public void Motion_depends_on_elapsed_time_and_remains_valid_over_multiple_orbits()
    {
        var whole = DemoScene.Create(Texture());
        var split = DemoScene.Create(Texture());
        whole.Update(DemoScenario.Mvp, 2f);
        for (int i = 0; i < 16; i++)
            split.Update(DemoScenario.Mvp, 0.125f);
        Assert.AreEqual(whole.Snapshot.Camera.Position, split.Snapshot.Camera.Position);
        Assert.AreEqual(whole.Snapshot.Scene.Objects[0].Transform.Rotation, split.Snapshot.Scene.Objects[0].Transform.Rotation);

        for (int i = 0; i < 241; i++)
        {
            split.Update(DemoScenario.Mvp, 0.5f);
            split.Snapshot.Camera.Validate();
            foreach (var item in split.Snapshot.Scene.Objects)
                item.Transform.Validate();
        }
    }

    [TestMethod]
    public void Demo_roundtrip_restores_new_objects_shared_mesh_materials_camera_and_light()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"engine3d-demo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var original = DemoScene.Create(Texture(Path.Combine(directory, "control.png"))).Snapshot;
            string path = Path.Combine(directory, "scene.json");
            SceneSerializer.Save(path, original.Scene, original.Camera);
            int loads = 0;
            var loaded = SceneSerializer.Load(path, texturePath => { loads++; return Texture(texturePath); });
            Assert.AreNotSame(original.Scene, loaded.Scene);
            Assert.AreNotSame(original.Camera, loaded.Camera);
            Assert.AreSame(loaded.Scene.Objects[0].Mesh, loaded.Scene.Objects[1].Mesh);
            Assert.AreEqual(1, loads);
            for (int i = 0; i < original.Scene.Objects.Count; i++)
            {
                var before = original.Scene.Objects[i];
                var after = loaded.Scene.Objects[i];
                Assert.AreNotSame(before, after);
                CollectionAssert.AreEqual(before.Mesh.Vertices.ToArray(), after.Mesh.Vertices.ToArray());
                Assert.AreEqual(before.Transform.Position, after.Transform.Position);
                Assert.AreEqual(before.Transform.Scale, after.Transform.Scale);
                Assert.AreEqual(1f, MathF.Abs(Quaternion.Dot(before.Transform.Rotation, after.Transform.Rotation)), 2e-6f);
                Assert.AreEqual(before.Material.BaseColor, after.Material.BaseColor);
                Assert.AreEqual(before.Material.Texture?.SourcePath, after.Material.Texture?.SourcePath);
            }
            Assert.AreEqual(original.Camera.GetViewMatrix(), loaded.Camera.GetViewMatrix());
            Assert.AreEqual(original.Camera.GetProjectionMatrix(1.5f), loaded.Camera.GetProjectionMatrix(1.5f));
            Assert.AreEqual(original.Scene.Lighting.AmbientColor, loaded.Scene.Lighting.AmbientColor);
            Assert.AreEqual(original.Scene.Lighting.DirectionalColor, loaded.Scene.Lighting.DirectionalColor);
            Assert.AreEqual(original.Scene.Lighting.Direction, loaded.Scene.Lighting.Direction);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static TextureData Texture(string? path = null) => new(1, 1, [255, 255, 255, 255], path);
}
