using CreatioAutoTests.Analysis;
using CreatioAutoTests.Commands;
using CreatioAutoTests.Configuration;
using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend.Registry;
using CreatioAutoTests.Session;
using CreatioAutoTests.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace CreatioAutoTests.Runner;

/// <summary>Composition root of the framework.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything needed to run .act files from <paramref name="rootDirectory"/>.
    /// Both registries are built by scanning assemblies, so a project can supply its own commands
    /// and element types by passing its assembly in <paramref name="extraAssemblies"/>.
    /// </summary>
    public static IServiceCollection AddCreatioAutoTests(
        this IServiceCollection services,
        string rootDirectory,
        ConfigurationPaths? paths = null,
        params System.Reflection.Assembly[] extraAssemblies)
    {
        ArgumentNullException.ThrowIfNull(services);

        var assemblies = new List<System.Reflection.Assembly> { typeof(ServiceCollectionExtensions).Assembly };
        assemblies.AddRange(extraAssemblies);

        var elementRegistry = UiElementRegistry.Discover(assemblies.ToArray());
        var commandRegistry = CommandRegistry.Discover(assemblies.ToArray());
        var configuration = FrameworkConfiguration.Load(rootDirectory, elementRegistry, paths);

        services.AddSingleton(elementRegistry);
        services.AddSingleton(commandRegistry);
        services.AddSingleton(configuration);
        services.AddSingleton(configuration.Environment);
        services.AddSingleton(configuration.Site);
        services.AddSingleton(configuration.RunSettings);
        services.AddSingleton(configuration.Catalog);

        services.AddSingleton(paths ?? new ConfigurationPaths());
        services.AddSingleton<UiElementFactory>();
        services.AddSingleton<PageAnalyzer>();
        services.AddSingleton<ClassicPageAnalyzer>();
        services.AddSingleton<PageConfigWriter>();
        services.AddSingleton<PageAnalysisService>();
        services.AddSingleton<CreatioODataClient>();
        services.AddSingleton<SchemaDiscoveryService>();
        services.AddSingleton<ValueCompatibilityPolicy>();
        services.AddSingleton<ScriptParser>();
        services.AddSingleton<ScriptValidator>();
        services.AddSingleton<CommandExecutor>();
        services.AddSingleton<CreatioAuthenticator>();
        services.AddSingleton<TestRunner>();
        RegisterReportWriters(services, configuration.RunSettings);

        RegisterDiscoveredTypes(services, commandRegistry);

        return services;
    }

    /// <summary>
    /// Commands and validators are resolved through the container so they can take dependencies.
    /// Registering them explicitly keeps a missing dependency a startup error, not a mid-run one.
    /// </summary>
    private static void RegisterDiscoveredTypes(IServiceCollection services, CommandRegistry commands)
    {
        foreach (var registration in commands.Registrations)
        {
            services.AddTransient(registration.ImplementationType);

            if (services.All(d => d.ServiceType != registration.ValidatorType))
            {
                services.AddTransient(registration.ValidatorType);
            }
        }
    }

    private static void RegisterReportWriters(IServiceCollection services, RunSettings settings)
    {
        var formats = settings.Reports.Select(format => format.ToLowerInvariant()).Distinct().ToArray();
        var unknown = formats.Where(format => format is not ("text" or "junit" or "html")).ToArray();
        if (unknown.Length > 0)
        {
            throw new InvalidOperationException("Unknown report format(s): " + string.Join(", ", unknown) + ". Supported: text, junit, html.");
        }

        if (formats.Contains("text")) services.AddSingleton<TextReportWriter>();
        if (formats.Contains("junit")) services.AddSingleton<JUnitReportWriter>();
        if (formats.Contains("html")) services.AddSingleton<HtmlReportWriter>();
        services.AddSingleton<IReportWriter>(provider => new CompositeReportWriter(formats.Select(format => (IReportWriter)(format switch
        {
            "text" => provider.GetRequiredService<TextReportWriter>(),
            "junit" => provider.GetRequiredService<JUnitReportWriter>(),
            _ => provider.GetRequiredService<HtmlReportWriter>()
        })).ToArray()));
    }
}
