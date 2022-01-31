namespace ScriptCompiler;

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
    /// <returns>
    /// The compiler results, or null when source is null.
    /// </returns>
    public CompilerResults LoadScriptToMemory(string source,
                                              IEnumerable<string> referencedAssemblies,
                                              IEnumerable<string> packageReferences,
                                              string? tempPath,
                                              string? nugetPackageSourceUrl,
                                              AssemblyLoadContext? customAssemblyLoadContext)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, options: new CSharpParseOptions().WithPreprocessorSymbols("TRACE"));
        var references = new List<MetadataReference>();

        AddAssemblyReferences(referencedAssemblies, references);
        AddPackageReferences(packageReferences, tempPath, nugetPackageSourceUrl, references);

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
                                             string? tempPath,
                                             string? nugetPackageSourceUrl,
                                             List<MetadataReference> references)
    {
        tempPath = string.IsNullOrEmpty(tempPath)
            ? Path.GetTempPath()
            : tempPath;

        nugetPackageSourceUrl = string.IsNullOrEmpty(nugetPackageSourceUrl)
            ? "https://api.nuget.org/v3/index.json"
            : nugetPackageSourceUrl;

        foreach (string reference in packageReferences)
        {
            if (!AddPackageReference(reference, references, tempPath, nugetPackageSourceUrl))
            {
                throw new ArgumentException($"Adding package reference [{reference}] failed", nameof(packageReferences));
            }
        }
    }

    private static bool AddPackageReference(string reference,
                                            ICollection<MetadataReference> references,
                                            string? tempPath,
                                            string? nugetPackageSourceUrl)
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
        if (!AddDependencies(packageReader, framework, references, tempPath, nugetPackageSourceUrl))
        {
            return false;
        }

        foreach (var item in GetItems(packageReader, framework.GetShortFolderName()))
        {
            var filename = item.Split('/').Last();
            AddItemToReferences(reference, references, tempPath, logger, packageReader, item, filename);
        }

        return true;
    }

    private static void AddItemToReferences(string reference,
                                            ICollection<MetadataReference> references,
                                            string? tempPath,
                                            ILogger logger,
                                            PackageArchiveReader packageReader,
                                            string item,
                                            string filename)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && RuntimeProvidedAssemblies.IsAssemblyProvidedByRuntime(filename))
        {
            // No need to store dll's that are provided by the run-time...
            if (IsNetStandardReference(reference, out var version, out var product)
                && File.Exists(Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "sdk", "NuGetFallbackFolder", "netstandard.library", version, "build", product, "ref"), filename)))
            {
                references.Add(MetadataReference.CreateFromFile(Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "sdk", "NuGetFallbackFolder", "netstandard.library", version, "build", product, "ref"), filename)));
            }
            else
            {
                references.Add(MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location), filename)));
            }
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

    private static bool IsNetStandardReference(string reference, out string version, out string product)
    {
        version = string.Empty;
        product = string.Empty;
        if (reference != "NETStandard.Library,2.0.3,.NETStandard,Version=v2.0")
        {
            return false;
        }

        version = "2.0.3";
        product = "netstandard2.0";
        return true;
    }

    private static bool AddDependencies(PackageArchiveReader packageReader,
                                        NuGet.Frameworks.NuGetFramework framework,
                                        ICollection<MetadataReference> references,
                                        string? tempPath,
                                        string? nugetPackageSourceUrl)
    {
        if (framework == null)
        {
            return false;
        }

        foreach (var dependency in GetDependencies(packageReader, framework))
        {
            var fullReferenceName = $"{dependency.Id},{dependency.VersionRange.MinVersion},{framework.DotNetFrameworkName}";
            var shortReferenceName = $"{dependency.Id},{dependency.VersionRange.MinVersion}";
            if (!AddPackageReference(fullReferenceName, references, tempPath, nugetPackageSourceUrl)
                && !AddPackageReference(shortReferenceName, references, tempPath, nugetPackageSourceUrl))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAssembly(string filename)
        => filename != "_._" && !filename.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && !filename.EndsWith(".targets", StringComparison.OrdinalIgnoreCase);

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

    private static CompilerResults Compile(CSharpCompilation compilation, AssemblyLoadContext? customAssemblyLoadContext)
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
