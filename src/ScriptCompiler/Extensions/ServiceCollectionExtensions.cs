namespace ScriptCompiler.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddScriptCompiler(this IServiceCollection instance)
        => instance.AddSingleton<IScriptCompiler, ScriptCompiler>();
}
