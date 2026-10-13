using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Engine3D.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class SceneDocumentValidationTests
{
    /// <summary>Корректный документ без текстур: куб m0 у объекта и неиспользуемая плоскость m1.</summary>
    internal const string ValidDocument = """
        {
          "version": 1,
          "meshes": [
            { "id": "m0", "kind": "cube", "size": 1 },
            { "id": "m1", "kind": "plane", "width": 2, "depth": 3 }
          ],
          "objects": [
            {
              "meshId": "m0",
              "transform": { "position": [0, 0, 0], "rotation": [0, 0, 0, 1], "scale": [1, 1, 1] },
              "material": { "baseColor": [1, 1, 1, 1], "texturePath": null }
            }
          ],
          "camera": { "position": [3, 2, 5], "target": [0, 0, 0], "up": [0, 1, 0], "fovDegrees": 60, "nearPlane": 0.1, "farPlane": 100 },
          "lighting": { "ambientColor": [0.2, 0.2, 0.2], "directionalColor": [1, 1, 1], "direction": [-1, -1, -1] }
        }
        """;

    private string _directory = "";

    [TestInitialize]
    public void CreateDirectory() => _directory = Directory.CreateTempSubdirectory("engine3d-document-").FullName;

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(_directory, recursive: true);

    [TestMethod]
    [DataRow("as is")]
    [DataRow("UTF-8 BOM")]
    [DataRow("unknown properties")]
    [DataRow("no objects")]
    [DataRow("raw UTF-8 text")]
    [DataRow("duplicate unknown properties")]
    public void Valid_documents_load_without_a_texture_loader(string kind)
    {
        var root = JsonNode.Parse(ValidDocument)!;
        string? text = null;
        switch (kind)
        {
            case "as is":
            case "UTF-8 BOM":
                break;
            case "raw UTF-8 text": // многобайтовые символы как есть, без \u-экранирования
                text = Replace(ValidDocument, "\"version\": 1,", "\"version\": 1, \"comment\": \"сцена — кирпич\",");
                break;
            case "duplicate unknown properties": // неизвестные свойства пропускаются целиком, их повторы тоже
                text = Replace(ValidDocument, "\"version\": 1,", "\"version\": 1, \"comment\": 1, \"comment\": { \"x\": 1, \"x\": 2 },");
                break;
            case "unknown properties": // лишние свойства игнорируются, обязательные проверяются как обычно
                root["comment"] = "saved by hand";
                root["meshes"]![0]!["color"] = "red";
                root["objects"]![0]!["name"] = "box";
                root["objects"]![0]!["material"]!["shininess"] = new JsonArray(1, 2);
                break;
            case "no objects":
                root["objects"] = new JsonArray();
                break;
            default:
                Assert.Fail($"Unknown case '{kind}'.");
                break;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(text ?? root.ToJsonString());
        string path = Path.Combine(_directory, "scene.json");
        File.WriteAllBytes(path, kind == "UTF-8 BOM" ? [0xEF, 0xBB, 0xBF, .. bytes] : bytes);

        var loaded = SceneSerializer.Load(path);

        Assert.HasCount(kind == "no objects" ? 0 : 1, loaded.Scene.Objects);
        Assert.AreEqual(new Vector3(3f, 2f, 5f), loaded.Camera.Position);
    }

    [TestMethod]
    [DataRow("not JSON", "$", true)]
    [DataRow("empty file", "$", true)]
    [DataRow("root is an array", "$", true)]
    [DataRow("root is null", "$", false)]
    [DataRow("text after the document", "$", true)]
    [DataRow("comment", "$", true)]
    [DataRow("trailing comma", "$.lighting", true)]
    [DataRow("duplicate key", "$.version", true)]
    [DataRow("missing version", "$", true)]
    [DataRow("version 2", "$.version", false)]
    [DataRow("version 0", "$.version", false)]
    [DataRow("version as text", "$.version", true)]
    [DataRow("fractional version", "$.version", true)]
    [DataRow("missing meshes", "$", true)]
    [DataRow("null meshes", "$.meshes", true)]
    [DataRow("null mesh", "$.meshes[0]", false)]
    [DataRow("unknown kind", "$.meshes[0].kind", false)]
    [DataRow("kind in another case", "$.meshes[0].kind", false)]
    [DataRow("duplicate mesh id", "$.meshes[1].id", false)]
    [DataRow("empty mesh id", "$.meshes[0].id", false)]
    [DataRow("cube without size", "$.meshes[0].size", false)]
    [DataRow("cube with null size", "$.meshes[0].size", false)]
    [DataRow("zero cube size", "$.meshes[0].size", false)]
    [DataRow("negative cube size", "$.meshes[0].size", false)]
    [DataRow("cube size overflow", "$.meshes[0].size", false)]
    [DataRow("unused plane without depth", "$.meshes[1].depth", false)]
    [DataRow("missing objects", "$", true)]
    [DataRow("null object", "$.objects[0]", false)]
    [DataRow("meshId without mesh", "$.objects[0].meshId", false)]
    [DataRow("missing transform", "$.objects[0]", true)]
    [DataRow("position of two numbers", "$.objects[0].transform.position", false)]
    [DataRow("rotation of three numbers", "$.objects[0].transform.rotation", false)]
    [DataRow("position as text", "$.objects[0].transform.position", true)]
    [DataRow("null in scale", "$.objects[0].transform.scale", true)]
    [DataRow("zero rotation", "$.objects[0].transform", false)]
    [DataRow("zero scale", "$.objects[0].transform", false)]
    [DataRow("scale overflow", "$.objects[0].transform", false)]
    [DataRow("missing material", "$.objects[0]", true)]
    [DataRow("missing texturePath", "$.objects[0].material", true)]
    [DataRow("base color above one", "$.objects[0].material.baseColor", false)]
    [DataRow("base color of three numbers", "$.objects[0].material.baseColor", false)]
    [DataRow("empty texturePath", "$.objects[0].material.texturePath", false)]
    [DataRow("absolute texturePath", "$.objects[0].material.texturePath", false)]
    [DataRow("texturePath with null character", "$.objects[0].material.texturePath", false)]
    [DataRow("missing camera", "$", true)]
    [DataRow("camera position equals target", "$.camera", false)]
    [DataRow("camera near beyond far", "$.camera", false)]
    [DataRow("camera fov 180", "$.camera", false)]
    [DataRow("camera up parallel to view", "$.camera", false)]
    [DataRow("camera up of four numbers", "$.camera.up", false)]
    [DataRow("missing lighting", "$", true)]
    [DataRow("zero light direction", "$.lighting", false)]
    [DataRow("ambient above one", "$.lighting", false)]
    public void Invalid_documents_are_rejected_with_the_file_and_location(string kind, string location, bool jsonError)
    {
        string path = Path.Combine(_directory, "scene.json");
        File.WriteAllText(path, InvalidDocument(kind));

        // Загрузчик не должен вызываться: документ проверяется целиком до загрузки текстур.
        var exception = Assert.ThrowsExactly<InvalidDataException>(
            () => SceneSerializer.Load(path, _ => throw new AssertFailedException("The texture loader must not be called.")));

        Assert.StartsWith($"Scene file '{path}' is invalid at {location}: ", exception.Message);
        if (jsonError)
            Assert.IsInstanceOfType<JsonException>(exception.InnerException); // разбор JSON: исходная причина сохранена
        else
            Assert.IsNotInstanceOfType<JsonException>(exception.InnerException);
    }

    [TestMethod]
    [DataRow(0xFF)]
    [DataRow(0x80)]
    [DataRow(0xD0)]
    public void Damaged_utf8_is_rejected_before_the_texture_loader(int damagedByte)
    {
        // Регрессия ревью #25: File.ReadAllText заменял поврежденные байты символом U+FFFD, и Load принимал другой
        // документ, вызывая загрузчик с испорченным путем. Байт '#' в пути заменяется на 0xFF (в UTF-8 не встречается),
        // 0x80 (продолжение без начала) или 0xD0 (начало двухбайтового символа, за которым идет 'b').
        byte[] bytes = Encoding.UTF8.GetBytes(Replace(ValidDocument, "\"texturePath\": null", "\"texturePath\": \"a#b.png\""));
        bytes[Array.IndexOf(bytes, (byte)'#')] = (byte)damagedByte;
        string path = Path.Combine(_directory, "scene.json");
        File.WriteAllBytes(path, bytes);
        int loaderCalls = 0;

        var exception = Assert.ThrowsExactly<InvalidDataException>(() => SceneSerializer.Load(path, texturePath =>
        {
            loaderCalls++;
            return new TextureData(1, 1, new byte[4], texturePath);
        }));

        Assert.StartsWith($"Scene file '{path}' is invalid at $: the file is not valid UTF-8", exception.Message);
        Assert.IsInstanceOfType<DecoderFallbackException>(exception.InnerException);
        Assert.AreEqual(0, loaderCalls);
    }

    private static string InvalidDocument(string kind)
    {
        switch (kind)
        {
            case "not JSON": return "{ this is not json";
            case "empty file": return "";
            case "root is an array": return "[]";
            case "root is null": return "null";
            case "text after the document": return ValidDocument + " x";
            case "comment": return Replace(ValidDocument, "\"version\": 1,", "\"version\": 1, // saved by hand");
            case "trailing comma": return Replace(ValidDocument, "\"direction\": [-1, -1, -1] }", "\"direction\": [-1, -1, -1], }");
            case "duplicate key": return Replace(ValidDocument, "\"version\": 1,", "\"version\": 1, \"version\": 1,");
        }

        var root = JsonNode.Parse(ValidDocument)!.AsObject();
        var mesh = root["meshes"]![0]!.AsObject();
        var plane = root["meshes"]![1]!.AsObject();
        var item = root["objects"]![0]!.AsObject();
        var transform = item["transform"]!.AsObject();
        var material = item["material"]!.AsObject();
        var camera = root["camera"]!.AsObject();
        var lighting = root["lighting"]!.AsObject();
        switch (kind)
        {
            case "missing version": root.Remove("version"); break;
            case "version 2": root["version"] = 2; break;
            case "version 0": root["version"] = 0; break;
            case "version as text": root["version"] = "1"; break;
            case "fractional version": root["version"] = 1.5; break;
            case "missing meshes": root.Remove("meshes"); break;
            case "null meshes": root["meshes"] = null; break;
            case "null mesh": root["meshes"]![0] = null; break;
            case "unknown kind": mesh["kind"] = "sphere"; break;
            case "kind in another case": mesh["kind"] = "Cube"; break;
            case "duplicate mesh id": plane["id"] = "m0"; break;
            case "empty mesh id": mesh["id"] = ""; break;
            case "cube without size": mesh.Remove("size"); break;
            case "cube with null size": mesh["size"] = null; break;
            case "zero cube size": mesh["size"] = 0; break;
            case "negative cube size": mesh["size"] = -1; break;
            case "cube size overflow": mesh["size"] = 1e39; break; // за пределом float: читается как ∞
            case "unused plane without depth": plane.Remove("depth"); break;
            case "missing objects": root.Remove("objects"); break;
            case "null object": root["objects"]![0] = null; break;
            case "meshId without mesh": item["meshId"] = "m9"; break;
            case "missing transform": item.Remove("transform"); break;
            case "position of two numbers": transform["position"] = new JsonArray(0, 0); break;
            case "rotation of three numbers": transform["rotation"] = new JsonArray(0, 0, 1); break;
            case "position as text": transform["position"] = "0, 0, 0"; break;
            case "null in scale": transform["scale"] = new JsonArray(1, null, 1); break;
            case "zero rotation": transform["rotation"] = new JsonArray(0, 0, 0, 0); break;
            case "zero scale": transform["scale"] = new JsonArray(1, 0, 1); break;
            case "scale overflow": transform["scale"] = new JsonArray(1e39, 1, 1); break;
            case "missing material": item.Remove("material"); break;
            case "missing texturePath": material.Remove("texturePath"); break;
            case "base color above one": material["baseColor"] = new JsonArray(1.5, 1, 1, 1); break;
            case "base color of three numbers": material["baseColor"] = new JsonArray(1, 1, 1); break;
            case "empty texturePath": material["texturePath"] = ""; break;
            case "absolute texturePath": material["texturePath"] = "/textures/checker.png"; break;
            case "texturePath with null character": material["texturePath"] = "a\0b.png"; break;
            case "missing camera": root.Remove("camera"); break;
            case "camera position equals target": camera["target"] = new JsonArray(3, 2, 5); break;
            case "camera near beyond far": camera["nearPlane"] = 200; break;
            case "camera fov 180": camera["fovDegrees"] = 180; break;
            case "camera up parallel to view": camera["up"] = new JsonArray(3, 2, 5); break;
            case "camera up of four numbers": camera["up"] = new JsonArray(0, 1, 0, 0); break;
            case "missing lighting": root.Remove("lighting"); break;
            case "zero light direction": lighting["direction"] = new JsonArray(0, 0, 0); break;
            case "ambient above one": lighting["ambientColor"] = new JsonArray(1.5, 0.2, 0.2); break;
            default: throw new ArgumentException($"Unknown case '{kind}'.", nameof(kind));
        }

        return root.ToJsonString();
    }

    private static string Replace(string text, string oldValue, string newValue)
    {
        Assert.Contains(oldValue, text); // защита от тихой поломки примера при правке ValidDocument
        return text.Replace(oldValue, newValue);
    }
}
