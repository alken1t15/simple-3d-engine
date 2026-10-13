using System.Numerics;
using Engine3D.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

/// <summary>
/// CPU-сценарий зеркального transform (api.md §8: отрицательный ненулевой scale разрешён). Inverse-transpose сохраняет
/// сторону поверхности при любом обратимом линейном преобразовании, поэтому мировые нормали остаются наружными.
/// Обход же зависит от знака определителя model: при нечётном числе отрицательных компонент scale CCW-треугольники
/// становятся CW при взгляде снаружи. Поэтому back-face culling в базовой версии выключен (api.md §8): с ним
/// зеркальный объект потерял бы лицевые грани.
/// </summary>
public sealed partial class CpuScenarioTests
{
    [TestMethod]
    [DataRow(1f, 1f, 1f, 1, DisplayName = "без отражения: CCW")]
    [DataRow(-1f, 1f, 1f, -1, DisplayName = "отражение по X: CW")]
    [DataRow(1f, -2f, 1f, -1, DisplayName = "отражение по Y с растяжением: CW")]
    [DataRow(0.5f, 1f, -3f, -1, DisplayName = "отражение по Z с анизотропией: CW")]
    [DataRow(-2f, -0.5f, 1f, 1, DisplayName = "два отражения = поворот: снова CCW")]
    public void Mirrored_cube_keeps_outward_normals_and_flips_winding(float sx, float sy, float sz, int expectedOrientation)
    {
        var triangles = PrepareWorldTriangles(
            PrimitiveFactory.CreateCube(), new(sx, sy, sz), new(0.3f, 1f, -0.2f), 0.9f, new(4f, -1f, 2f), out var worldCenter);

        Assert.HasCount(12, triangles);
        AssertSurfaceAfterTransform(triangles, worldCenter, expectedOrientation);
    }

    /// <summary>
    /// Нормали cube/plane направлены по осям, и для них умножение нормали на model совпадает по направлению
    /// с inverse-transpose. У тетраэдра все грани наклонные, поэтому при неравномерном scale такая ошибка
    /// даёт нормаль, не перпендикулярную граням.
    /// </summary>
    [TestMethod]
    [DataRow(3f, 0.5f, 1.5f, 1, DisplayName = "неравномерный scale: CCW")]
    [DataRow(-3f, 0.5f, 1.5f, -1, DisplayName = "неравномерный scale, отражение по X: CW")]
    [DataRow(2f, 4f, -0.8f, -1, DisplayName = "неравномерный scale, отражение по Z: CW")]
    [DataRow(-0.6f, -2.5f, -1f, -1, DisplayName = "отражение по всем осям: CW")]
    public void Tetrahedron_oblique_faces_keep_perpendicular_outward_normals_under_mirrored_nonuniform_scale(
        float sx, float sy, float sz, int expectedOrientation)
    {
        var triangles = PrepareWorldTriangles(
            CreateTetrahedron(), new(sx, sy, sz), new(1f, -0.4f, 0.7f), 1.1f, new(-6f, 3f, 0.5f), out var worldCenter);

        Assert.HasCount(4, triangles);
        AssertSurfaceAfterTransform(triangles, worldCenter, expectedOrientation);
    }

    /// <summary>
    /// Правильный тетраэдр с центром в начале координат, вершины — чередующиеся вершины куба (±1, ±1, ±1).
    /// Грани с отдельными вершинами и плоской нормалью, равной направлению на центр грани (противоположной
    /// вершине со знаком минус); порядок вершин — CCW при взгляде снаружи (проверено вручную по cross).
    /// </summary>
    private static MeshData CreateTetrahedron()
    {
        Vector3 a = new(1f, 1f, 1f), b = new(-1f, -1f, 1f), c = new(-1f, 1f, -1f), d = new(1f, -1f, -1f);
        (Vector3 P0, Vector3 P1, Vector3 P2, Vector3 Normal)[] faces =
        [
            (b, c, d, new(-1f, -1f, -1f)), // напротив a
            (a, d, c, new(1f, 1f, -1f)),   // напротив b
            (a, b, d, new(1f, -1f, 1f)),   // напротив c
            (a, c, b, new(-1f, 1f, 1f)),   // напротив d
        ];

        var vertices = new List<Vertex>();
        var indices = new List<uint>();
        foreach (var face in faces)
        {
            foreach (var position in new[] { face.P0, face.P1, face.P2 })
            {
                indices.Add((uint)vertices.Count);
                vertices.Add(new Vertex(position, Vector2.Zero, face.Normal));
            }
        }

        return new MeshData(vertices, indices);
    }

    /// <summary>
    /// Для каждой вершинной нормали: знак обхода равен ожидаемому (+1 — CCW снаружи, −1 — CW), нормаль
    /// перпендикулярна рёбрам и направлена наружу выпуклого тела с центром <paramref name="worldCenter"/>.
    /// </summary>
    private static void AssertSurfaceAfterTransform(List<WorldTriangle> triangles, Vector3 worldCenter, int expectedOrientation)
    {
        for (int t = 0; t < triangles.Count; t++)
        {
            var triangle = triangles[t];
            foreach (var normal in triangle.Normals)
            {
                Assert.AreEqual(expectedOrientation, triangle.OrientationSign(normal),
                    $"triangle {t}: winding sign relative to normal {normal}");
                AssertPerpendicularToEdges(normal, triangle, $"triangle {t}");
                AssertOutward(normal, triangle.Center, worldCenter, $"triangle {t}");
            }
        }
    }
}
