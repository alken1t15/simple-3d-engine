namespace Engine3D.Core;

// Описание происхождения меша, созданного фабрикой примитивов. Хранится внутри MeshData,
// чтобы сохранение сцены в JSON (#25) записывало тип и размеры примитива, а не угадывало их по геометрии.
internal abstract record PrimitiveDescription;

internal sealed record CubeDescription(float Size) : PrimitiveDescription;

internal sealed record PlaneDescription(float Width, float Depth) : PrimitiveDescription;
