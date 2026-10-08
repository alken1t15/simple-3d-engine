using System.Numerics;
using System.Runtime.InteropServices;

namespace Engine3D.Core;

/// <summary>
/// Вершина меша: позиция, текстурные координаты и локальная нормаль поверхности.
/// Раскладка в памяти последовательная, 32 байта: Position (3 float), Uv (2 float), Normal (3 float) —
/// в таком виде вершины передаются на GPU без преобразования.
/// </summary>
/// <remarks>UV (0, 0) — левый нижний угол изображения.</remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct Vertex(Vector3 Position, Vector2 Uv, Vector3 Normal);
