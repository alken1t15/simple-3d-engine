using System.Collections.ObjectModel;

namespace Engine3D.Core;

/// <summary>
/// Упорядоченный набор объектов и освещение. Сцена не владеет GPU-ресурсами и не требует Dispose:
/// удаление объекта не затрагивает меш и материал, которые могут использоваться другими объектами.
/// </summary>
public sealed class Scene
{
    private readonly List<SceneObject> _objects = [];

    // Отдельный набор для проверки повторного добавления за O(1): в нагрузочной сцене около 1000 объектов.
    private readonly HashSet<SceneObject> _members = new(ReferenceEqualityComparer.Instance);

    public Scene()
    {
        Objects = new ReadOnlyCollection<SceneObject>(_objects);
    }

    public SceneLighting Lighting { get; } = new();

    /// <summary>Объекты в порядке добавления; представление только для чтения, без копирования.</summary>
    public IReadOnlyList<SceneObject> Objects { get; }

    /// <exception cref="ArgumentNullException">Объект равен null.</exception>
    /// <exception cref="ArgumentException">Объект уже есть в этой сцене.</exception>
    public void Add(SceneObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!_members.Add(item))
            throw new ArgumentException("The object is already in this scene.", nameof(item));

        _objects.Add(item);
    }

    /// <returns>true, если объект был в сцене и удален; false, если его там не было.</returns>
    /// <exception cref="ArgumentNullException">Объект равен null.</exception>
    public bool Remove(SceneObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!_members.Remove(item))
            return false;

        _objects.Remove(item);
        return true;
    }
}
