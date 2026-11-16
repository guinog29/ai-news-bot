using System.Reflection;

namespace AiNewsBot.Infrastructure.Tests;

public class ArchitectureTests
{
    [Fact]
    public void Infrastructure_does_not_reference_the_entrypoint()
    {
        var infra = Assembly.Load("AiNewsBot.Infrastructure");

        var names = infra.GetReferencedAssemblies().Select(a => a.Name);

        Assert.DoesNotContain("AiNewsBot", names);
    }
}
