using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Engine3D.Core;

/// <summary>
/// Документ JSON сцены, схема version=1 (docs/api.md, раздел 9): внутренние DTO из чисел, массивов и строк.
/// Типы System.Numerics и граф доменных объектов напрямую не сериализуются. При чтении System.Text.Json проверяет
/// наличие обязательных полей, null у свойств, типы значений и повторы известных полей (неизвестные свойства, в том
/// числе повторные, игнорируются); null-элементы массивов, длины массивов, конечность и диапазоны чисел и ссылки на
/// меши проверяет <see cref="SceneRestorer"/>.
/// </summary>
internal sealed class SceneDocument
{
    public const int SupportedVersion = 1;

    /// <summary>Формат файла: ключи camelCase, отступы и LF, массивы чисел одной строкой, строгий разбор.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectNullableAnnotations = true, // null допустим только там, где это разрешает тип: texturePath и размеры меша
        AllowDuplicateProperties = false, // повтор известного поля — неверный документ; неизвестные свойства пропускаются целиком
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // файл не встраивается в HTML: кириллица и '+' в путях как есть
        Converters = { new InlineNumberArrayConverter() },
    };

    [JsonRequired] public int Version { get; set; }
    [JsonRequired] public MeshDocument[] Meshes { get; set; } = [];
    [JsonRequired] public ObjectDocument[] Objects { get; set; } = [];
    [JsonRequired] public CameraDocument Camera { get; set; } = new();
    [JsonRequired] public LightingDocument Lighting { get; set; } = new();
}

/// <summary>Описание примитива: у cube — size, у plane — width и depth; размеры другого вида не записываются.</summary>
internal sealed class MeshDocument
{
    public const string Cube = "cube";
    public const string Plane = "plane";

    [JsonRequired] public string Id { get; set; } = "";
    [JsonRequired] public string Kind { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public float? Size { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public float? Width { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public float? Depth { get; set; }
}

internal sealed class ObjectDocument
{
    [JsonRequired] public string MeshId { get; set; } = "";
    [JsonRequired] public TransformDocument Transform { get; set; } = new();
    [JsonRequired] public MaterialDocument Material { get; set; } = new();
}

internal sealed class TransformDocument
{
    [JsonRequired] public float[] Position { get; set; } = [];
    [JsonRequired] public float[] Rotation { get; set; } = []; // кватернион [x, y, z, w]
    [JsonRequired] public float[] Scale { get; set; } = [];
}

internal sealed class MaterialDocument
{
    [JsonRequired] public float[] BaseColor { get; set; } = []; // RGBA
    [JsonRequired] public string? TexturePath { get; set; } // поле обязательно; null — без текстуры, иначе путь от каталога JSON
}

internal sealed class CameraDocument
{
    [JsonRequired] public float[] Position { get; set; } = [];
    [JsonRequired] public float[] Target { get; set; } = [];
    [JsonRequired] public float[] Up { get; set; } = [];
    [JsonRequired] public float FovDegrees { get; set; }
    [JsonRequired] public float NearPlane { get; set; }
    [JsonRequired] public float FarPlane { get; set; }
}

internal sealed class LightingDocument
{
    [JsonRequired] public float[] AmbientColor { get; set; } = [];
    [JsonRequired] public float[] DirectionalColor { get; set; } = [];
    [JsonRequired] public float[] Direction { get; set; } = [];
}

/// <summary>
/// Массив чисел одной строкой ([3, 2, 5]) и в документе с отступами — компактно и как в примере контракта.
/// Число записывается кратчайшей формой, которая читается обратно в тот же float.
/// </summary>
internal sealed class InlineNumberArrayConverter : JsonConverter<float[]>
{
    public override float[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // JsonException без текста: System.Text.Json сам дополнит сообщение путем в документе, строкой и позицией.
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException();

        var values = new List<float>(4);
        while (true)
        {
            if (!reader.Read())
                throw new JsonException();
            if (reader.TokenType == JsonTokenType.EndArray)
                return [.. values];
            if (reader.TokenType != JsonTokenType.Number || !reader.TryGetSingle(out float value))
                throw new JsonException();

            values.Add(value); // переполнение дает ±∞: конечность проверяет SceneSerializer
        }
    }

    public override void Write(Utf8JsonWriter writer, float[] value, JsonSerializerOptions options)
    {
        var text = new StringBuilder("[");
        for (int i = 0; i < value.Length; i++)
            text.Append(i == 0 ? "" : ", ").Append(value[i].ToString("R", CultureInfo.InvariantCulture));

        writer.WriteRawValue(text.Append(']').ToString());
    }
}
