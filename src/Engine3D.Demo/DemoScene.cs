using System.Numerics;
using Engine3D.Core;

namespace Engine3D.Demo;

/// <summary>Сцена и движение принадлежат приложению; используются только публичные CPU-типы движка.</summary>
internal sealed class DemoScene(SceneSnapshot snapshot, SceneObject rotatingCube)
{
    private readonly Vector3 _initialCameraPosition = snapshot.Camera.Position;
    private readonly Vector3 _initialLightDirection = snapshot.Scene.Lighting.Direction;
    private double _elapsedSeconds;

    internal SceneSnapshot Snapshot { get; } = snapshot;

    internal static DemoScene Create(TextureData texture)
    {
        var scene = new Scene();
        scene.Lighting.AmbientColor = new Vector3(0.25f);
        scene.Lighting.DirectionalColor = new Vector3(0.75f);
        scene.Lighting.Direction = new Vector3(-1f, -1.5f, -2f);

        // Два объекта разделяют один mesh; материалы и transform у них разные.
        var cubeMesh = PrimitiveFactory.CreateCube();
        var cube = new SceneObject(cubeMesh, new MaterialData { Texture = texture });
        cube.Transform.Position = new Vector3(-0.8f, 0.75f, 0.55f);
        cube.Transform.Scale = new Vector3(1.25f, 1.5f, 1.25f);
        scene.Add(cube);

        var secondCube = new SceneObject(cubeMesh, new MaterialData { BaseColor = new ColorRgba(0.25f, 0.75f, 0.9f) });
        secondCube.Transform.Position = new Vector3(0.65f, 0.65f, -0.6f);
        secondCube.Transform.Scale = new Vector3(1.1f, 1.3f, 0.75f);
        secondCube.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.45f);
        scene.Add(secondCube);

        var floor = new SceneObject(PrimitiveFactory.CreatePlane(7f, 6f),
            new MaterialData { BaseColor = new ColorRgba(0.65f, 0.7f, 0.75f) });
        floor.Transform.Position = new Vector3(0f, -0.015f, 0f);
        // Пол добавлен последним: он не должен перекрывать кубы независимо от порядка рисования.
        scene.Add(floor);

        var camera = new Camera
        {
            Position = new Vector3(4f, 3.2f, 6.5f),
            Target = new Vector3(0f, 0.6f, 0f),
            FieldOfViewDegrees = 55f,
            NearPlane = 0.1f,
            FarPlane = 50f,
        };
        return new DemoScene(new SceneSnapshot(scene, camera), cube);
    }

    /// <summary>Вызывается на потоке onUpdate. Периоды ограничивают углы при длительном показе.</summary>
    internal void Update(DemoScenario scenario, float deltaSeconds)
    {
        _elapsedSeconds += deltaSeconds;
        if (scenario == DemoScenario.Mvp)
        {
            rotatingCube.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, Angle(6d));
            var orbit = Quaternion.CreateFromAxisAngle(Vector3.UnitY, Angle(24d));
            Snapshot.Camera.Position = Snapshot.Camera.Target + Vector3.Transform(_initialCameraPosition - Snapshot.Camera.Target, orbit);
        }
        else if (scenario == DemoScenario.Lighting)
        {
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, Angle(12d));
            Snapshot.Scene.Lighting.Direction = Vector3.Transform(_initialLightDirection, rotation);
        }
    }

    private float Angle(double period) => (float)(_elapsedSeconds % period / period * Math.Tau);
}
