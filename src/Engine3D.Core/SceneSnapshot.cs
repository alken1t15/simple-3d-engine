namespace Engine3D.Core;

/// <summary>
/// Результат <see cref="SceneSerializer.Load"/>: новые сцена и камера, восстановленные из JSON. Каждый Load создает
/// свои меши, материалы и объекты; с ранее загруженными или сохраненными сценами они не связаны.
/// </summary>
public sealed record SceneSnapshot(Scene Scene, Camera Camera);
