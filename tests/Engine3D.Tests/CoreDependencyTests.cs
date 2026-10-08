using Engine3D.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class CoreDependencyTests
{
    [TestMethod]
    public void Core_does_not_reference_OpenTK_or_OpenGL()
    {
        // Критерий приемки #7: Core не зависит от окна и вызовов OpenGL.
        var references = typeof(Scene).Assembly.GetReferencedAssemblies().Select(name => name.Name ?? "");

        Assert.DoesNotContain(
            name => name.StartsWith("OpenTK", StringComparison.OrdinalIgnoreCase)
                || name.Contains("OpenGL", StringComparison.OrdinalIgnoreCase),
            references);
    }
}
