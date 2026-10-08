namespace Engine3D.Core;

/// <summary>
/// Непрозрачный материал: базовый цвет и необязательная текстура. Свойства можно менять во время Run
/// на потоке onUpdate; одна <see cref="TextureData"/> может использоваться многими материалами.
/// </summary>
public sealed class MaterialData
{
    public ColorRgba BaseColor { get; set; } = new(1f, 1f, 1f, 1f);

    public TextureData? Texture { get; set; }

    /// <summary>
    /// Проверяет текущее состояние. Вызывается renderer перед кадром и сохранением сцены,
    /// поэтому присваивание неверного значения само по себе ошибкой не является.
    /// </summary>
    /// <exception cref="InvalidOperationException">Компонента BaseColor вне [0, 1] или не конечна.</exception>
    public void Validate()
    {
        if (!BaseColor.IsValid)
            throw new InvalidOperationException($"Material base color {BaseColor} must have components in [0, 1].");
    }
}
