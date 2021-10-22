# ScriptCompiler
Compiles an assembly from a C# code string.

Has support for both assembly and package references.

Separate implementation for .NET Framework, because the new Roslyn compiler throws an exception saying that this operation is not supported on the platform.

Example:
```C#
//using Microsoft.Extensions.DependencyInjection;

var serviceCollection = new ServiceCollection();
var serviceProvider = serviceCollection.AddScriptCompiler().BuildServiceProvider();
var scriptCompiler = serviceProvider.GetRequiredService<IScriptCompiler>();

var sourceCode = @"namespace MyNamespace
{
    public static class MyClass
    {
        public static string MyFunction() => ""Hello world"";
    }
}";
var result = scriptCompiler.LoadScriptToMemory
(
    sourceCode,
    Enumerable.Empty<string>(),
    new[] { "NETStandard.Library,2.0.3,.NETStandard,Version=v2.0" },
    GetTempPath(nameof(Can_Compile_Script)),
    null,
    null
);
```

Note that once you have the result, then you have to use the CompiledAssembly property and use reflection to invoke calls on the produced assembly.

See unit tests for more examples.
