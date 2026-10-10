using System.Numerics;
using Engine3D.Core;

namespace Engine3D.OpenGL;

/// <summary>
/// CPU-часть кадра: проверяет изменяемое состояние сцены (после onUpdate) и считает матрицы до любых GL-вызовов,
/// чтобы неверные данные отклонялись с InvalidOperationException, а не попадали в OpenGL.
/// Буфер model-матриц переиспользуется между кадрами.
/// </summary>
internal sealed class FramePreparation
{
    private Matrix4x4[] _models = new Matrix4x4[64];
    private int _count;

    public Matrix4x4 View { get; private set; }

    /// <summary>Проекция уже в соглашении OpenGL (глубина NDC в [−1, 1]).</summary>
    public Matrix4x4 Projection { get; private set; }

    /// <summary>Единичное направление лучей направленного света в мировых координатах.</summary>
    public Vector3 LightDirection { get; private set; }

    /// <summary>Model-матрицы объектов в порядке <see cref="Scene.Objects"/>.</summary>
    public ReadOnlySpan<Matrix4x4> Models => _models.AsSpan(0, _count);

    /// <exception cref="InvalidOperationException">Камера, свет, материал или transform объекта неверны.</exception>
    public void Prepare(Scene scene, Camera camera, int framebufferWidth, int framebufferHeight)
    {
        View = camera.GetViewMatrix();
        Projection = GlProjection.ToOpenGl(camera.GetProjectionMatrix(framebufferWidth / (float)framebufferHeight));
        LightDirection = scene.Lighting.GetNormalizedDirection();

        var objects = scene.Objects;
        if (_models.Length < objects.Count)
            Array.Resize(ref _models, Math.Max(objects.Count, _models.Length * 2));

        _count = 0;
        for (int i = 0; i < objects.Count; i++)
        {
            var item = objects[i];
            try
            {
                item.Material.Validate();
                _models[i] = item.Transform.GetModelMatrix();
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException($"Scene object {i} is invalid: {exception.Message}", exception);
            }
        }

        _count = objects.Count;
    }
}
