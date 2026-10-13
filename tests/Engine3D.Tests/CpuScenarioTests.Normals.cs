using System.Numerics;
using Engine3D.Core;
using Engine3D.OpenGL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

/// <summary>
/// CPU-сценарий нормалей примитивов после transform: мировые нормали из <see cref="FramePreparation.NormalTransforms"/>
/// проверяются геометрическими свойствами реального cube/plane (перпендикулярность рёбрам, направление наружу),
/// а не повторением формулы inverse-transpose.
/// </summary>
public sealed partial class CpuScenarioTests
{
    /// <summary>
    /// Допуск перпендикулярности: |cos угла| между единичной мировой нормалью и единичным мировым ребром.
    /// Ошибка округления float32 при построении model, делении на масштаб и нормализации — порядка 1e-7 на операцию,
    /// при неравномерном масштабе она усиливается примерно в отношение max|scale| / min|scale| (в наборах ниже до ~10).
    /// 1e-4 (≈ 0.006°) даёт запас на это, а перепутанные строки/столбцы поворота дают cos порядка 0.004–0.99.
    /// Локальные нормали cube/plane направлены по осям, поэтому для них умножение нормали на model и inverse-transpose
    /// совпадают по направлению; эту ошибку ловит FramePreparationTests.Normal_transform_gives_the_inverse_transpose_direction.
    /// </summary>
    internal const float NormalPerpendicularTolerance = 1e-4f;

    [TestMethod]
    [DataRow(2f, 0.5f, 3f, 0.2f, 1f, 0.1f, 0.7f, 5f, -2f, 1f, DisplayName = "неравномерный scale, наклонная ось")]
    [DataRow(0.3f, 4f, 1f, 1f, 1f, 1f, 2.1f, -10f, 3f, 7f, DisplayName = "сжатие по X, вытягивание по Y, диагональная ось")]
    [DataRow(5f, 1f, 0.5f, 0f, 0f, 1f, 0.785398f, 0f, 0f, -20f, DisplayName = "поворот 45° вокруг Z, сильная анизотропия")]
    [DataRow(1.5f, 1.5f, 1.5f, -0.4f, 0.3f, 0.9f, -1.3f, 100f, 50f, -75f, DisplayName = "равномерный scale, далёкая позиция")]
    public void Cube_world_normals_stay_perpendicular_and_outward_after_transform(
        float sx, float sy, float sz, float ax, float ay, float az, float angle, float px, float py, float pz)
    {
        var transform = CreateTransform(new(sx, sy, sz), new(ax, ay, az), angle, new(px, py, pz));

        var triangles = PrepareWorldTriangles(PrimitiveFactory.CreateCube(), transform, out var worldCenter);

        Assert.HasCount(12, triangles);
        for (int t = 0; t < triangles.Count; t++)
        {
            var triangle = triangles[t];
            foreach (var normal in triangle.Normals)
            {
                AssertPerpendicularToEdges(normal, triangle, $"triangle {t}");
                AssertOutward(normal, triangle.Center, worldCenter, $"triangle {t}");
            }
        }
    }

    [TestMethod]
    [DataRow(2f, 1f, 0.5f, 0.2f, 1f, 0.1f, 0.7f, 5f, -2f, 1f, DisplayName = "неравномерный scale, наклонная ось")]
    [DataRow(4f, 0.25f, 1f, 1f, 0f, 0f, 1.2f, -3f, 8f, 0f, DisplayName = "наклон вокруг X, сплющенная толщина")]
    [DataRow(0.5f, 3f, 6f, 0f, 0f, 1f, 2.8f, 0f, -15f, 4f, DisplayName = "почти переворот вокруг Z")]
    public void Plane_world_normal_is_perpendicular_and_on_the_side_of_transformed_up(
        float sx, float sy, float sz, float ax, float ay, float az, float angle, float px, float py, float pz)
    {
        var transform = CreateTransform(new(sx, sy, sz), new(ax, ay, az), angle, new(px, py, pz));
        var model = transform.GetModelMatrix();
        // Образ локального +Y — направление, а не нормаль: model без translation. Он не лежит в образе плоскости
        // (model обратима), поэтому задаёт сторону лицевой поверхности независимо от формулы нормали.
        var transformedUp = Vector3.TransformNormal(Vector3.UnitY, model);

        var triangles = PrepareWorldTriangles(PrimitiveFactory.CreatePlane(2f, 3f), transform, out _);

        Assert.HasCount(2, triangles);
        for (int t = 0; t < triangles.Count; t++)
        {
            foreach (var normal in triangles[t].Normals)
            {
                AssertPerpendicularToEdges(normal, triangles[t], $"triangle {t}");
                Assert.IsGreaterThan(0f, Vector3.Dot(normal, transformedUp),
                    $"triangle {t}: нормаль {normal} не на стороне образа +Y {transformedUp}");
            }
        }
    }

    /// <summary>Мировой треугольник: позиции после model и единичные мировые нормали трёх вершин.</summary>
    internal readonly record struct WorldTriangle(Vector3 A, Vector3 B, Vector3 C, Vector3[] Normals)
    {
        public Vector3 Center => (A + B + C) / 3f;

        /// <summary>
        /// Знак ориентации обхода относительно нормали: +1 — CCW при взгляде со стороны нормали, −1 — CW.
        /// </summary>
        public int OrientationSign(Vector3 normal) => MathF.Sign(Vector3.Dot(Vector3.Cross(B - A, C - A), normal));
    }

    internal static Transform CreateTransform(Vector3 scale, Vector3 axis, float angle, Vector3 position) => new()
    {
        Position = position,
        Rotation = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), angle),
        Scale = scale,
    };

    /// <summary>
    /// Готовит кадр со сценой из одного объекта и возвращает его треугольники в мировых координатах.
    /// Позиции переводятся model из Prepare, нормали — <see cref="WorldNormal"/> по NormalTransforms из Prepare.
    /// </summary>
    internal static List<WorldTriangle> PrepareWorldTriangles(MeshData mesh, Transform transform, out Vector3 worldCenter)
    {
        var scene = new Scene();
        var item = new SceneObject(mesh, new MaterialData());
        item.Transform.Position = transform.Position;
        item.Transform.Rotation = transform.Rotation;
        item.Transform.Scale = transform.Scale;
        scene.Add(item);
        var frame = new FramePreparation();
        frame.Prepare(scene, new Camera { Position = new Vector3(0f, 0f, 10f), Target = Vector3.Zero },
            FramebufferWidth, FramebufferHeight);

        var model = frame.Models[0];
        var normalTransform = frame.NormalTransforms[0];
        worldCenter = Vector3.Transform(Vector3.Zero, model);

        var vertices = mesh.Vertices;
        var indices = mesh.Indices;
        var triangles = new List<WorldTriangle>(mesh.TriangleCount);
        for (int i = 0; i < indices.Length; i += 3)
        {
            var a = vertices[(int)indices[i]];
            var b = vertices[(int)indices[i + 1]];
            var c = vertices[(int)indices[i + 2]];
            triangles.Add(new WorldTriangle(
                Vector3.Transform(a.Position, model),
                Vector3.Transform(b.Position, model),
                Vector3.Transform(c.Position, model),
                [WorldNormal(a.Normal, normalTransform), WorldNormal(b.Normal, normalTransform), WorldNormal(c.Normal, normalTransform)]));
        }

        return triangles;
    }

    /// <summary>
    /// Мировая нормаль по данным NormalTransform так, как их применяет шейдер (api.md §8): компоненты делятся на |scale|
    /// через 2^log2(1/|scale|) из строки 4, затем вектор умножается как направление (без translation) на строки 1–3
    /// и нормализуется. Масштабы в сценариях умеренные, поэтому 2^x во float не переполняется.
    /// </summary>
    internal static Vector3 WorldNormal(Vector3 localNormal, Matrix4x4 normalTransform)
    {
        var inverseScale = new Vector3(
            MathF.Pow(2f, normalTransform.M41), MathF.Pow(2f, normalTransform.M42), MathF.Pow(2f, normalTransform.M43));
        return Vector3.Normalize(Vector3.TransformNormal(localNormal * inverseScale, normalTransform));
    }

    /// <summary>Единичная нормаль перпендикулярна обоим рёбрам треугольника, исходящим из A, с допуском <see cref="NormalPerpendicularTolerance"/>.</summary>
    internal static void AssertPerpendicularToEdges(Vector3 normal, WorldTriangle triangle, string context)
    {
        foreach (var edge in new[] { triangle.B - triangle.A, triangle.C - triangle.A })
        {
            float cosine = Vector3.Dot(normal, Vector3.Normalize(edge));
            Assert.IsLessThanOrEqualTo(NormalPerpendicularTolerance, MathF.Abs(cosine),
                $"{context}: cos(нормаль {normal}, ребро {edge}) = {cosine}");
        }
    }

    /// <summary>Нормаль смотрит наружу: dot(нормаль, центр треугольника − центр объекта) &gt; 0 (для выпуклого тела).</summary>
    internal static void AssertOutward(Vector3 normal, Vector3 triangleCenter, Vector3 objectCenter, string context)
    {
        float outward = Vector3.Dot(normal, triangleCenter - objectCenter);
        Assert.IsGreaterThan(0f, outward, $"{context}: нормаль {normal} не наружу, dot = {outward}");
    }
}
