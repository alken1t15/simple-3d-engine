using System.Numerics;
using System.Runtime.InteropServices;

namespace OpenTkSpike.Tests;

// Проверяет соглашения, которые рендер spike использует при передаче матриц System.Numerics в GLSL.
public class MatrixConventionTests
{
    private const float Tolerance = 1e-5f;

    private static readonly Matrix4x4 Model =
        Matrix4x4.CreateScale(1.5f, 0.5f, 2f)
        * Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromAxisAngle(Vector3.Normalize(new Vector3(0.3f, 1f, 0.2f)), 0.7f))
        * Matrix4x4.CreateTranslation(0.4f, -0.2f, 1.1f);

    private static readonly Matrix4x4 View = Matrix4x4.CreateLookAt(new Vector3(2.5f, 2f, 4f), Vector3.Zero, Vector3.UnitY);

    private static readonly Matrix4x4 Projection = GlProjection.CreatePerspective(MathF.PI / 3f, 4f / 3f, 0.1f, 100f);

    private static readonly Vector4 Point = new(0.25f, -0.5f, 0.75f, 1f);

    [Fact]
    public void Glsl_reads_uploaded_matrix_as_transpose_so_column_vector_product_equals_row_vector_product()
    {
        var expected = Vector4.Transform(Point, Model); // System.Numerics: v * M

        var actual = GlslMultiply(Model, Point);        // GLSL: M * v

        AssertEqual(expected, actual);
    }

    [Fact]
    public void Shader_chain_projection_view_model_equals_csharp_model_view_projection()
    {
        var expected = Vector4.Transform(Point, Model * View * Projection);

        var actual = GlslMultiply(Projection, GlslMultiply(View, GlslMultiply(Model, Point)));

        AssertEqual(expected, actual);
    }

    [Fact]
    public void Model_matrix_applies_scale_then_rotation_then_translation()
    {
        var model = Matrix4x4.CreateScale(2f)
            * Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f))
            * Matrix4x4.CreateTranslation(0f, 0f, 5f);

        // (1,0,0) → масштаб (2,0,0) → поворот на +90° вокруг Y в правой системе (0,0,-2) → сдвиг (0,0,3).
        var actual = Vector3.Transform(Vector3.UnitX, model);

        AssertEqual(new Vector3(0f, 0f, 3f), actual);
        Assert.Equal(new Vector3(0f, 0f, 5f), model.Translation); // сдвиг хранится в M41..M43 (нижняя строка)
    }

    [Fact]
    public void Look_at_camera_looks_along_negative_z_of_view_space()
    {
        var view = Matrix4x4.CreateLookAt(new Vector3(0f, 0f, 5f), Vector3.Zero, Vector3.UnitY);

        var target = Vector3.Transform(Vector3.Zero, view);
        var right = Vector3.Transform(Vector3.UnitX, view);

        AssertEqual(new Vector3(0f, 0f, -5f), target);
        AssertEqual(new Vector3(1f, 0f, -5f), right);
    }

    [Theory]
    [InlineData(0.1f, -1f)]
    [InlineData(100f, 1f)]
    public void Corrected_projection_maps_near_and_far_planes_to_opengl_ndc_range(float distance, float expectedNdcZ)
    {
        var projection = GlProjection.CreatePerspective(MathF.PI / 3f, 4f / 3f, 0.1f, 100f);

        Assert.Equal(expectedNdcZ, NdcDepth(projection, distance), Tolerance);
    }

    [Theory]
    [InlineData(0.1f, 0f)]
    [InlineData(100f, 1f)]
    public void System_numerics_projection_alone_uses_zero_to_one_depth_range(float distance, float expectedNdcZ)
    {
        // Фиксирует найденную ловушку: без поправки GlProjection глубина попадает в [0, 1], а не в [-1, 1] OpenGL.
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, 4f / 3f, 0.1f, 100f);

        Assert.Equal(expectedNdcZ, NdcDepth(projection, distance), Tolerance);
    }

    [Fact]
    public void Corrected_projection_keeps_x_y_and_w_unchanged()
    {
        var original = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, 4f / 3f, 0.1f, 100f);
        var corrected = GlProjection.CreatePerspective(MathF.PI / 3f, 4f / 3f, 0.1f, 100f);
        var viewSpacePoint = new Vector4(0.3f, -0.2f, -5f, 1f);

        var a = Vector4.Transform(viewSpacePoint, original);
        var b = Vector4.Transform(viewSpacePoint, corrected);

        Assert.Equal(a.X, b.X, Tolerance);
        Assert.Equal(a.Y, b.Y, Tolerance);
        Assert.Equal(a.W, b.W, Tolerance);
    }

    // Имитирует GLSL для матрицы, переданной glUniformMatrix4fv(..., transpose: false, ...):
    // GLSL читает 16 чисел по столбцам (столбец c — элементы 4c..4c+3) и умножает на вектор-столбец справа.
    private static Vector4 GlslMultiply(Matrix4x4 uploaded, Vector4 v)
    {
        ReadOnlySpan<float> m = MemoryMarshal.CreateReadOnlySpan(ref uploaded.M11, 16);
        var result = Vector4.Zero;
        for (int column = 0; column < 4; column++)
            result += new Vector4(m[column * 4], m[column * 4 + 1], m[column * 4 + 2], m[column * 4 + 3]) * v[column];
        return result;
    }

    // Точка на оси взгляда на заданном расстоянии перед камерой (в пространстве вида камера смотрит вдоль -Z).
    private static float NdcDepth(Matrix4x4 projection, float distance)
    {
        var clip = Vector4.Transform(new Vector4(0f, 0f, -distance, 1f), projection);
        return clip.Z / clip.W;
    }

    private static void AssertEqual(Vector4 expected, Vector4 actual)
    {
        Assert.Equal(expected.X, actual.X, Tolerance);
        Assert.Equal(expected.Y, actual.Y, Tolerance);
        Assert.Equal(expected.Z, actual.Z, Tolerance);
        Assert.Equal(expected.W, actual.W, Tolerance);
    }

    private static void AssertEqual(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, Tolerance);
        Assert.Equal(expected.Y, actual.Y, Tolerance);
        Assert.Equal(expected.Z, actual.Z, Tolerance);
    }
}
