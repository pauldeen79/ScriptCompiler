namespace ScriptCompiler.Tests;

public sealed class CompilerTests
{
    public ScriptCompiler Sut { get; }

    public CompilerTests()
    {
        Sut = new ScriptCompiler();
    }

    public string GetTempPath(string suffix)
    {
        var result = Path.Combine(Path.GetTempPath(), $"UTC_{suffix}");
        if (!string.IsNullOrEmpty(result) && Directory.Exists(result))
        {
            Directory.Delete(result, true);
        }
        return result;
    }

    [Fact]
    public void Can_Compile_Script()
    {
        // Arrange
        var script = @"namespace MyNamespace
{
    public static class MyClass
    {
        public static string MyFunction() => ""Hello world"";
    }
}";
        var packageReferences = new List<string>
        {
            "NETStandard.Library,2.0.3,.NETStandard,Version=v2.0"
        };

        // Act
        var result = Sut.LoadScriptToMemory
        (
            script,
            Enumerable.Empty<string>(),
            packageReferences,
            GetTempPath(nameof(Can_Compile_Script)),
            null,
            null
        );

        // Assert
        result.Errors.ShouldBeEmpty();
        result.IsSuccessful.ShouldBeTrue();
        result.Diagnostics.HasErrors().ShouldBeFalse();
        result.CompiledAssembly.ShouldNotBeNull();
        var myClass = Array.Find(result.CompiledAssembly.GetExportedTypes(), x => x.Name == "MyClass");
        myClass.ShouldNotBeNull();
        var functionResult = myClass.GetMethod("MyFunction").Invoke(null, null) as string;
        functionResult.ShouldBe("Hello world");
    }

    [Fact]
    public void Can_Compile_Script_With_Nuget_Reference()
    {
        // Arrange
        var script = @"using CrossCutting.Data.Abstractions;

namespace MyNamespace
{
    public static class MyClass
    {
        public static string MyFunction() => typeof(IDatabaseCommand).Name;
    }
}";
        var packageReferences = new List<string>
        {
            "NETStandard.Library,2.0.3,.NETStandard,Version=v2.0",
            "pauldeen79.CrossCutting.Data.Abstractions,2.7.5"
        };
        var tempPath = GetTempPath(nameof(Can_Compile_Script_With_Nuget_Reference));
        var context = AssemblyLoadContext.Default;
        var handler = new Func<AssemblyLoadContext, AssemblyName, Assembly>((sender, args) =>
        {
            return sender.LoadFromAssemblyPath(Path.Combine(tempPath, args.Name + ".dll"));
        });
        context.Resolving += handler;
        try
        {
            // Act
            var result = Sut.LoadScriptToMemory
            (
                script,
                Enumerable.Empty<string>(),
                packageReferences,
                tempPath,
                null,
                context
            );

            // Assert
            result.Errors.ShouldBeEmpty();
            result.IsSuccessful.ShouldBeTrue();
            result.Diagnostics.HasErrors().ShouldBeFalse();
            result.CompiledAssembly.ShouldNotBeNull();
            var myClass = Array.Find(result.CompiledAssembly.GetExportedTypes(), x => x.Name == "MyClass");
            myClass.ShouldNotBeNull();
            var functionResult = myClass.GetMethod("MyFunction").Invoke(null, null) as string;
            functionResult.ShouldBe("IDatabaseCommand");
        }
        finally
        {
            context.Resolving -= handler;
        }
    }

    [Theory]
    [InlineData("ScriptCompiler.Tests.dll")]
    [InlineData("ScriptCompiler.Tests, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null")]
    public void Can_Compile_Script_With_Assembly_Reference(string referenceName)
    {
        // Arrange
        var script = @"using ScriptCompiler.Tests;

namespace MyNamespace
{
    public static class MyClass
    {
        public static string MyFunction() => typeof(CompilerTests).Name;
    }
}";
        var packageReferences = new List<string>
        {
            "NETStandard.Library,2.0.3,.NETStandard,Version=v2.0"
        };
        var context = AssemblyLoadContext.Default;
        var tempPath = Directory.GetCurrentDirectory();
        var handler = new Func<AssemblyLoadContext, AssemblyName, Assembly>((sender, args) =>
        {
            return sender.LoadFromAssemblyPath(Path.Combine(tempPath, args.Name + ".dll"));
        });
        context.Resolving += handler;
        try
        {
            // Act
            var result = Sut.LoadScriptToMemory
            (
                script,
                new[] { referenceName },
                packageReferences,
                tempPath,
                null,
                null
            );

            // Assert
            result.Errors.ShouldBeEmpty();
            result.IsSuccessful.ShouldBeTrue();
            result.Diagnostics.HasErrors().ShouldBeFalse();
            result.CompiledAssembly.ShouldNotBeNull();
            var myClass = Array.Find(result.CompiledAssembly.GetExportedTypes(), x => x.Name == "MyClass");
            myClass.ShouldNotBeNull();
            var functionResult = myClass.GetMethod("MyFunction").Invoke(null, null) as string;
            functionResult.ShouldBe("CompilerTests");
        }
        finally
        {
            context.Resolving -= handler;
        }
    }

    [Fact]
    public void Can_Compile_Script_With_Recursive_Nuget_Reference()
    {
        // Arrange
        var script = @"using CrossCutting.Data.Core.Commands;

namespace MyNamespace
{
    public static class MyClass
    {
        public static string MyFunction() => typeof(SqlDatabaseCommand).Name;
    }
}";
        var packageReferences = new List<string>
        {
            "NETStandard.Library,2.0.3,.NETStandard,Version=v2.0",
            "pauldeen79.CrossCutting.Data.Core,2.7.5"
        };
        var tempPath = GetTempPath(nameof(Can_Compile_Script_With_Recursive_Nuget_Reference));

        // Act
        var handler = new Func<AssemblyLoadContext, AssemblyName, Assembly>((sender, args) =>
        {
            return sender.LoadFromAssemblyPath(Path.Combine(tempPath, args.Name + ".dll"));
        });
        var context = AssemblyLoadContext.Default;
        context.Resolving += handler;
        try
        {
            var result = Sut.LoadScriptToMemory
            (
                script,
                Enumerable.Empty<string>(),
                packageReferences,
                tempPath,
                null,
                context
            );

            // Assert
            result.Errors.ShouldBeEmpty();
            result.IsSuccessful.ShouldBeTrue();
            result.Diagnostics.HasErrors().ShouldBeFalse();
            result.CompiledAssembly.ShouldNotBeNull();
            var myClass = Array.Find(result.CompiledAssembly.GetExportedTypes(), x => x.Name == "MyClass");
            myClass.ShouldNotBeNull();
            var functionResult = myClass.GetMethod("MyFunction").Invoke(null, null) as string;
            functionResult.ShouldBe(@"SqlDatabaseCommand");
        }
        finally
        {
            context.Resolving -= handler;
        }
    }

    [Fact]
    public void Can_Compile_Script_With_Newtonsoft_Json_Nuget_Reference()
    {
        // Arrange
        var script = @"using Newtonsoft.Json;

namespace MyNamespace
{
    public static class MyClass
    {
        public static string MyFunction() => JsonConvert.SerializeObject(new { Property = 1 });
    }
}";
        var packageReferences = new List<string>
        {
            "NETStandard.Library,2.0.3,.NETStandard,Version=v2.0",
            "Newtonsoft.Json,13.0.1,.NETStandard,Version=v2.0"
        };

        // Act
        var context = AssemblyLoadContext.Default;
        var tempPath = GetTempPath(nameof(Can_Compile_Script_With_Newtonsoft_Json_Nuget_Reference));
        var handler = new Func<AssemblyLoadContext, AssemblyName, Assembly>((sender, args) =>
        {
            return sender.LoadFromAssemblyPath(Path.Combine(tempPath, args.Name + ".dll"));
        });
        context.Resolving += handler;
        try
        {
            var result = Sut.LoadScriptToMemory
            (
                script,
                Enumerable.Empty<string>(),
                packageReferences,
                tempPath,
                null,
                context
            );

            // Assert
            result.Errors.ShouldBeEmpty();
            result.IsSuccessful.ShouldBeTrue();
            result.Diagnostics.HasErrors().ShouldBeFalse();
            result.CompiledAssembly.ShouldNotBeNull();
            var myClass = Array.Find(result.CompiledAssembly.GetExportedTypes(), x => x.Name == "MyClass");
            myClass.ShouldNotBeNull();
            var functionResult = myClass.GetMethod("MyFunction").Invoke(null, null) as string;
            functionResult.ShouldBe(@"{""Property"":1}");
        }
        finally
        {
            context.Resolving -= handler;
        }
    }

    [Fact]
    public void Invalid_Source_Returns_Errors()
    {
        // Arrange
        var script = @"namespace MyNamespace
{
    public static class MyClass
    {
        public static string MyFunction() => Error();
    }
}";

        // Act
        var result = Sut.LoadScriptToMemory
        (
            script,
            Enumerable.Empty<string>(),
            Enumerable.Empty<string>(),
            null,
            null,
            null
        );

        // Assert
        result.IsSuccessful.ShouldBeFalse();
        result.Diagnostics.HasErrors().ShouldBeTrue();
        result.Errors.ShouldNotBeEmpty();
        result.CompiledAssembly.ShouldBeNull();
    }
}
