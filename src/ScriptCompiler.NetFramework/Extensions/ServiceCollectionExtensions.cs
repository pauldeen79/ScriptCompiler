using Microsoft.Extensions.DependencyInjection;

namespace ScriptCompiler.NetFramework.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddScriptCompiler(this IServiceCollection instance)
            => instance.AddSingleton<IScriptCompiler, ScriptCompiler>();
    }
}
