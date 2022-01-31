namespace ScriptCompiler;

public interface IScriptCompiler
{
    CompilerResults LoadScriptToMemory(string source,
                                       IEnumerable<string> referencedAssemblies,
                                       IEnumerable<string> packageReferences,
                                       string? tempPath,
                                       string? nugetPackageSourceUrl,
                                       AssemblyLoadContext? customAssemblyLoadContext);
}
