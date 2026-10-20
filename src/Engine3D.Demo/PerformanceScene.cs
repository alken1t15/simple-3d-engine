using System.Numerics;
using Engine3D.Core;

namespace Engine3D.Demo;

/// <summary>Фиксированная нагрузка B: без JSON, анимации, culling и сторонних ассетов.</summary>
internal static class PerformanceScene
{
    internal const int ObjectCount = 1000;
    internal const int TriangleCount = ObjectCount * 12;

    internal static SceneSnapshot Create(TextureData texture)
    {
        var mesh = PrimitiveFactory.CreateCube();
        var material = new MaterialData { Texture = texture, BaseColor = new ColorRgba(1f, 0.8f, 0.6f) };
        var scene = new Scene();
        scene.Lighting.AmbientColor = new Vector3(0.25f);
        scene.Lighting.DirectionalColor = new Vector3(0.75f);
        scene.Lighting.Direction = new Vector3(-1f, -1.5f, -2f);
        for (int i = 0; i < ObjectCount; i++)
        {
            var item = new SceneObject(mesh, material);
            item.Transform.Position = new Vector3(i % 10 * 2f, i / 10 % 10 * 2f, i / 100 * 2f);
            scene.Add(item);
        }

        var camera = new Camera
        {
            Position = new Vector3(35f, 30f, 40f),
            Target = new Vector3(9f, 9f, 9f),
            FieldOfViewDegrees = 60f,
            NearPlane = 0.1f,
            FarPlane = 200f,
        };
        camera.Validate();
        scene.Lighting.Validate();
        foreach (var item in scene.Objects)
            item.Transform.Validate();
        return new SceneSnapshot(scene, camera);
    }
}
