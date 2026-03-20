using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Seevalocal.Config.Loading;
using Seevalocal.Config.Merging;
using Seevalocal.Config.Validation;
using Seevalocal.Core;
using Seevalocal.Core.Models;
using Seevalocal.Core.Pipeline;
using Seevalocal.Pipelines.Factories;
using Seevalocal.Server;
using Seevalocal.Server.Models;
using Seevalocal.UI.Commands;
using Seevalocal.UI.Services;
using Serilog;
using Spectre.Console.Cli;

namespace Seevalocal.UI;

internal class Program
{

    [STAThread] // An async main method can't be STAThread, and COM access (like to the clipboard) doesn't work without STAThread.
    private static void Main(string[] args)
    {
        // Call the asynchronous method synchronously on the STA thread
        MainAsync(args).GetAwaiter().GetResult();
    }

    private static async Task<int> MainAsync(string[] args)
    {
        // Check for monitor process mode (used for Unix process cleanup)
        if (args.Length >= 3 && args[0] == "--monitor-process")
        {
            // Running as a monitor process - don't initialize full app
            if (int.TryParse(args[1], out int parentPid) && int.TryParse(args[2], out int childPid))
            {
                var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                };

                await ProcessCleanupMonitor.RunAsync(parentPid, childPid, cts.Token);
                return 0;
            }
            return 1;
        }

        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(Path.Combine("logs", "seevalocal-.log"),
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                rollingInterval: RollingInterval.Day)
            .CreateLogger();

        try
        {
            // Check if running with CLI arguments
            if (args.Length > 0)
            {
                // Run as CLI
                return await RunCliAsync(args);
            }
            else
            {
                // Run as UI
                return RunUi(args);
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Unhandled exception at startup");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }

        static async Task<int> RunCliAsync(string[] args)
        {
            var services = new ServiceCollection();
            _ = services.AddLogging(static b => b.AddSerilog(dispose: true));

            // Register Config services
            _ = services.AddSingleton<SettingsFileLoader>();
            _ = services.AddSingleton<ConfigurationMerger>();
            _ = services.AddSingleton<ConfigValidator>();
            _ = services.AddSingleton<IConfigurationService, DefaultConfigurationService>();

            // Register Server services
            _ = services.AddSingleton<LlamaServerArgBuilder>();
            _ = services.AddSingleton<LlamaServerDownloader>();
            _ = services.AddSingleton<GpuDetector>();
            // No timeout - LLM inference can take hours on local machines
            _ = services.AddSingleton(static () => new HttpClient() { Timeout = TimeSpan.FromHours(6) });
            // Transient - each server gets its own manager instance
            _ = services.AddTransient<LlamaServerManager>();
            _ = services.AddSingleton<IServerLifecycleService, DefaultServerLifecycleService>();

            // Register Runner service
            _ = services.AddSingleton<IRunnerService, DefaultRunnerService>();

            // Register Shell Script Exporter
            _ = services.AddSingleton<IShellScriptExporter, ShellScriptExporterWrapper>();

            // Register pipeline factories
            _ = services.AddSingleton<IBuiltinPipelineFactory, TranslationPipelineFactory>();
            _ = services.AddSingleton<IBuiltinPipelineFactory, CSharpCodingPipelineFactory>();
            _ = services.AddSingleton<IBuiltinPipelineFactory, CasualQAPipelineFactory>();

            // Register commands
            _ = services.AddTransient<RunCommand>();
            _ = services.AddTransient<ValidateCommand>();
            _ = services.AddTransient<ExportScriptCommand>();
            _ = services.AddTransient<ServerStartCommand>();
            _ = services.AddTransient<ServerCheckCommand>();
            _ = services.AddTransient<PipelineListCommand>();
            _ = services.AddTransient<EvalGenCommand>();

            // Register eval gen services
            _ = services.AddSingleton<IEvalGenService>(sp =>
            {
                var serverManager = sp.GetRequiredService<LlamaServerManager>();
                var downloader = sp.GetRequiredService<LlamaServerDownloader>();
                var gpuDetector = sp.GetRequiredService<GpuDetector>();
                var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                var httpClient = sp.GetRequiredService<HttpClient>();
                var logger = sp.GetRequiredService<ILogger<EvalGenService>>();
                return new EvalGenService(serverManager, downloader, gpuDetector, loggerFactory, httpClient, logger);
            });

            var registrar = new TypeRegistrar(services);
            var app = new CommandApp(registrar);

            app.Configure(static config =>
            {
                _ = config.SetApplicationName("seevalocal");
                _ = config.SetApplicationVersion(typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0");

                _ = config.AddCommand<RunCommand>("run")
                    .WithDescription("Run an evaluation pipeline");

                _ = config.AddCommand<ValidateCommand>("validate")
                    .WithDescription("Validate a settings file without running");

                _ = config.AddCommand<ExportScriptCommand>("export-script")
                    .WithDescription("Export a shell script from a settings file");

                _ = config.AddCommand<EvalGenCommand>("eval-gen")
                    .WithDescription("Generate an evaluation set agentically using the judge LLM");

                _ = config.AddBranch("server", static server =>
                {
                    server.SetDescription("Server lifecycle commands");
                    _ = server.AddCommand<ServerStartCommand>("start")
                        .WithDescription("Start llama-server (for debugging)");
                    _ = server.AddCommand<ServerCheckCommand>("check")
                        .WithDescription("Check if a URL is a healthy llama-server");
                });

                _ = config.AddBranch("pipeline", static pipeline =>
                {
                    pipeline.SetDescription("Pipeline management commands");
                    _ = pipeline.AddCommand<PipelineListCommand>("list")
                        .WithDescription("List registered pipeline names and descriptions");
                });
            });

            return await app.RunAsync(args);
        }

        static int RunUi(string[] args)
        {
            var builder = BuildAvaloniaApp();
            _ = builder.StartWithClassicDesktopLifetime(args);
            return 0;
        }

        static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
        }
    }
}

file class TypeRegistrar(IServiceCollection services) : ITypeRegistrar
{
    private readonly IServiceCollection _services = services;

    public ITypeResolver Build() => new TypeResolver(_services.BuildServiceProvider());
    public void Register(Type service, Type implementation) => _services.AddSingleton(service, implementation);
    public void RegisterInstance(Type service, object implementation) => _services.AddSingleton(service, implementation);
    public void RegisterLazy(Type service, Func<object> factory) => _services.AddSingleton(service, _ => factory());
}

file class TypeResolver(IServiceProvider provider) : ITypeResolver, IDisposable
{
    private readonly IServiceProvider _provider = provider;

    public object? Resolve(Type? type) => type != null ? _provider.GetService(type) : null;
    public void Dispose() => (_provider as IDisposable)?.Dispose();
}