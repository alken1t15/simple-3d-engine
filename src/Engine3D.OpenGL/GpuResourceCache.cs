namespace Engine3D.OpenGL;

/// <summary>
/// GPU-ресурсы по идентичности CPU-объекта: общий <c>MeshData</c> у многих объектов сцены получает одно
/// GPU-представление. Ресурсы живут до <see cref="DisposeAll"/>, то есть до Dispose движка.
/// </summary>
internal sealed class GpuResourceCache<TKey, TResource>
    where TKey : class
    where TResource : class, IDisposable
{
    private readonly Dictionary<TKey, TResource> _resources = new(ReferenceEqualityComparer.Instance);
    private readonly Func<TKey, TResource> _create;

    public GpuResourceCache(Func<TKey, TResource> create) => _create = create;

    /// <summary>Сколько GPU-ресурсов создано — способ проверить переиспользование без системы профилирования.</summary>
    public int Count => _resources.Count;

    public TResource GetOrCreate(TKey key)
    {
        if (!_resources.TryGetValue(key, out var resource))
        {
            resource = _create(key);
            _resources.Add(key, resource);
        }

        return resource;
    }

    /// <summary>Освобождает все ресурсы; ошибка одного не отменяет попыток для остальных — ошибки собираются.</summary>
    public void DisposeAll(List<Exception> errors)
    {
        foreach (var resource in _resources.Values)
        {
            try
            {
                resource.Dispose();
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }
        }

        _resources.Clear();
    }
}
