namespace ScriptCompiler;

public record CompilerResults
{
    public CSharpCompilation Compilation { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
    public bool IsSuccessful { get; }
    public Assembly? CompiledAssembly { get; }

    public IEnumerable<Diagnostic> Errors => Diagnostics.Where(diagnostic
        => diagnostic.IsWarningAsError || diagnostic.Severity == DiagnosticSeverity.Error);

    public CompilerResults(CSharpCompilation compilation,
                           ImmutableArray<Diagnostic> diagnostics,
                           bool isSuccessful,
                           Assembly? compiledAssembly)
    {
        Compilation = compilation;
        Diagnostics = diagnostics;
        IsSuccessful = isSuccessful;
        CompiledAssembly = compiledAssembly;
    }
}
