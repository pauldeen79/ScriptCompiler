using System.CodeDom.Compiler;
using System.Collections.Generic;

namespace ScriptCompiler.NetFramework
{
    public interface IScriptCompiler
    {
        CompilerResults LoadScriptToMemory(string source,
                                           string language,
                                           IEnumerable<string> referencedAssemblies,
                                           IEnumerable<string> packageReferences,
                                           string tempPath,
                                           string nugetPackageSourceUrl);
    }
}
