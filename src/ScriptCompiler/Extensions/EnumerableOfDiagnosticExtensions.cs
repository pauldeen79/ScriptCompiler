using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;

namespace ScriptCompiler.Extensions
{
    public static class EnumerableOfDiagnosticExtensions
    {
        public static bool HasErrors(this IEnumerable<Diagnostic> instance) =>
            instance.Any(diagnostic =>
            diagnostic.IsWarningAsError
            || diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
