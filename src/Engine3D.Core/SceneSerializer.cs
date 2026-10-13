using System.Text.Json;

namespace Engine3D.Core;

/// <summary>
/// Сохранение собственной сцены в JSON (схема version=1, docs/api.md раздел 9) и загрузка в новую сцену.
/// CPU-код без окна и OpenGL. Методы синхронные: вызываются вне Run, на потоке приложения, без одновременного
/// изменения сцены.
/// </summary>
public static class SceneSerializer
{
    /// <summary>
    /// Сохраняет примитивы, объекты (transform, базовый цвет и путь текстуры), камеру и свет. Сначала проверяет сцену
    /// и строит весь JSON в памяти, затем пишет файл в UTF-8: при неверной сцене файл не изменяется. Общий меш
    /// записывается один раз, поворот — единичным кватернионом, путь текстуры — относительно каталога файла JSON.
    /// Пиксели, вершины и GPU-ресурсы не сохраняются. Каталог для файла создает приложение.
    /// </summary>
    /// <param name="path">Путь к файлу JSON; относительный путь разрешается от текущего каталога.</param>
    /// <exception cref="ArgumentNullException">Путь, сцена или камера равны null.</exception>
    /// <exception cref="ArgumentException">Путь пустой, недопустимый или оканчивается разделителем каталога.</exception>
    /// <exception cref="InvalidOperationException">Камера, свет, transform или материал объекта неверны.</exception>
    /// <exception cref="NotSupportedException">
    /// Меш объекта создан не фабрикой примитивов или у текстуры нет исходного файла (<see cref="TextureData.SourcePath"/>).
    /// </exception>
    /// <exception cref="IOException">Ошибка записи, например каталог не существует.</exception>
    /// <exception cref="UnauthorizedAccessException">Нет доступа к файлу.</exception>
    public static void Save(string path, Scene scene, Camera camera)
    {
        string fullPath = GetFullFilePath(path);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(camera);

        var document = SceneDocumentBuilder.Build(scene, camera, Path.GetDirectoryName(fullPath)!);
        string json = JsonSerializer.Serialize(document, SceneDocument.JsonOptions) + "\n";

        // UTF-8 без BOM. Атомарная замена старого файла при сбое записи не обещается.
        File.WriteAllText(fullPath, json);
    }

    /// <summary>
    /// Загружает сцену из JSON в новые объекты. Сначала читает и полностью проверяет документ, затем создает сцену
    /// (один меш на запись meshes, общий для ее объектов; материал на объект) и вызывает загрузчик текстур — один
    /// раз на каждый полный путь. Пути текстур разрешаются от каталога файла JSON, текущий каталог не участвует.
    /// При любой ошибке результат не возвращается; действия загрузчика до ошибки не откатываются.
    /// </summary>
    /// <param name="path">Путь к файлу JSON; относительный путь разрешается от текущего каталога.</param>
    /// <param name="textureLoader">
    /// Получает полный путь изображения и возвращает текстуру с этим <see cref="TextureData.SourcePath"/>, например
    /// Engine.LoadTexture (он работает только до Run). Обязателен, если в сцене есть текстуры; его исключения
    /// выходят из Load без изменений.
    /// </param>
    /// <exception cref="ArgumentNullException">Путь равен null.</exception>
    /// <exception cref="ArgumentException">Путь пустой, недопустимый или оканчивается разделителем каталога.</exception>
    /// <exception cref="IOException">Ошибка чтения, например файл или каталог не существует.</exception>
    /// <exception cref="UnauthorizedAccessException">Нет доступа к файлу.</exception>
    /// <exception cref="InvalidDataException">
    /// Неверный JSON (исходная <see cref="JsonException"/> — InnerException), неподдерживаемые версия или kind,
    /// отсутствующие или неверные поля, повтор id меша или ссылка на неописанный меш, абсолютный путь текстуры.
    /// Сообщение содержит путь файла, место в документе и причину.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// В сцене есть текстуры, а загрузчик не передан; загрузчик вернул null или текстуру с другим SourcePath.
    /// </exception>
    public static SceneSnapshot Load(string path, Func<string, TextureData>? textureLoader = null)
    {
        string fullPath = GetFullFilePath(path);
        string json = File.ReadAllText(fullPath); // UTF-8; BOM, если есть, отбрасывается

        SceneDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<SceneDocument>(json, SceneDocument.JsonOptions);
        }
        catch (JsonException exception)
        {
            throw SceneRestorer.InvalidDocument(fullPath, exception.Path ?? "$", exception.Message, exception);
        }

        if (document is null)
            throw SceneRestorer.InvalidDocument(fullPath, "$", "expected a scene object, got null.");

        return SceneRestorer.Restore(document, fullPath, textureLoader);
    }

    private static string GetFullFilePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Path.EndsInDirectorySeparator(path))
            throw new ArgumentException($"Path '{path}' must name a file, not a directory.", nameof(path));

        return Path.GetFullPath(path); // ArgumentException для недопустимых символов
    }
}
