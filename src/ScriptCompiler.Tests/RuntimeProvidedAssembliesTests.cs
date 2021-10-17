using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using Xunit;

namespace ScriptCompiler.Tests
{
    [ExcludeFromCodeCoverage]
    public class RuntimeProvidedAssembliesTests
    {
        [Fact]
        public void ProvidedAssemblies_Contains_Netstandard()
        {
            RuntimeProvidedAssemblies.ProvidedAssemblies.Should().Contain("netstandard.dll");
        }
    }
}
