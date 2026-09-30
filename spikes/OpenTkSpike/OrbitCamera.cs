using System.Numerics;

namespace OpenTkSpike;

// Камера, облетающая точку Target по сфере: yaw — поворот вокруг оси Y, pitch — наклон над плоскостью XZ.
internal sealed class OrbitCamera
{
    private const float MinPitch = -1.5f;
    private const float MaxPitch = 1.5f; // чуть меньше π/2, чтобы направление взгляда не совпало с Up
    private const float MinDistance = 1.5f;
    private const float MaxDistance = 20f;

    private float _yaw;
    private float _pitch;
    private float _distance;

    public OrbitCamera(Vector3 target, Vector3 initialPosition)
    {
        Target = target;
        var offset = initialPosition - target;
        _distance = Math.Clamp(offset.Length(), MinDistance, MaxDistance);
        _yaw = MathF.Atan2(offset.X, offset.Z);
        _pitch = Math.Clamp(MathF.Asin(offset.Y / offset.Length()), MinPitch, MaxPitch);
    }

    public Vector3 Target { get; }

    public Vector3 Position
    {
        get
        {
            float horizontal = _distance * MathF.Cos(_pitch);
            return Target + new Vector3(horizontal * MathF.Sin(_yaw), _distance * MathF.Sin(_pitch), horizontal * MathF.Cos(_yaw));
        }
    }

    public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Target, Vector3.UnitY);

    // Поворот «за сцену»: движение мыши вправо поворачивает сцену вправо (камера уходит влево), вниз — камера поднимается.
    public void Rotate(float deltaYaw, float deltaPitch)
    {
        _yaw -= deltaYaw;
        _pitch = Math.Clamp(_pitch + deltaPitch, MinPitch, MaxPitch);
    }

    // steps > 0 — приближение, < 0 — отдаление; каждый шаг меняет расстояние на 10 %.
    public void Zoom(float steps) =>
        _distance = Math.Clamp(_distance * MathF.Pow(0.9f, steps), MinDistance, MaxDistance);
}
