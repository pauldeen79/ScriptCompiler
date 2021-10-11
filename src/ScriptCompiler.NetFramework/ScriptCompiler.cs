using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using NuGet.Common;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace ScriptCompiler.NetFramework
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
        /// <param name="packageReferences">The package references.</param>
        /// <param name="language">The language.</param>
        /// <param name="tempPath">Optional temporary path for extracting assemblies from package references. When not provided, the default temporary directory is used.</param>
        /// <param name="nugetPackageSourceUrl">Optional package source url for nuget packages. When not provided, nuget.org will be used.</param>
        /// <returns>
        /// The compiler results, or null when source is null.
        /// </returns>
        public CompilerResults LoadScriptToMemory(string source,
                                                  string language,
                                                  IEnumerable<string> referencedAssemblies,
                                                  IEnumerable<string> packageReferences,
                                                  string tempPath,
                                                  string nugetPackageSourceUrl)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var parameters = new CompilerParameters
            {
                GenerateInMemory = true,
                GenerateExecutable = false,
                IncludeDebugInformation = true,
                CompilerOptions = "/d:TRACE" //important to allow tracing
            };

            AddAssemblyReferences(referencedAssemblies, parameters);
            AddPackageReferences(referencedAssemblies, packageReferences, tempPath, nugetPackageSourceUrl, parameters);

            return Compile(parameters, source, language);
        }

        private static void AddAssemblyReferences(IEnumerable<string> referencedAssemblies, CompilerParameters parameters)
        {
            if (referencedAssemblies == null)
            {
                return;
            }
            
            foreach (string reference in referencedAssemblies)
            {
                parameters.ReferencedAssemblies.Add(reference);
            }
        }

        private static void AddPackageReferences(IEnumerable<string> referencedAssemblies,
                                                 IEnumerable<string> packageReferences,
                                                 string tempPath,
                                                 string nugetPackageSourceUrl,
                                                 CompilerParameters parameters)
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
                if (!AddNugetReference(reference, parameters.ReferencedAssemblies, referencedAssemblies, tempPath, nugetPackageSourceUrl))
                {
                    throw new ArgumentException($"Adding package reference [{reference}] failed", nameof(packageReferences));
                }
            }

            if (packageReferences.Any(x => x.Equals("NETStandard.Library,2.0.3,.NETStandard,Version=v2.0", StringComparison.OrdinalIgnoreCase)))
            {
                var netstandard = Assembly.Load("netstandard, Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51");
                parameters.ReferencedAssemblies.Add(netstandard.Location);
            }
        }

        private static bool AddNugetReference(string reference,
                                              StringCollection referencedAssemblies,
                                              IEnumerable<string> originalReferencedAssemblies,
                                              string tempPath,
                                              string nugetPackageSourceUrl)
        {
            if (reference == "NETStandard.Library,2.0.3,.NETStandard,Version=v2.0")
            {
                //Skip .NET standard (netstandard.dll) and dependencies on .NET framework, when already added in original referenced assemblies
                return true;
            }
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
            using (var packageStream = new MemoryStream())
            {
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

                using (var packageReader = new PackageArchiveReader(packageStream))
                {
                    var framework = packageReader.GetSupportedFrameworks().FirstOrDefault(x => split.Length < 3 || x.DotNetFrameworkName == string.Join(",", split.Skip(2)));
                    if (framework == null)
                    {
                        return false;
                    }
                    var shortFolderName = framework.GetShortFolderName();
                    var dependencies = packageReader.GetPackageDependencies().FirstOrDefault(x => x.TargetFramework == framework)?.Packages ?? Enumerable.Empty<PackageDependency>();
                    foreach (var dependency in dependencies)
                    {
                        var fullReferenceName = $"{dependency.Id},{dependency.VersionRange.MinVersion},{framework.DotNetFrameworkName}";
                        var shortReferenceName = $"{dependency.Id},{dependency.VersionRange.MinVersion}";
                        if (!AddNugetReference(fullReferenceName, referencedAssemblies, originalReferencedAssemblies, tempPath, nugetPackageSourceUrl)
                            && !AddNugetReference(shortReferenceName, referencedAssemblies, originalReferencedAssemblies, tempPath, nugetPackageSourceUrl))
                        {
                            return false;
                        }
                    }
                    var items = packageReader.GetFiles($"lib/{shortFolderName}").ToArray();
                    if (items.Length == 0)
                    {
                        items = packageReader.GetFiles($"build/{shortFolderName}").ToArray();
                    }
                    foreach (var item in items.Where(x => !x.EndsWith("mscorlib.dll")))
                    {
                        var dllName = item.Split('/').Last();
                        if (originalReferencedAssemblies.Any(x => x.Split('\\').Last().Equals(dllName, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        var tempFilePath = Path.Combine(tempPath, dllName);
                        if (!File.Exists(tempFilePath))
                        {
                            packageReader.ExtractFile(item, tempFilePath, logger);
                        }
                        if (item.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        {
                            referencedAssemblies.Add(Path.Combine(tempPath, dllName));
                        }
                    }

                    return true;
                }
            }
        }

        private static CompilerResults Compile(CompilerParameters parameters, string source, string language)
        {
            using (var compiler = CodeDomProvider.CreateProvider(language))
            {
                return compiler.CompileAssemblyFromSource(parameters, source);
            }
        }
    }
}
