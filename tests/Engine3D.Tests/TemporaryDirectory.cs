namespace Engine3D.Tests;

internal static class TemporaryDirectory
{
    /// <summary>
    /// Создает временный каталог и возвращает его путь в том виде, в каком его отдает
    /// <see cref="Environment.CurrentDirectory"/>. Если временный каталог лежит под символьной ссылкой (на macOS
    /// /var → /private/var), после смены текущего каталога относительные пути разрешаются уже от реального пути,
    /// и тест сравнивал бы два написания одного каталога.
    /// </summary>
    public static string Create(string prefix)
    {
        string path = Directory.CreateTempSubdirectory(prefix).FullName;
        string previous = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = path;
            return Environment.CurrentDirectory;
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
    }
}
