﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using Shouldly;
using Xunit;

namespace ScriptCompiler.NetFramework.Tests
{
    [ExcludeFromCodeCoverage]
    public sealed class CompilerTests 
    {
        public ScriptCompiler Sut { get; }

        public CompilerTests()
        {
            Sut = new ScriptCompiler();
        }

        public static string GetTempPath(string suffix)
        {
            var result = Path.Combine(Path.GetTempPath(), $"UTF_{suffix}");
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
        public static string MyFunction() { return ""Hello world""; }
    }
}";
            var packageReferences = new List<string>
            {
                "NETStandard.Library,2.0.3,.NETStandard,Version=v2.0"
            };
            var tempPath = GetTempPath(nameof(Can_Compile_Script));

            // Act
            var result = Sut.LoadScriptToMemory
            (
                script,
                "C#",
                Enumerable.Empty<string>(),
                packageReferences,
                tempPath,
                null
            );

            // Assert
            result.CompiledAssembly.ShouldNotBeNull();
            var myClass = result.CompiledAssembly.GetExportedTypes().FirstOrDefault(x => x.Name == "MyClass");
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
        public static string MyFunction() { return typeof(IDatabaseCommand).Name; }
    }
}";
            var packageReferences = new List<string>
            {
                "NETStandard.Library,2.0.3,.NETStandard,Version=v2.0",
                "pauldeen79.CrossCutting.Data.Abstractions,1.1.0"
            };
            var tempPath = GetTempPath(nameof(Can_Compile_Script_With_Nuget_Reference));
            var handler = new ResolveEventHandler((_, args) => CustomResolve(args, tempPath));
            AppDomain.CurrentDomain.AssemblyResolve += handler;
            try
            {
                // Act
                var result = Sut.LoadScriptToMemory
                (
                    script,
                    "C#",
                    Enumerable.Empty<string>(),
                    packageReferences,
                    tempPath,
                    null
                );

                // Assert
                result.CompiledAssembly.ShouldNotBeNull();
                var myClass = result.CompiledAssembly.GetExportedTypes().FirstOrDefault(x => x.Name == "MyClass");
                myClass.ShouldNotBeNull();
                var functionResult = myClass.GetMethod("MyFunction").Invoke(null, null) as string;
                functionResult.ShouldBe("IDatabaseCommand");
            }
            finally
            {
                AppDomain.CurrentDomain.AssemblyResolve -= handler;
            }
        }

        [Fact]
        public void Can_Compile_Script_With_Assembly_Reference()
        {
            // Arrange
            var script = @"using ScriptCompiler.NetFramework.Tests;

namespace MyNamespace
{
    public static class MyClass
    {
        public static string MyFunction() { return typeof(CompilerTests).Name; }
    }
}";
            var tempPath = GetTempPath(nameof(Can_Compile_Script_With_Assembly_Reference));
            var handler = new ResolveEventHandler((_, args) => CustomResolve(args, tempPath));
            AppDomain.CurrentDomain.AssemblyResolve += handler;
            try
            {
                // Act
                var result = Sut.LoadScriptToMemory
                (
                    script,
                    "C#",
                    new[] { "ScriptCompiler.NetFramework.Tests.dll" },
                    null,
                    tempPath,
                    null
                );

                // Assert
                result.CompiledAssembly.ShouldNotBeNull();
                var myClass = result.CompiledAssembly.GetExportedTypes().FirstOrDefault(x => x.Name == "MyClass");
                myClass.ShouldNotBeNull();
                var functionResult = myClass.GetMethod("MyFunction").Invoke(null, null) as string;
                functionResult.ShouldBe("CompilerTests");
            }
            finally
            {
                AppDomain.CurrentDomain.AssemblyResolve -= handler;
            }
        }

        [Fact]
        public void Can_Compile_Script_With_Recursive_Nuget_Reference()
        {
            // Arrange
            var script = @"using CrossCutting.Data.Core;

namespace MyNamespace
{
    public static class MyClass
    {
        public static string MyFunction() { return typeof(SqlTextCommand).Name; }
    }
}";
            var packageReferences = new List<string>
            {
                "NETStandard.Library,2.0.3,.NETStandard,Version=v2.0",
                "pauldeen79.CrossCutting.Data.Core,1.1.0"
            };
            var tempPath = GetTempPath(nameof(Can_Compile_Script_With_Recursive_Nuget_Reference));
            var handler = new ResolveEventHandler((_, args) => CustomResolve(args, tempPath));
            AppDomain.CurrentDomain.AssemblyResolve += handler;
            try
            {
                // Act
                var result = Sut.LoadScriptToMemory
                (
                    script,
                    "C#",
                    Enumerable.Empty<string>(),
                    packageReferences,
                    tempPath,
                    null
                );

                // Assert
                result.CompiledAssembly.ShouldNotBeNull();
                var myClass = result.CompiledAssembly.GetExportedTypes().FirstOrDefault(x => x.Name == "MyClass");
                myClass.ShouldNotBeNull();
                var functionResult = myClass.GetMethod("MyFunction").Invoke(null, null) as string;
                functionResult.ShouldBe(@"SqlTextCommand");
            }
            finally
            {
                AppDomain.CurrentDomain.AssemblyResolve -= handler;
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
        public static string MyFunction() { return JsonConvert.SerializeObject(new { Property = 1 }); }
    }
}";
            var packageReferences = new List<string>
            {
                "NETStandard.Library,2.0.3,.NETStandard,Version=v2.0",
                "Newtonsoft.Json,13.0.1,.NETStandard,Version=v2.0"
            };
            var tempPath = GetTempPath(nameof(Can_Compile_Script_With_Newtonsoft_Json_Nuget_Reference));
            var handler = new ResolveEventHandler((_, args) => CustomResolve(args, tempPath));
            AppDomain.CurrentDomain.AssemblyResolve += handler;
            try
            {

                // Act
                var result = Sut.LoadScriptToMemory
                (
                    script,
                    "C#",
                    Enumerable.Empty<string>(),
                    packageReferences,
                    tempPath,
                    null
                );

                // Assert
                result.CompiledAssembly.ShouldNotBeNull();
                var myClass = result.CompiledAssembly.GetExportedTypes().FirstOrDefault(x => x.Name == "MyClass");
                myClass.ShouldNotBeNull();
                var functionResult = myClass.GetMethod("MyFunction").Invoke(null, null) as string;
                functionResult.ShouldBe(@"{""Property"":1}");
            }
            finally
            {
                AppDomain.CurrentDomain.AssemblyResolve -= handler;
            }
        }

        [Fact]
        public void Null_Source_Throws()
        {
            // Act & Assert
            Action a = () => Sut.LoadScriptToMemory(null, null, null, null, null, null);
            a.ShouldThrow<ArgumentNullException>()
             .ParamName.ShouldBe("source");
        }

        [Fact]
        public void Invalid_Source_Returns_Errors()
        {
            // Arrange
            var script = @"namespace MyNamespace
{
    public static class MyClass
    {
        public static string MyFunction() { return Error(); }
    }
}";

            // Act
            var result = Sut.LoadScriptToMemory
            (
                script,
                "C#",
                null,
                null,
                null,
                null
            );

            // Assert
            result.Errors.Count.ShouldBe(1);
            Action a = () => _ = result.CompiledAssembly;
            a.ShouldThrow<Exception>();
        }

        private static Assembly CustomResolve(ResolveEventArgs args, string directory)
        {
            var dllName = args.Name.Split(',')[0] + ".dll";
            var fullPath = Path.Combine(directory, dllName);
            if (File.Exists(fullPath))
            {
#pragma warning disable S3885 // "Assembly.Load" should be used
                return Assembly.LoadFrom(fullPath);
#pragma warning restore S3885 // "Assembly.Load" should be used
            }

            return null;
        }
    }
}
