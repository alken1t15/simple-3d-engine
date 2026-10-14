using System.Numerics;

namespace Engine3D.Core;

/// <summary>
/// Документ JSON → новая сцена для <see cref="SceneSerializer.Load"/>. Сначала документ проверяется целиком:
/// структура и значения (теми же Validate(), что и перед кадром renderer). Затем, если сцене нужны текстуры,
/// проверяется наличие загрузчика, и только после этого создаются меши и объекты и вызывается загрузчик.
/// </summary>
internal static class SceneRestorer
{
    // Проверенные данные объекта: поворот уже единичный, путь текстуры — полный или null.
    private readonly record struct ObjectData(
        string MeshId, Vector3 Position, Quaternion Rotation, Vector3 Scale, ColorRgba BaseColor, string? TexturePath);

    /// <param name="jsonPath">Полный путь файла JSON: для сообщений об ошибках и для путей текстур от его каталога.</param>
    /// <exception cref="InvalidDataException">
    /// Неверны версия, kind или размеры меша, id и ссылки на меши, числа, transform, цвет, путь текстуры, камера или свет.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Сцене нужны текстуры, а загрузчик не передан; загрузчик вернул null или текстуру с другим SourcePath.
    /// </exception>
    public static SceneSnapshot Restore(SceneDocument document, string jsonPath, Func<string, TextureData>? textureLoader)
    {
        if (document.Version != SceneDocument.SupportedVersion)
        {
            throw InvalidDocument(jsonPath, "$.version",
                $"version {document.Version} is not supported; only version {SceneDocument.SupportedVersion} can be loaded.");
        }

        var meshes = ReadMeshes(document.Meshes, jsonPath);
        string directory = Path.GetDirectoryName(jsonPath)!;
        var objects = new ObjectData[document.Objects.Length];
        for (int i = 0; i < objects.Length; i++)
            objects[i] = ReadObject(document.Objects[i], $"$.objects[{i}]", meshes, directory, jsonPath);
        var camera = ReadCamera(document.Camera, jsonPath);
        var lighting = ReadLighting(document.Lighting, jsonPath);

        // Загрузчик нужен только сцене с текстурами; проверяется после всего документа и до создания сцены.
        if (textureLoader is null && objects.Any(item => item.TexturePath is not null))
        {
            throw new InvalidOperationException(
                $"Scene file '{jsonPath}' uses textures: pass a texture loader such as Engine.LoadTexture.");
        }

        var scene = new Scene();
        scene.Lighting.AmbientColor = lighting.AmbientColor;
        scene.Lighting.DirectionalColor = lighting.DirectionalColor;
        scene.Lighting.Direction = lighting.Direction;

        // Один новый MeshData на запись meshes — общий для всех ее объектов; материал на объект; текстура —
        // один вызов загрузчика на полный путь.
        var createdMeshes = new Dictionary<string, MeshData>(StringComparer.Ordinal);
        var textures = new Dictionary<string, TextureData>(StringComparer.Ordinal);
        foreach (var data in objects)
        {
            if (!createdMeshes.TryGetValue(data.MeshId, out var mesh))
            {
                mesh = CreateMesh(meshes[data.MeshId]);
                createdMeshes.Add(data.MeshId, mesh);
            }

            TextureData? texture = null;
            if (data.TexturePath is { } texturePath && !textures.TryGetValue(texturePath, out texture))
            {
                texture = LoadTexture(textureLoader!, texturePath);
                textures.Add(texturePath, texture);
            }

            var item = new SceneObject(mesh, new MaterialData { BaseColor = data.BaseColor, Texture = texture });
            item.Transform.Position = data.Position;
            item.Transform.Rotation = data.Rotation;
            item.Transform.Scale = data.Scale;
            scene.Add(item);
        }

        return new SceneSnapshot(scene, camera);
    }

    /// <summary>Ошибка документа: путь файла, место в документе ($.objects[0].transform) и причина.</summary>
    public static InvalidDataException InvalidDocument(string jsonPath, string location, string reason, Exception? inner = null) =>
        new($"Scene file '{jsonPath}' is invalid at {location}: {reason}", inner);

    // Все записи проверяются, даже если ни один объект на них не ссылается.
    private static Dictionary<string, MeshDocument> ReadMeshes(MeshDocument[] meshes, string jsonPath)
    {
        var byId = new Dictionary<string, MeshDocument>(StringComparer.Ordinal);
        for (int i = 0; i < meshes.Length; i++)
        {
            string location = $"$.meshes[{i}]";
            var mesh = meshes[i];
            if (mesh is null) // null-элементы массивов System.Text.Json не отклоняет
                throw InvalidDocument(jsonPath, location, "expected a mesh description, got null.");
            if (mesh.Id.Length == 0)
                throw InvalidDocument(jsonPath, location + ".id", "the mesh id must not be empty.");
            if (!byId.TryAdd(mesh.Id, mesh))
                throw InvalidDocument(jsonPath, location + ".id", $"mesh id '{mesh.Id}' is already used by another mesh.");

            switch (mesh.Kind)
            {
                case MeshDocument.Cube:
                    RequireDimension(mesh.Size, location + ".size", jsonPath);
                    break;
                case MeshDocument.Plane:
                    RequireDimension(mesh.Width, location + ".width", jsonPath);
                    RequireDimension(mesh.Depth, location + ".depth", jsonPath);
                    break;
                default:
                    throw InvalidDocument(jsonPath, location + ".kind",
                        $"mesh kind '{mesh.Kind}' is not supported; expected '{MeshDocument.Cube}' or '{MeshDocument.Plane}'.");
            }
        }

        return byId;
    }

    // Размер обязателен для своего kind, конечен и положителен — как требуют фабрики примитивов.
    private static void RequireDimension(float? value, string location, string jsonPath)
    {
        if (value is not { } dimension)
            throw InvalidDocument(jsonPath, location, "the value is required for this mesh kind.");
        if (!(float.IsFinite(dimension) && dimension > 0f))
            throw InvalidDocument(jsonPath, location, $"the value must be positive and finite, got {dimension}.");
    }

    private static ObjectData ReadObject(
        ObjectDocument item, string location, Dictionary<string, MeshDocument> meshes, string directory, string jsonPath)
    {
        if (item is null)
            throw InvalidDocument(jsonPath, location, "expected a scene object, got null.");
        if (!meshes.ContainsKey(item.MeshId))
            throw InvalidDocument(jsonPath, location + ".meshId", $"mesh '{item.MeshId}' is not described in meshes.");

        var transform = new Transform
        {
            Position = ReadVector3(item.Transform.Position, location + ".transform.position", jsonPath),
            Rotation = ReadQuaternion(item.Transform.Rotation, location + ".transform.rotation", jsonPath),
            Scale = ReadVector3(item.Transform.Scale, location + ".transform.scale", jsonPath),
        };
        Check(transform.Validate, location + ".transform", jsonPath);
        Finite.TryNormalize(transform.Rotation, out var rotation); // Validate прошел: поворот конечный и ненулевой

        var material = new MaterialData { BaseColor = ReadColor(item.Material.BaseColor, location + ".material.baseColor", jsonPath) };
        Check(material.Validate, location + ".material.baseColor", jsonPath);

        string? texturePath = ReadTexturePath(item.Material.TexturePath, location + ".material.texturePath", directory, jsonPath);
        return new ObjectData(item.MeshId, transform.Position, rotation, transform.Scale, material.BaseColor, texturePath);
    }

    // Путь в документе — относительно каталога JSON (абсолютный Save не пишет и Load не принимает); результат — полный путь.
    private static string? ReadTexturePath(string? texturePath, string location, string directory, string jsonPath)
    {
        if (texturePath is null)
            return null;
        if (string.IsNullOrWhiteSpace(texturePath))
            throw InvalidDocument(jsonPath, location, "the texture path must not be empty; use null for a material without a texture.");
        if (Path.IsPathRooted(texturePath))
            throw InvalidDocument(jsonPath, location, $"the texture path '{texturePath}' must be relative to the scene file directory.");

        try
        {
            return Path.GetFullPath(texturePath, directory);
        }
        catch (ArgumentException exception)
        {
            throw InvalidDocument(jsonPath, location, $"the texture path '{texturePath}' is not a valid path.", exception);
        }
    }

    private static Camera ReadCamera(CameraDocument document, string jsonPath)
    {
        var camera = new Camera
        {
            Position = ReadVector3(document.Position, "$.camera.position", jsonPath),
            Target = ReadVector3(document.Target, "$.camera.target", jsonPath),
            Up = ReadVector3(document.Up, "$.camera.up", jsonPath),
            FieldOfViewDegrees = document.FovDegrees,
            NearPlane = document.NearPlane,
            FarPlane = document.FarPlane,
        };
        Check(camera.Validate, "$.camera", jsonPath);
        return camera;
    }

    private static SceneLighting ReadLighting(LightingDocument document, string jsonPath)
    {
        var lighting = new SceneLighting
        {
            AmbientColor = ReadVector3(document.AmbientColor, "$.lighting.ambientColor", jsonPath),
            DirectionalColor = ReadVector3(document.DirectionalColor, "$.lighting.directionalColor", jsonPath),
            Direction = ReadVector3(document.Direction, "$.lighting.direction", jsonPath),
        };
        Check(lighting.Validate, "$.lighting", jsonPath);
        return lighting;
    }

    // Значение проверяет Validate() Core; его InvalidOperationException становится ошибкой документа.
    private static void Check(Action validate, string location, string jsonPath)
    {
        try
        {
            validate();
        }
        catch (InvalidOperationException exception)
        {
            throw InvalidDocument(jsonPath, location, exception.Message, exception);
        }
    }

    private static Vector3 ReadVector3(float[] values, string location, string jsonPath)
    {
        RequireLength(values, 3, location, jsonPath);
        return new Vector3(values[0], values[1], values[2]);
    }

    private static Quaternion ReadQuaternion(float[] values, string location, string jsonPath)
    {
        RequireLength(values, 4, location, jsonPath);
        return new Quaternion(values[0], values[1], values[2], values[3]);
    }

    private static ColorRgba ReadColor(float[] values, string location, string jsonPath)
    {
        RequireLength(values, 4, location, jsonPath);
        return new ColorRgba(values[0], values[1], values[2], values[3]);
    }

    private static void RequireLength(float[] values, int length, string location, string jsonPath)
    {
        if (values.Length != length)
            throw InvalidDocument(jsonPath, location, $"expected {length} numbers, got {values.Length}.");
    }

    private static MeshData CreateMesh(MeshDocument mesh) => mesh.Kind == MeshDocument.Cube
        ? PrimitiveFactory.CreateCube(mesh.Size!.Value)
        : PrimitiveFactory.CreatePlane(mesh.Width!.Value, mesh.Depth!.Value);

    // Ошибка самого загрузчика (например, FileNotFoundException от Engine.LoadTexture) выходит без изменений.
    private static TextureData LoadTexture(Func<string, TextureData> loader, string fullPath)
    {
        var texture = loader(fullPath)
            ?? throw new InvalidOperationException($"Texture loader returned null for '{fullPath}'.");
        if (!string.Equals(texture.SourcePath, fullPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Texture loader returned a texture with SourcePath '{texture.SourcePath}' for '{fullPath}': "
                + "SourcePath must be the requested full path so that the scene can be saved again.");
        }

        return texture;
    }
}
