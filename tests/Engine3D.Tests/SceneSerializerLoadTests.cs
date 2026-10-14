using System.Numerics;
using System.Text.Json.Nodes;
using Engine3D.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class SceneSerializerLoadTests
{
    private string _directory = "";

    [TestInitialize]
    public void CreateDirectory() => _directory = TemporaryDirectory.Create("engine3d-load-");

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(_directory, recursive: true);

    [TestMethod]
    public void Round_trip_restores_meshes_objects_camera_and_light()
    {
        var cube = PrimitiveFactory.CreateCube(2f);
        var scene = new Scene();
        var first = AddObject(scene, cube, new ColorRgba(1f, 0.5f, 0.25f), TextureAt("assets/checker.png"));
        first.Transform.Position = new Vector3(1.5f, -2f, 0.25f);
        first.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.Normalize(new Vector3(1f, 2f, 3f)), 0.7f);
        first.Transform.Scale = new Vector3(1f, 2f, -0.5f);
        // Другой экземпляр TextureData с тем же файлом: после загрузки на путь остается одна текстура.
        AddObject(scene, cube, new ColorRgba(0.1f, 0.2f, 0.3f, 0.4f), TextureAt("assets/checker.png"))
            .Transform.Position = new Vector3(4f, 0f, 0f);
        AddObject(scene, PrimitiveFactory.CreatePlane(3f, 4f), new ColorRgba(0.7f, 0.7f, 0.7f), null)
            .Transform.Position = new Vector3(0f, -1f, 0f);
        scene.Lighting.AmbientColor = new Vector3(0.1f, 0.2f, 0.3f);
        scene.Lighting.DirectionalColor = new Vector3(0.9f, 0.8f, 0.7f);
        scene.Lighting.Direction = new Vector3(-1f, -2f, -0.5f);
        var camera = new Camera
        {
            Position = new Vector3(3f, 2f, 5f),
            Target = new Vector3(0f, 0.5f, 0f),
            Up = new Vector3(0f, 2f, 0f),
            FieldOfViewDegrees = 50f,
            NearPlane = 0.5f,
            FarPlane = 250f,
        };
        string path = InDirectory("scenes/demo.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SceneSerializer.Save(path, scene, camera);
        var loads = new List<string>();

        var loaded = SceneSerializer.Load(path, RecordingLoader(loads));

        // Общий меш — один новый экземпляр на запись; тип и размеры восстановлены фабриками.
        var objects = loaded.Scene.Objects;
        Assert.HasCount(3, objects);
        Assert.AreSame(objects[0].Mesh, objects[1].Mesh);
        Assert.AreNotSame(cube, objects[0].Mesh);
        AssertSamePrimitive(PrimitiveFactory.CreateCube(2f), objects[0].Mesh);
        AssertSamePrimitive(PrimitiveFactory.CreatePlane(3f, 4f), objects[2].Mesh);
        for (int i = 0; i < objects.Count; i++)
        {
            var expected = scene.Objects[i];
            var actual = objects[i];
            Assert.AreEqual(expected.Transform.Position, actual.Transform.Position);
            Assert.AreEqual(expected.Transform.Scale, actual.Transform.Scale);
            // Поворот нормализуется во float при Save и при Load: допуск 1e-6 — несколько ulp единичных компонент.
            AssertClose(expected.Transform.Rotation, actual.Transform.Rotation, 1e-6f);
            Assert.AreEqual(expected.Material.BaseColor, actual.Material.BaseColor);
            Assert.AreNotSame(expected.Material, actual.Material);
        }

        string checker = InDirectory("assets/checker.png");
        CollectionAssert.AreEqual(new[] { checker }, loads);
        Assert.AreSame(objects[0].Material.Texture, objects[1].Material.Texture);
        Assert.AreEqual(checker, objects[0].Material.Texture!.SourcePath);
        Assert.IsNull(objects[2].Material.Texture);

        Assert.AreEqual(camera.Position, loaded.Camera.Position);
        Assert.AreEqual(camera.Target, loaded.Camera.Target);
        Assert.AreEqual(camera.Up, loaded.Camera.Up);
        Assert.AreEqual(camera.FieldOfViewDegrees, loaded.Camera.FieldOfViewDegrees);
        Assert.AreEqual(camera.NearPlane, loaded.Camera.NearPlane);
        Assert.AreEqual(camera.FarPlane, loaded.Camera.FarPlane);
        Assert.AreEqual(scene.Lighting.AmbientColor, loaded.Scene.Lighting.AmbientColor);
        Assert.AreEqual(scene.Lighting.DirectionalColor, loaded.Scene.Lighting.DirectionalColor);
        Assert.AreEqual(scene.Lighting.Direction, loaded.Scene.Lighting.Direction);

        var again = SceneSerializer.Load(path, RecordingLoader(loads)); // каждый Load создает свою сцену
        Assert.AreNotSame(objects[0].Mesh, again.Scene.Objects[0].Mesh);
    }

    [TestMethod]
    public void Saving_a_loaded_scene_reproduces_the_file()
    {
        // Повороты выбраны с точной нормализацией: повторная нормализация во float может сдвинуть последний бит,
        // это покрыто допуском в round-trip выше. Остальные числа читаются и пишутся без потерь.
        var cube = PrimitiveFactory.CreateCube(0.3f);
        var scene = new Scene();
        var first = AddObject(scene, cube, new ColorRgba(0.1f, 1f / 3f, 0.5f, 1f), TextureAt("assets/a.png"));
        first.Transform.Position = new Vector3(0.1f, -1e-7f, 123456.79f);
        first.Transform.Rotation = new Quaternion(0f, 0f, 0f, 2f);
        first.Transform.Scale = new Vector3(1e-7f, 3e7f, -0.3f);
        AddObject(scene, cube, new ColorRgba(1f, 1f, 1f), TextureAt("текстуры/кирпич.png")).Transform.Rotation = new Quaternion(0f, 1f, 0f, 0f);
        AddObject(scene, PrimitiveFactory.CreatePlane(5f, 0.25f), new ColorRgba(0.7f, 0.7f, 0.7f), null);
        scene.Lighting.Direction = new Vector3(-0.4f, -1f, -0.6f);
        var camera = new Camera { Position = new Vector3(3f, 2f, 5f), Target = Vector3.Zero, FieldOfViewDegrees = 45.5f, NearPlane = 0.05f, FarPlane = 500f };
        string firstPath = InDirectory("first.json");
        string secondPath = InDirectory("second.json");

        SceneSerializer.Save(firstPath, scene, camera);
        var loaded = SceneSerializer.Load(firstPath, texturePath => new TextureData(1, 1, new byte[4], texturePath));
        SceneSerializer.Save(secondPath, loaded.Scene, loaded.Camera);

        CollectionAssert.AreEqual(File.ReadAllBytes(firstPath), File.ReadAllBytes(secondPath));
    }

    [TestMethod]
    public void Example_from_the_contract_loads()
    {
        // Документ из docs/api.md, раздел 9: общий куб m0 у двух объектов, текстура только у первого.
        string path = Write("scenes/example.json", """
            {
              "version": 1,
              "meshes": [{ "id": "m0", "kind": "cube", "size": 1.0 }],
              "objects": [
                {
                  "meshId": "m0",
                  "transform": { "position": [0,0,0], "rotation": [0,0,0,1], "scale": [1,1,1] },
                  "material": { "baseColor": [1,1,1,1], "texturePath": "../Assets/checker.png" }
                },
                {
                  "meshId": "m0",
                  "transform": { "position": [2,0,0], "rotation": [0,0,0,1], "scale": [1,1,1] },
                  "material": { "baseColor": [1,0.5,0.5,1], "texturePath": null }
                }
              ],
              "camera": { "position": [3,2,5], "target": [0,0,0], "up": [0,1,0], "fovDegrees": 60, "nearPlane": 0.1, "farPlane": 100 },
              "lighting": { "ambientColor": [0.2,0.2,0.2], "directionalColor": [1,1,1], "direction": [-1,-1,-1] }
            }
            """);
        var loads = new List<string>();

        var loaded = SceneSerializer.Load(path, RecordingLoader(loads));

        var objects = loaded.Scene.Objects;
        Assert.HasCount(2, objects);
        Assert.AreSame(objects[0].Mesh, objects[1].Mesh);
        AssertSamePrimitive(PrimitiveFactory.CreateCube(1f), objects[0].Mesh);
        Assert.AreEqual(InDirectory("Assets/checker.png"), objects[0].Material.Texture!.SourcePath);
        Assert.IsNull(objects[1].Material.Texture);
        Assert.AreEqual(new ColorRgba(1f, 0.5f, 0.5f, 1f), objects[1].Material.BaseColor);
        Assert.AreEqual(new Vector3(2f, 0f, 0f), objects[1].Transform.Position);
        Assert.AreEqual(new Vector3(3f, 2f, 5f), loaded.Camera.Position);
        Assert.AreEqual(Vector3.Zero, loaded.Camera.Target);
        Assert.AreEqual(Vector3.UnitY, loaded.Camera.Up);
        Assert.AreEqual(60f, loaded.Camera.FieldOfViewDegrees);
        Assert.AreEqual(0.1f, loaded.Camera.NearPlane);
        Assert.AreEqual(100f, loaded.Camera.FarPlane);
        Assert.AreEqual(new Vector3(0.2f), loaded.Scene.Lighting.AmbientColor);
        Assert.AreEqual(Vector3.One, loaded.Scene.Lighting.DirectionalColor);
        Assert.AreEqual(new Vector3(-1f, -1f, -1f), loaded.Scene.Lighting.Direction);
        CollectionAssert.AreEqual(new[] { InDirectory("Assets/checker.png") }, loads);
    }

    [TestMethod]
    public void Thousand_objects_on_one_mesh_share_one_mesh_after_loading()
    {
        var cube = PrimitiveFactory.CreateCube();
        var scene = new Scene();
        for (int i = 0; i < 1000; i++)
            AddObject(scene, cube, new ColorRgba(1f, 1f, 1f), null).Transform.Position = new Vector3(i % 10, i / 10 % 10, i / 100);
        string path = InDirectory("thousand.json");
        SceneSerializer.Save(path, scene, new Camera { Position = new Vector3(5f, 5f, 30f), Target = new Vector3(5f, 5f, 5f) });

        var objects = SceneSerializer.Load(path).Scene.Objects;

        Assert.HasCount(1000, objects);
        Assert.AreEqual(1, objects.Select(item => (object)item.Mesh).Distinct(ReferenceEqualityComparer.Instance).Count());
        for (int i = 0; i < objects.Count; i++)
            Assert.AreEqual(scene.Objects[i].Transform.Position, objects[i].Transform.Position);
    }

    [TestMethod]
    [DataRow(0f, 0f, 0f, 2f)]
    [DataRow(0f, 1e-30f, 0f, 0f)]
    [DataRow(1f, 2f, 3f, 4f)]
    [DataRow(3e38f, 0f, 0f, -3e38f)]
    public void Rotation_is_normalized_on_load(float x, float y, float z, float w)
    {
        var root = JsonNode.Parse(SceneDocumentValidationTests.ValidDocument)!;
        root["objects"]![0]!["transform"]!["rotation"] = new JsonArray(x, y, z, w);
        string path = Write("scene.json", root.ToJsonString());

        var rotation = SceneSerializer.Load(path).Scene.Objects[0].Transform.Rotation;

        // Эталон — деление на длину в double; допуск 1e-6 — несколько ulp float у компонент порядка единицы.
        double length = Math.Sqrt((double)x * x + (double)y * y + (double)z * z + (double)w * w);
        Assert.AreEqual(x / length, rotation.X, 1e-6);
        Assert.AreEqual(y / length, rotation.Y, 1e-6);
        Assert.AreEqual(z / length, rotation.Z, 1e-6);
        Assert.AreEqual(w / length, rotation.W, 1e-6);
    }

    [TestMethod]
    public void Textures_are_resolved_from_the_json_directory_and_loaded_once_per_full_path()
    {
        // Один файл двумя записями пути, тот же файл в другом регистре (на Linux — другой файл), объект без текстуры.
        Write("scenes/scene.json", DocumentWithTextures(
            "../assets/a.png", "../assets/../assets/a.png", "../assets/A.png", "../assets/a.png", null));
        Directory.CreateDirectory(InDirectory("elsewhere"));
        var loads = new List<string>();
        string previous = Environment.CurrentDirectory;
        SceneSnapshot loaded;
        try
        {
            Environment.CurrentDirectory = InDirectory("elsewhere");
            loaded = SceneSerializer.Load("../scenes/scene.json", RecordingLoader(loads));
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }

        CollectionAssert.AreEqual(new[] { InDirectory("assets/a.png"), InDirectory("assets/A.png") }, loads);
        var textures = loaded.Scene.Objects.Select(item => item.Material.Texture).ToArray();
        Assert.AreSame(textures[0], textures[1]);
        Assert.AreSame(textures[0], textures[3]);
        Assert.AreNotSame(textures[0], textures[2]);
        Assert.IsNull(textures[4]);
    }

    [TestMethod]
    public void Missing_loader_is_reported_only_after_the_whole_document_is_checked()
    {
        string textured = Write("textured.json", DocumentWithTextures("a.png"));
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => SceneSerializer.Load(textured));
        Assert.Contains(textured, exception.Message);

        // Неверная камера в документе с текстурой: сначала сообщается ошибка документа, а не отсутствие загрузчика.
        var root = JsonNode.Parse(DocumentWithTextures("a.png"))!;
        root["camera"]!["target"] = new JsonArray(3, 2, 5);
        string invalid = Write("invalid.json", root.ToJsonString());
        Assert.ThrowsExactly<InvalidDataException>(() => SceneSerializer.Load(invalid));
    }

    [TestMethod]
    public void Loader_error_is_passed_through_unchanged()
    {
        string path = Write("scene.json", DocumentWithTextures("missing.png", "other.png"));
        var failure = new FileNotFoundException("Texture file is missing.", InDirectory("missing.png"));
        var loads = new List<string>();

        var thrown = Assert.ThrowsExactly<FileNotFoundException>(
            () => SceneSerializer.Load(path, texturePath => { loads.Add(texturePath); throw failure; }));

        Assert.AreSame(failure, thrown);
        CollectionAssert.AreEqual(new[] { InDirectory("missing.png") }, loads); // после ошибки загрузка останавливается
    }

    [TestMethod]
    [DataRow("null texture")]
    [DataRow("texture without source path")]
    [DataRow("texture with another source path")]
    public void Loader_must_return_the_requested_texture(string kind)
    {
        string path = Write("scene.json", DocumentWithTextures("a.png"));
        Func<string, TextureData> loader = kind switch
        {
            "null texture" => _ => null!,
            "texture without source path" => _ => new TextureData(1, 1, new byte[4]),
            "texture with another source path" => _ => new TextureData(1, 1, new byte[4], InDirectory("b.png")),
            _ => throw new ArgumentException($"Unknown case '{kind}'.", nameof(kind)),
        };

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => SceneSerializer.Load(path, loader));

        Assert.Contains(InDirectory("a.png"), exception.Message);
    }

    [TestMethod]
    public void File_errors_are_reported_by_the_file_system()
    {
        Assert.ThrowsExactly<FileNotFoundException>(() => SceneSerializer.Load(InDirectory("missing.json")));
        Assert.ThrowsExactly<DirectoryNotFoundException>(() => SceneSerializer.Load(InDirectory("missing/scene.json")));
    }

    [TestMethod]
    public void Rejects_invalid_arguments()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => SceneSerializer.Load(null!));
        Assert.ThrowsExactly<ArgumentException>(() => SceneSerializer.Load(""));
        Assert.ThrowsExactly<ArgumentException>(() => SceneSerializer.Load("   "));
        Assert.ThrowsExactly<ArgumentException>(() => SceneSerializer.Load(InDirectory("scenes") + Path.DirectorySeparatorChar));
        Assert.ThrowsExactly<ArgumentException>(() => SceneSerializer.Load(InDirectory("bad\0name.json")));
    }

    private string InDirectory(string relativePath) => Path.Combine(_directory, relativePath);

    private TextureData TextureAt(string relativePath) => new(1, 1, new byte[4], InDirectory(relativePath));

    private string Write(string relativePath, string json)
    {
        string path = InDirectory(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        return path;
    }

    // Загрузчик без окна: запоминает полные пути и возвращает TextureData с тем же SourcePath, как Engine.LoadTexture.
    private static Func<string, TextureData> RecordingLoader(List<string> loads) => texturePath =>
    {
        loads.Add(texturePath);
        return new TextureData(1, 1, new byte[4], texturePath);
    };

    // Корректный документ, у которого объект m0 повторен с указанными путями текстур.
    private static string DocumentWithTextures(params string?[] texturePaths)
    {
        var root = JsonNode.Parse(SceneDocumentValidationTests.ValidDocument)!;
        var template = root["objects"]![0]!;
        var objects = new JsonArray();
        foreach (string? texturePath in texturePaths)
        {
            var item = template.DeepClone();
            item["material"]!["texturePath"] = texturePath;
            objects.Add(item);
        }

        root["objects"] = objects;
        return root.ToJsonString();
    }

    private static SceneObject AddObject(Scene scene, MeshData mesh, ColorRgba color, TextureData? texture)
    {
        var item = new SceneObject(mesh, new MaterialData { BaseColor = color, Texture = texture });
        scene.Add(item);
        return item;
    }

    private static void AssertSamePrimitive(MeshData expected, MeshData actual)
    {
        Assert.AreEqual(expected.Primitive, actual.Primitive);
        CollectionAssert.AreEqual(expected.Vertices.ToArray(), actual.Vertices.ToArray());
        CollectionAssert.AreEqual(expected.Indices.ToArray(), actual.Indices.ToArray());
    }

    private static void AssertClose(Quaternion expected, Quaternion actual, float tolerance)
    {
        Assert.AreEqual(expected.X, actual.X, tolerance);
        Assert.AreEqual(expected.Y, actual.Y, tolerance);
        Assert.AreEqual(expected.Z, actual.Z, tolerance);
        Assert.AreEqual(expected.W, actual.W, tolerance);
    }
}
