using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NuGet.Common;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace ScriptCompiler
{
    /// <summary>
    /// Class for compiling code to an in-memory assembly.
    /// </summary>
    public class ScriptCompiler : IScriptCompiler
    {
        /// <summary>
        /// Compiles the input string and saves it in memory.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <param name="referencedAssemblies">The referenced assemblies.</param>
        /// <param name="packageReferences">Package references.</param>
        /// <param name="tempPath">Optional temporary path for extracting assemblies from package references. When not provided, the default temporary directory is used.</param>
        /// <param name="nugetPackageSourceUrl">Optional package source url for nuget packages. When not provided, nuget.org will be used.</param>
        /// <param name="customAssemblyLoadContext">Optional custom assembly load context.</param>
        /// <param name="allowOptimalization">Boolean to determine whether framework assemblies need to be skipped when writing assemblies from package references.</param>
        /// <returns>
        /// The compiler results, or null when source is null.
        /// </returns>
        public CompilerResults LoadScriptToMemory(string source,
                                                  IEnumerable<string> referencedAssemblies,
                                                  IEnumerable<string> packageReferences,
                                                  string tempPath,
                                                  string nugetPackageSourceUrl,
                                                  AssemblyLoadContext customAssemblyLoadContext,
                                                  bool allowOptimalization)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var syntaxTree = CSharpSyntaxTree.ParseText(source, options: new CSharpParseOptions().WithPreprocessorSymbols("TRACE"));
            var references = new List<MetadataReference>();

            AddAssemblyReferences(referencedAssemblies, references);
            AddPackageReferences(packageReferences, tempPath, nugetPackageSourceUrl, allowOptimalization, references);

            var compilation = CSharpCompilation.Create
            (
                $"ScriptAssembly{DateTime.Now.Ticks}",
                syntaxTrees: new[] { syntaxTree },
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                            .WithOptimizationLevel(OptimizationLevel.Debug)
                            .WithPlatform(Platform.AnyCpu)
            );

            return Compile(compilation, customAssemblyLoadContext);
        }

        private static void AddAssemblyReferences(IEnumerable<string> referencedAssemblies, List<MetadataReference> references)
        {
            if (referencedAssemblies == null)
            {
                return;
            }
            
            foreach (string reference in referencedAssemblies)
            {
                if (reference.Contains(","))
                {
                    references.Add(MetadataReference.CreateFromFile(reference.Split(',')[0] + ".dll"));
                }
                else
                {
                    references.Add(MetadataReference.CreateFromFile(reference));
                }
            }
        }

        private static void AddPackageReferences(IEnumerable<string> packageReferences,
                                                 string tempPath,
                                                 string nugetPackageSourceUrl,
                                                 bool allowOptimalization,
                                                 List<MetadataReference> references)
        {
            if (packageReferences == null)
            {
                return;
            }

            tempPath = string.IsNullOrEmpty(tempPath)
                ? Path.GetTempPath()
                : tempPath;

            nugetPackageSourceUrl = string.IsNullOrEmpty(nugetPackageSourceUrl)
                ? "https://api.nuget.org/v3/index.json"
                : nugetPackageSourceUrl;

            foreach (string reference in packageReferences)
            {
                if (!AddPackageReference(reference, references, tempPath, nugetPackageSourceUrl, allowOptimalization))
                {
                    throw new ArgumentException($"Adding package reference [{reference}] failed", nameof(packageReferences));
                }
            }
        }

        private static bool AddPackageReference(string reference,
                                                ICollection<MetadataReference> references,
                                                string tempPath,
                                                string nugetPackageSourceUrl,
                                                bool allowOptimalization)
        {
            var split = reference.Split(',');
            if (split.Length < 2)
            {
                return false;
            }

            var logger = NullLogger.Instance;
            var cancellationToken = CancellationToken.None;

            var cache = new SourceCacheContext();
            var repository = Repository.Factory.GetCoreV3(nugetPackageSourceUrl);
            var resource = repository.GetResourceAsync<FindPackageByIdResource>().Result;

            var packageVersion = new NuGetVersion(split[1].Trim());
            using var packageStream = new MemoryStream();

            var success = resource.CopyNupkgToStreamAsync(
                split[0].Trim(),
                packageVersion,
                packageStream,
                cache,
                logger,
                cancellationToken).Result;

            if (!success)
            {
                return false;
            }

            using var packageReader = new PackageArchiveReader(packageStream);
            var framework = GetFramework(split, packageReader);
            if (!AddDependencies(packageReader, framework, references, tempPath, nugetPackageSourceUrl, allowOptimalization))
            {
                return false;
            }

            foreach (var item in GetItems(packageReader, framework.GetShortFolderName()))
            {
                var filename = item.Split('/').Last();
                if (allowOptimalization && RuntimeProvidedAssemblies.IsAssemblyProvidedByRuntime(filename))
                {
                    // No need to store dll's that are provided by the run-time...
                    references.Add(MetadataReference.CreateFromFile(filename));
                }
                else
                {
                    var tempFilePath = Path.Combine(tempPath, filename);
                    if (!File.Exists(tempFilePath) && IsAssembly(filename))
                    {
                        packageReader.ExtractFile(item, tempFilePath, logger);
                    }
                    if (item.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        references.Add(MetadataReference.CreateFromFile(Path.Combine(tempPath, filename)));
                    }
                }
            }

            return true;
        }

        private static bool AddDependencies(PackageArchiveReader packageReader,
                                            NuGet.Frameworks.NuGetFramework framework,
                                            ICollection<MetadataReference> references,
                                            string tempPath,
                                            string nugetPackageSourceUrl,
                                            bool allowOptimalization)
        {
            if (framework == null)
            {
                return false;
            }

            foreach (var dependency in GetDependencies(packageReader, framework))
            {
                var fullReferenceName = $"{dependency.Id},{dependency.VersionRange.MinVersion},{framework.DotNetFrameworkName}";
                var shortReferenceName = $"{dependency.Id},{dependency.VersionRange.MinVersion}";
                if (!AddPackageReference(fullReferenceName, references, tempPath, nugetPackageSourceUrl, allowOptimalization)
                    && !AddPackageReference(shortReferenceName, references, tempPath, nugetPackageSourceUrl, allowOptimalization))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsAssembly(string filename)
            => filename != "_._" && !filename.EndsWith(".xml") && !filename.EndsWith(".targets");

        private static IEnumerable<PackageDependency> GetDependencies(PackageArchiveReader packageReader,
                                                                      NuGet.Frameworks.NuGetFramework framework)
            => packageReader.GetPackageDependencies().FirstOrDefault(x => x.TargetFramework == framework)?.Packages ?? Enumerable.Empty<PackageDependency>();

        private static NuGet.Frameworks.NuGetFramework GetFramework(string[] split, PackageArchiveReader packageReader)
            => packageReader.GetSupportedFrameworks().FirstOrDefault(x => split.Length < 3 || x.DotNetFrameworkName == string.Join(",", split.Skip(2)));

        private static string[] GetItems(PackageArchiveReader packageReader, string shortFolderName)
        {
            var items = packageReader.GetFiles($"lib/{shortFolderName}").ToArray();
            if (items.Length == 0)
            {
                items = packageReader.GetFiles($"build/{shortFolderName}").ToArray();
            }

            return items;
        }

        private static CompilerResults Compile(CSharpCompilation compilation, AssemblyLoadContext customAssemblyLoadContext)
        {
            using var ms = new MemoryStream();
            var result = compilation.Emit(ms);

            CompilerResults returnValue;
            if (!result.Success)
            {
                returnValue = new CompilerResults(compilation, result.Diagnostics, false, null);
            }
            else
            {
                ms.Seek(0, SeekOrigin.Begin);
                var assembly = (customAssemblyLoadContext ?? AssemblyLoadContext.Default).LoadFromStream(ms);
                returnValue = new CompilerResults(compilation, result.Diagnostics, true, assembly);
            }

            return returnValue;
        }
    }
}
