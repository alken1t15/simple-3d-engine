using System.Numerics;

namespace Engine3D.Core;

/// <summary>
/// Сцена → документ JSON для <see cref="SceneSerializer.Save"/>. Данные проверяются теми же Validate(), что и перед
/// кадром renderer; файл при этом еще не открыт, поэтому неверная сцена его не меняет.
/// </summary>
internal static class SceneDocumentBuilder
{
    /// <param name="jsonDirectory">Полный путь каталога файла JSON: от него считаются пути текстур.</param>
    /// <exception cref="InvalidOperationException">Камера, свет, transform или материал объекта неверны.</exception>
    /// <exception cref="NotSupportedException">Меш создан не фабрикой примитивов или у текстуры нет исходного файла.</exception>
    public static SceneDocument Build(Scene scene, Camera camera, string jsonDirectory)
    {
        camera.Validate();
        scene.Lighting.Validate();

        // Один экземпляр MeshData — одна запись, даже если геометрия двух мешей совпадает: id назначаются по ссылке
        // в порядке первого использования (m0, m1, ...).
        var meshIds = new Dictionary<MeshData, string>(ReferenceEqualityComparer.Instance);
        var meshes = new List<MeshDocument>();
        var objects = new ObjectDocument[scene.Objects.Count];
        for (int i = 0; i < objects.Length; i++)
        {
            var item = scene.Objects[i];
            try
            {
                item.Transform.Validate();
                item.Material.Validate();
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException($"Scene object {i} is invalid: {exception.Message}", exception);
            }

            if (!meshIds.TryGetValue(item.Mesh, out string? meshId))
            {
                meshId = $"m{meshes.Count}";
                meshes.Add(CreateMesh(item.Mesh, meshId, i));
                meshIds.Add(item.Mesh, meshId);
            }

            objects[i] = new ObjectDocument
            {
                MeshId = meshId,
                Transform = CreateTransform(item.Transform),
                Material = CreateMaterial(item.Material, jsonDirectory, i),
            };
        }

        return new SceneDocument
        {
            Version = SceneDocument.SupportedVersion,
            Meshes = [.. meshes],
            Objects = objects,
            Camera = new CameraDocument
            {
                Position = ToArray(camera.Position),
                Target = ToArray(camera.Target),
                Up = ToArray(camera.Up),
                FovDegrees = camera.FieldOfViewDegrees,
                NearPlane = camera.NearPlane,
                FarPlane = camera.FarPlane,
            },
            Lighting = new LightingDocument
            {
                AmbientColor = ToArray(scene.Lighting.AmbientColor),
                DirectionalColor = ToArray(scene.Lighting.DirectionalColor),
                Direction = ToArray(scene.Lighting.Direction),
            },
        };
    }

    // Тип и размеры берутся из описания, которое фабрика сохранила в MeshData; по геометрии тип не угадывается.
    private static MeshDocument CreateMesh(MeshData mesh, string id, int objectIndex) => mesh.Primitive switch
    {
        CubeDescription cube => new MeshDocument { Id = id, Kind = MeshDocument.Cube, Size = cube.Size },
        PlaneDescription plane => new MeshDocument { Id = id, Kind = MeshDocument.Plane, Width = plane.Width, Depth = plane.Depth },
        _ => throw new NotSupportedException(
            $"Scene object {objectIndex} uses a mesh that was not created by PrimitiveFactory: only cube and plane meshes can be saved."),
    };

    private static TransformDocument CreateTransform(Transform transform)
    {
        // Validate прошел, поэтому поворот конечный и ненулевой; в файл записывается единичный кватернион.
        Finite.TryNormalize(transform.Rotation, out var rotation);
        return new TransformDocument
        {
            Position = ToArray(transform.Position),
            Rotation = [rotation.X, rotation.Y, rotation.Z, rotation.W],
            Scale = ToArray(transform.Scale),
        };
    }

    private static MaterialDocument CreateMaterial(MaterialData material, string jsonDirectory, int objectIndex)
    {
        string? texturePath = null;
        if (material.Texture is { } texture)
        {
            if (texture.SourcePath is null)
            {
                throw new NotSupportedException(
                    $"Scene object {objectIndex} has a texture without a source file (TextureData.SourcePath is null): only textures loaded from files can be saved.");
            }

            // SourcePath уже полный; в файл попадает путь относительно каталога JSON, абсолютный путь не записывается.
            texturePath = Path.GetRelativePath(jsonDirectory, texture.SourcePath);
        }

        var color = material.BaseColor;
        return new MaterialDocument { BaseColor = [color.R, color.G, color.B, color.A], TexturePath = texturePath };
    }

    private static float[] ToArray(Vector3 value) => [value.X, value.Y, value.Z];
}
