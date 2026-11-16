using System.Reflection;

namespace AiNewsBot.Core.Tests;

public class ArchitectureTests
{
    [Fact]
    public void Core_does_not_reference_other_project_assemblies()
    {
        var core = Assembly.Load("AiNewsBot.Core");

        var projectRefs = core.GetReferencedAssemblies()
            .Where(a => a.Name!.StartsWith("AiNewsBot", StringComparison.Ordinal));

        Assert.Empty(projectRefs);
    }
}
