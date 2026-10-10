using System.Numerics;
using Engine3D.Core;

namespace Engine3D.OpenGL;

/// <summary>
/// CPU-часть кадра: проверяет изменяемое состояние сцены (после onUpdate) и считает матрицы до любых GL-вызовов,
/// чтобы неверные данные отклонялись с InvalidOperationException, а не попадали в OpenGL.
/// Буферы матриц переиспользуются между кадрами.
/// </summary>
internal sealed class FramePreparation
{
    private Matrix4x4[] _models = new Matrix4x4[64];
    private Matrix4x4[] _normalTransforms = new Matrix4x4[64];
    private int _count;

    public Matrix4x4 View { get; private set; }

    /// <summary>Проекция уже в соглашении OpenGL (глубина NDC в [−1, 1]).</summary>
    public Matrix4x4 Projection { get; private set; }

    /// <summary>Единичное направление лучей направленного света в мировых координатах.</summary>
    public Vector3 LightDirection { get; private set; }

    /// <summary>Model-матрицы объектов в порядке <see cref="Scene.Objects"/>.</summary>
    public ReadOnlySpan<Matrix4x4> Models => _models.AsSpan(0, _count);

    /// <summary>Данные для нормалей объектов в порядке <see cref="Scene.Objects"/>, см. <see cref="GetNormalTransform"/>.</summary>
    public ReadOnlySpan<Matrix4x4> NormalTransforms => _normalTransforms.AsSpan(0, _count);

    /// <exception cref="InvalidOperationException">Камера, свет, материал или transform объекта неверны.</exception>
    public void Prepare(Scene scene, Camera camera, int framebufferWidth, int framebufferHeight)
    {
        View = camera.GetViewMatrix();
        Projection = GlProjection.ToOpenGl(camera.GetProjectionMatrix(framebufferWidth / (float)framebufferHeight));
        LightDirection = scene.Lighting.GetNormalizedDirection();

        var objects = scene.Objects;
        if (_models.Length < objects.Count)
        {
            int capacity = Math.Max(objects.Count, _models.Length * 2);
            Array.Resize(ref _models, capacity);
            Array.Resize(ref _normalTransforms, capacity);
        }

        _count = 0;
        for (int i = 0; i < objects.Count; i++)
        {
            var item = objects[i];
            try
            {
                item.Material.Validate();
                _models[i] = item.Transform.GetModelMatrix();
                _normalTransforms[i] = GetNormalTransform(_models[i], item.Transform.Scale);
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException($"Scene object {i} is invalid: {exception.Message}", exception);
            }
        }

        _count = objects.Count;
    }

    /// <summary>
    /// Нормаль переводится inverse-transpose линейной части model = scale * rotation, то есть n_world = (n / scale) * rotation.
    /// Допустимый Scale бывает от ~1e-38 до ~1e38 по оси, и n / scale во float переполняется или обнуляется; поэтому шейдер
    /// делит на масштаб в log2-пространстве. Строки 1–3 результата — строки rotation со знаками масштаба (строки model,
    /// деленные на |scale|), строка 4 (x, y, z) — log2(1 / |scale|) по осям. Scale уже проверен: конечный и ненулевой.
    /// </summary>
    internal static Matrix4x4 GetNormalTransform(in Matrix4x4 model, Vector3 scale)
    {
        float x = MathF.Abs(scale.X), y = MathF.Abs(scale.Y), z = MathF.Abs(scale.Z);
        return new Matrix4x4(
            model.M11 / x, model.M12 / x, model.M13 / x, 0f,
            model.M21 / y, model.M22 / y, model.M23 / y, 0f,
            model.M31 / z, model.M32 / z, model.M33 / z, 0f,
            -MathF.Log2(x), -MathF.Log2(y), -MathF.Log2(z), 1f);
    }
}
