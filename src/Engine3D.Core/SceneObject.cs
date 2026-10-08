namespace Engine3D.Core;

/// <summary>
/// Видимый экземпляр геометрии: ссылка на общий меш, материал и собственный transform.
/// Физики и игрового поведения не содержит.
/// </summary>
public sealed class SceneObject
{
    private MaterialData _material;

    /// <exception cref="ArgumentNullException">Меш или материал равен null.</exception>
    public SceneObject(MeshData mesh, MaterialData material)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(material);

        Mesh = mesh;
        _material = material;
    }

    public MeshData Mesh { get; }

    /// <exception cref="ArgumentNullException">Присваивается null.</exception>
    public MaterialData Material
    {
        get => _material;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _material = value;
        }
    }

    public Transform Transform { get; } = new();
}
