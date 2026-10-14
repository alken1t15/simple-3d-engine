using System.Numerics;
using System.Text;
using System.Text.Json;
using Engine3D.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class SceneSerializerSaveTests
{
    private string _directory = "";

    [TestInitialize]
    public void CreateDirectory() => _directory = TemporaryDirectory.Create("engine3d-save-");

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(_directory, recursive: true);

    [TestMethod]
    public void Writes_version_1_with_primitives_objects_camera_and_light()
    {
        var cube = PrimitiveFactory.CreateCube(2f);
        var scene = new Scene();
        var first = AddObject(scene, cube, new ColorRgba(1f, 0.5f, 0.25f));
        first.Transform.Position = new Vector3(1.5f, -2f, 0.25f);
        first.Transform.Scale = new Vector3(1f, 2f, -0.5f);
        AddObject(scene, cube, new ColorRgba(0.1f, 0.2f, 0.3f, 0.4f)).Transform.Position = new Vector3(4f, 0f, 0f);
        AddObject(scene, PrimitiveFactory.CreatePlane(3f, 4f), new ColorRgba(1f, 1f, 1f));
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

        var root = SaveAndRead("scene.json", scene, camera);

        AssertProperties(root, "version", "meshes", "objects", "camera", "lighting");
        Assert.AreEqual(1, root.GetProperty("version").GetInt32());

        var meshes = root.GetProperty("meshes");
        Assert.AreEqual(2, meshes.GetArrayLength());
        AssertProperties(meshes[0], "id", "kind", "size");
        Assert.AreEqual("m0", meshes[0].GetProperty("id").GetString());
        Assert.AreEqual("cube", meshes[0].GetProperty("kind").GetString());
        Assert.AreEqual(2f, meshes[0].GetProperty("size").GetSingle());
        AssertProperties(meshes[1], "id", "kind", "width", "depth");
        Assert.AreEqual("m1", meshes[1].GetProperty("id").GetString());
        Assert.AreEqual("plane", meshes[1].GetProperty("kind").GetString());
        Assert.AreEqual(3f, meshes[1].GetProperty("width").GetSingle());
        Assert.AreEqual(4f, meshes[1].GetProperty("depth").GetSingle());

        // Объекты — в порядке Scene.Objects; общий меш куба записан один раз и указан у двух объектов.
        var objects = root.GetProperty("objects");
        CollectionAssert.AreEqual(new[] { "m0", "m0", "m1" }, MeshIds(objects));
        var transform = objects[0].GetProperty("transform");
        AssertProperties(transform, "position", "rotation", "scale");
        CollectionAssert.AreEqual(new[] { 1.5f, -2f, 0.25f }, Numbers(transform.GetProperty("position")));
        CollectionAssert.AreEqual(new[] { 0f, 0f, 0f, 1f }, Numbers(transform.GetProperty("rotation")));
        CollectionAssert.AreEqual(new[] { 1f, 2f, -0.5f }, Numbers(transform.GetProperty("scale")));
        var material = objects[0].GetProperty("material");
        AssertProperties(material, "baseColor", "texturePath");
        CollectionAssert.AreEqual(new[] { 1f, 0.5f, 0.25f, 1f }, Numbers(material.GetProperty("baseColor")));
        Assert.AreEqual(JsonValueKind.Null, material.GetProperty("texturePath").ValueKind);
        CollectionAssert.AreEqual(new[] { 0.1f, 0.2f, 0.3f, 0.4f }, Numbers(objects[1].GetProperty("material").GetProperty("baseColor")));

        // Камера и направление света записываются как заданы, без нормализации.
        var savedCamera = root.GetProperty("camera");
        AssertProperties(savedCamera, "position", "target", "up", "fovDegrees", "nearPlane", "farPlane");
        CollectionAssert.AreEqual(new[] { 3f, 2f, 5f }, Numbers(savedCamera.GetProperty("position")));
        CollectionAssert.AreEqual(new[] { 0f, 0.5f, 0f }, Numbers(savedCamera.GetProperty("target")));
        CollectionAssert.AreEqual(new[] { 0f, 2f, 0f }, Numbers(savedCamera.GetProperty("up")));
        Assert.AreEqual(50f, savedCamera.GetProperty("fovDegrees").GetSingle());
        Assert.AreEqual(0.5f, savedCamera.GetProperty("nearPlane").GetSingle());
        Assert.AreEqual(250f, savedCamera.GetProperty("farPlane").GetSingle());

        var lighting = root.GetProperty("lighting");
        AssertProperties(lighting, "ambientColor", "directionalColor", "direction");
        CollectionAssert.AreEqual(new[] { 0.1f, 0.2f, 0.3f }, Numbers(lighting.GetProperty("ambientColor")));
        CollectionAssert.AreEqual(new[] { 0.9f, 0.8f, 0.7f }, Numbers(lighting.GetProperty("directionalColor")));
        CollectionAssert.AreEqual(new[] { -1f, -2f, -0.5f }, Numbers(lighting.GetProperty("direction")));
    }

    [TestMethod]
    public void Meshes_are_grouped_by_reference_not_by_geometry()
    {
        var shared = PrimitiveFactory.CreateCube();
        var scene = new Scene();
        AddObject(scene, shared, new ColorRgba(1f, 1f, 1f));
        AddObject(scene, PrimitiveFactory.CreateCube(), new ColorRgba(1f, 1f, 1f)); // та же геометрия, другой экземпляр
        AddObject(scene, shared, new ColorRgba(1f, 1f, 1f));

        var root = SaveAndRead("scene.json", scene, ValidCamera());

        Assert.AreEqual(2, root.GetProperty("meshes").GetArrayLength());
        CollectionAssert.AreEqual(new[] { "m0", "m1", "m0" }, MeshIds(root.GetProperty("objects")));
    }

    [TestMethod]
    [DataRow(0f, 0f, 0f, 2f)]
    [DataRow(1f, 2f, 3f, 4f)]
    [DataRow(0f, 1e-30f, 0f, 0f)]
    [DataRow(3e38f, 0f, 0f, 3e38f)]
    public void Rotation_is_saved_as_a_unit_quaternion(float x, float y, float z, float w)
    {
        var rotation = new Quaternion(x, y, z, w);
        var scene = new Scene();
        AddObject(scene, PrimitiveFactory.CreateCube(), new ColorRgba(1f, 1f, 1f)).Transform.Rotation = rotation;

        var root = SaveAndRead("scene.json", scene, ValidCamera());

        // Эталон — деление на длину в double, где нет ни переполнения, ни потери малых компонент. Допуск 1e-6 —
        // несколько ulp float у компонент порядка единицы после нормализации во float.
        var saved = Numbers(root.GetProperty("objects")[0].GetProperty("transform").GetProperty("rotation"));
        double length = Math.Sqrt((double)x * x + (double)y * y + (double)z * z + (double)w * w);
        double[] expected = [x / length, y / length, z / length, w / length];
        Assert.HasCount(4, saved);
        for (int i = 0; i < 4; i++)
            Assert.AreEqual(expected[i], saved[i], 1e-6);
        Assert.AreEqual(rotation, scene.Objects[0].Transform.Rotation); // сама сцена при сохранении не меняется
    }

    [TestMethod]
    [DataRow("scenes/demo.json", "assets/checker.png", "../assets/checker.png")]
    [DataRow("demo.json", "checker.png", "checker.png")]
    [DataRow("demo.json", "textures/stone/wall.png", "textures/stone/wall.png")]
    [DataRow("a/b/c/demo.json", "x.png", "../../../x.png")]
    [DataRow("сцены/демо.json", "текстуры/кирпич+1.png", "../текстуры/кирпич+1.png")]
    public void Texture_path_is_relative_to_the_json_directory(string jsonPath, string texturePath, string expected)
    {
        var scene = new Scene();
        AddObject(scene, PrimitiveFactory.CreateCube(), new ColorRgba(1f, 1f, 1f)).Material.Texture = TextureAt(texturePath);

        var root = SaveAndRead(jsonPath, scene, ValidCamera());

        Assert.AreEqual(expected, root.GetProperty("objects")[0].GetProperty("material").GetProperty("texturePath").GetString());
        Assert.Contains($"\"texturePath\": \"{expected}\"", File.ReadAllText(InDirectory(jsonPath))); // без \u-экранирования
    }

    [TestMethod]
    public void Texture_path_does_not_depend_on_the_current_directory()
    {
        // Относительный путь самого JSON разрешается от текущего каталога, а путь текстуры — только от каталога JSON.
        Directory.CreateDirectory(InDirectory("scenes"));
        Directory.CreateDirectory(InDirectory("elsewhere"));
        var scene = new Scene();
        AddObject(scene, PrimitiveFactory.CreateCube(), new ColorRgba(1f, 1f, 1f)).Material.Texture = TextureAt("assets/checker.png");
        string previous = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = InDirectory("elsewhere");
            SceneSerializer.Save("../scenes/demo.json", scene, ValidCamera());
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }

        var root = Read(InDirectory("scenes/demo.json"));
        Assert.AreEqual("../assets/checker.png", root.GetProperty("objects")[0].GetProperty("material").GetProperty("texturePath").GetString());
    }

    [TestMethod]
    public void File_is_indented_utf8_with_inline_number_arrays_and_repeatable()
    {
        var scene = new Scene();
        AddObject(scene, PrimitiveFactory.CreateCube(), new ColorRgba(1f, 1f, 1f)).Transform.Position = new Vector3(1.5f, -2f, 0.25f);
        string path = InDirectory("scene.json");

        SceneSerializer.Save(path, scene, ValidCamera());
        byte[] first = File.ReadAllBytes(path);
        SceneSerializer.Save(path, scene, ValidCamera());

        CollectionAssert.AreEqual(first, File.ReadAllBytes(path)); // та же сцена — тот же файл
        string text = Encoding.UTF8.GetString(first);
        Assert.StartsWith("{\n  \"version\": 1,\n", text); // без BOM, отступ 2 пробела, LF
        Assert.Contains("\"position\": [1.5, -2, 0.25]", text);
        Assert.DoesNotContain("\r", text);
        Assert.EndsWith("}\n", text);
    }

    [TestMethod]
    public void Numbers_are_written_exactly()
    {
        // Кратчайшая запись float читается обратно в тот же float, поэтому сравнение точное, без допуска.
        var scene = new Scene();
        var item = AddObject(scene, PrimitiveFactory.CreateCube(0.1f), new ColorRgba(0.1f, 1f / 3f, 0f, 0.999f));
        item.Transform.Position = new Vector3(0.1f, 1e-7f, 123456.79f);
        item.Transform.Scale = new Vector3(1e-7f, 3e7f, -0.3f);

        var root = SaveAndRead("scene.json", scene, ValidCamera());

        Assert.AreEqual(0.1f, root.GetProperty("meshes")[0].GetProperty("size").GetSingle());
        var saved = root.GetProperty("objects")[0];
        CollectionAssert.AreEqual(new[] { 0.1f, 1e-7f, 123456.79f }, Numbers(saved.GetProperty("transform").GetProperty("position")));
        CollectionAssert.AreEqual(new[] { 1e-7f, 3e7f, -0.3f }, Numbers(saved.GetProperty("transform").GetProperty("scale")));
        CollectionAssert.AreEqual(new[] { 0.1f, 1f / 3f, 0f, 0.999f }, Numbers(saved.GetProperty("material").GetProperty("baseColor")));
    }

    [TestMethod]
    public void Empty_scene_keeps_camera_and_light()
    {
        var root = SaveAndRead("empty.json", new Scene(), ValidCamera());

        Assert.AreEqual(0, root.GetProperty("meshes").GetArrayLength());
        Assert.AreEqual(0, root.GetProperty("objects").GetArrayLength());
        CollectionAssert.AreEqual(new[] { 3f, 2f, 5f }, Numbers(root.GetProperty("camera").GetProperty("position")));
        CollectionAssert.AreEqual(new[] { -1f, -1f, -1f }, Numbers(root.GetProperty("lighting").GetProperty("direction")));
    }

    [TestMethod]
    [DataRow("custom mesh")]
    [DataRow("texture without source file")]
    public void Unsupported_mesh_or_texture_is_rejected_and_the_file_is_unchanged(string kind)
    {
        string path = InDirectory("scene.json");
        File.WriteAllText(path, "previous content");
        var scene = new Scene();
        AddObject(scene, PrimitiveFactory.CreateCube(), new ColorRgba(1f, 1f, 1f));
        var mesh = kind == "custom mesh" ? CustomTriangle() : PrimitiveFactory.CreatePlane();
        AddObject(scene, mesh, new ColorRgba(1f, 1f, 1f)).Material.Texture =
            kind == "texture without source file" ? new TextureData(1, 1, new byte[4]) : null;

        var exception = Assert.ThrowsExactly<NotSupportedException>(() => SceneSerializer.Save(path, scene, ValidCamera()));

        Assert.StartsWith("Scene object 1 ", exception.Message);
        Assert.AreEqual("previous content", File.ReadAllText(path));
    }

    [TestMethod]
    [DataRow("zero scale")]
    [DataRow("NaN position")]
    [DataRow("zero rotation")]
    [DataRow("base color above one")]
    [DataRow("camera position equals target")]
    [DataRow("camera near beyond far")]
    [DataRow("zero light direction")]
    [DataRow("ambient above one")]
    public void Invalid_scene_is_rejected_and_the_file_is_unchanged(string kind)
    {
        string path = InDirectory("scene.json");
        File.WriteAllText(path, "previous content");
        var scene = new Scene();
        AddObject(scene, PrimitiveFactory.CreateCube(), new ColorRgba(1f, 1f, 1f));
        var item = AddObject(scene, PrimitiveFactory.CreateCube(), new ColorRgba(1f, 1f, 1f));
        var camera = ValidCamera();
        bool objectIsInvalid = true;
        switch (kind)
        {
            case "zero scale": item.Transform.Scale = new Vector3(1f, 0f, 1f); break;
            case "NaN position": item.Transform.Position = new Vector3(float.NaN, 0f, 0f); break;
            case "zero rotation": item.Transform.Rotation = default; break;
            case "base color above one": item.Material.BaseColor = new ColorRgba(1.5f, 0f, 0f); break;
            case "camera position equals target": camera.Target = camera.Position; objectIsInvalid = false; break;
            case "camera near beyond far": camera.NearPlane = 10f; camera.FarPlane = 5f; objectIsInvalid = false; break;
            case "zero light direction": scene.Lighting.Direction = Vector3.Zero; objectIsInvalid = false; break;
            case "ambient above one": scene.Lighting.AmbientColor = new Vector3(1.5f); objectIsInvalid = false; break;
            default: Assert.Fail($"Unknown case '{kind}'."); break;
        }

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => SceneSerializer.Save(path, scene, camera));

        if (objectIsInvalid)
            Assert.StartsWith("Scene object 1 is invalid: ", exception.Message);
        Assert.AreEqual("previous content", File.ReadAllText(path));
    }

    [TestMethod]
    public void Rejects_invalid_arguments()
    {
        var scene = new Scene();
        var camera = ValidCamera();
        string path = InDirectory("scene.json");

        Assert.ThrowsExactly<ArgumentNullException>(() => SceneSerializer.Save(null!, scene, camera));
        Assert.ThrowsExactly<ArgumentException>(() => SceneSerializer.Save("", scene, camera));
        Assert.ThrowsExactly<ArgumentException>(() => SceneSerializer.Save("   ", scene, camera));
        Assert.ThrowsExactly<ArgumentException>(() => SceneSerializer.Save(InDirectory("scenes") + Path.DirectorySeparatorChar, scene, camera));
        Assert.ThrowsExactly<ArgumentException>(() => SceneSerializer.Save(InDirectory("bad\0name.json"), scene, camera));
        Assert.ThrowsExactly<ArgumentNullException>(() => SceneSerializer.Save(path, null!, camera));
        Assert.ThrowsExactly<ArgumentNullException>(() => SceneSerializer.Save(path, scene, null!));
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    public void Missing_directory_is_reported_by_the_file_system()
    {
        // Каталог создает приложение; Save сообщает стандартную ошибку ввода-вывода.
        Assert.ThrowsExactly<DirectoryNotFoundException>(
            () => SceneSerializer.Save(InDirectory("missing/scene.json"), new Scene(), ValidCamera()));
    }

    private string InDirectory(string relativePath) => Path.Combine(_directory, relativePath);

    private TextureData TextureAt(string relativePath) => new(1, 1, new byte[4], InDirectory(relativePath));

    private JsonElement SaveAndRead(string relativePath, Scene scene, Camera camera)
    {
        string path = InDirectory(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SceneSerializer.Save(path, scene, camera);
        return Read(path);
    }

    private static JsonElement Read(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    private static Camera ValidCamera() => new() { Position = new Vector3(3f, 2f, 5f), Target = Vector3.Zero };

    private static SceneObject AddObject(Scene scene, MeshData mesh, ColorRgba color)
    {
        var item = new SceneObject(mesh, new MaterialData { BaseColor = color });
        scene.Add(item);
        return item;
    }

    private static MeshData CustomTriangle() => new(
        [
            new Vertex(Vector3.Zero, Vector2.Zero, Vector3.UnitZ),
            new Vertex(Vector3.UnitX, Vector2.UnitX, Vector3.UnitZ),
            new Vertex(Vector3.UnitY, Vector2.UnitY, Vector3.UnitZ),
        ],
        [0, 1, 2]);

    private static float[] Numbers(JsonElement array) => [.. array.EnumerateArray().Select(number => number.GetSingle())];

    private static string?[] MeshIds(JsonElement objects) =>
        [.. objects.EnumerateArray().Select(item => item.GetProperty("meshId").GetString())];

    private static void AssertProperties(JsonElement element, params string[] names) =>
        CollectionAssert.AreEquivalent(names, element.EnumerateObject().Select(property => property.Name).ToArray());
}
