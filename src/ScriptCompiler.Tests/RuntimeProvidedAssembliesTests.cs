namespace ScriptCompiler.Tests;

public class RuntimeProvidedAssembliesTests
{
    [Fact]
    public void ProvidedAssemblies_Contains_Netstandard()
    {
        RuntimeProvidedAssemblies.ProvidedAssemblies.ShouldContain("netstandard.dll");
    }
}
