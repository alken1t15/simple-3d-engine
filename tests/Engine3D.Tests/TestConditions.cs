using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

/// <summary>
/// Тест с окном и контекстом OpenGL 3.3: выполняется только при <c>ENGINE3D_GRAPHICS_TESTS=1</c> (нужны дисплей и
/// драйвер OpenGL), иначе пропускается. Обычный <c>dotnet test</c> графического окружения не требует.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class GraphicsTestAttribute : ConditionBaseAttribute
{
    public const string VariableName = "ENGINE3D_GRAPHICS_TESTS";

    public GraphicsTestAttribute()
        : base(ConditionMode.Include) =>
        IgnoreMessage = $"Graphics test: set {VariableName}=1 to run it with a display and an OpenGL 3.3 driver.";

    public override bool IsConditionMet => Environment.GetEnvironmentVariable(VariableName) == "1";

    public override string GroupName => nameof(GraphicsTestAttribute);
}
