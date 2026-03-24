using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Seevalocal.Config.Loading;
using Seevalocal.Config.Merging;
using Seevalocal.Config.Validation;
using Seevalocal.Core.Models;
using Seevalocal.Core.Pipeline;
using Seevalocal.DataSources;
using Seevalocal.Pipelines;
using Seevalocal.Pipelines.Factories;
using Seevalocal.Server;
using Seevalocal.UI.Services;
using Seevalocal.UI.ViewModels;
using Serilog;

namespace Seevalocal.UI;

public partial class App : Application
{
    private IServiceProvider? _serviceProvider;

    /// <summary>
    /// Gets the service provider for the application.
    /// </summary>
    public IServiceProvider? Services => _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Set up DI container
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();

            // Create MainWindow with DI
            var mainWindow = new MainWindow
            {
                DataContext = _serviceProvider.GetRequiredService<MainWindowViewModel>()
            };

            // Wire up wizard button callbacks
            if (mainWindow.DataContext is MainWindowViewModel vm)
            {
                vm.WizardState.OnExportScript = () =>
                {
                    // Fire-and-forget async save flow so we can keep the Action signature.
                    _ = SaveScriptAsync();
                };

                vm.WizardState.OnStartRun = vm.StartRunAsync;

                // Wire up toast notifications
                var toastService = _serviceProvider.GetRequiredService<IToastService>();
                vm.WizardState.OnShowNotification = msg => toastService.Show(msg);
            }

            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();


        // Local async helper: generates script, shows save dialog, writes file and notifies user / logs.
        async Task SaveScriptAsync()
        {
            try
            {
                if (_serviceProvider == null) return;
                if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

                if (desktop.MainWindow?.DataContext is not MainWindowViewModel vm) return;

                var shellTarget = vm.WizardState.ShellTarget ?? ShellTarget.Bash;
                var script = vm.ExportScript(shellTarget);

                var filePicker = _serviceProvider.GetRequiredService<IFilePickerService>();
                var toastService = _serviceProvider.GetRequiredService<IToastService>();

                // Choose sensible default file name & filters based on shell target
                var isPowerShell = shellTarget == ShellTarget.PowerShell;

                var runName = vm.WizardState.RunName;
                if (string.IsNullOrWhiteSpace(runName)) runName = "seevalocal";
                var suggestedName = isPowerShell ? $"{runName}.ps1" : $"{runName}.sh";

                var filters = isPowerShell
                    ? "PowerShell Script|*.ps1|All Files|*.*"
                    : "Shell Script|*.sh|All Files|*.*";

                var path = await filePicker.ShowSaveFileDialogAsync("Save Exported Script", filters, suggestedName);
                if (path == null) return;

                await File.WriteAllTextAsync(path, script);

                Log.Information("Export script saved to {Path}", path);
                toastService.ShowSuccess($"Script saved to {path}");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save exported script");
                if (_serviceProvider != null)
                {
                    var toastService = _serviceProvider.GetRequiredService<IToastService>();
                    toastService.ShowError($"Failed to save script: {ex.Message}");
                }
            }
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Logging
        _ = services.AddLogging(static b => b.AddSerilog());

        // Config services
        _ = services.AddSingleton<SettingsFileLoader>();
        _ = services.AddSingleton<ConfigurationMerger>();
        _ = services.AddSingleton<ConfigValidator>();
        _ = services.AddSingleton<IConfigurationService, DefaultConfigurationService>();

        // Pipeline registry - register all built-in pipeline factories
        _ = services.AddSingleton<IBuiltinPipelineFactory>(sp =>
            new TranslationPipelineFactory(sp.GetRequiredService<ILoggerFactory>()));
        _ = services.AddSingleton<IBuiltinPipelineFactory>(sp =>
            new CSharpCodingPipelineFactory(sp.GetRequiredService<ILoggerFactory>()));
        _ = services.AddSingleton<IBuiltinPipelineFactory>(sp =>
            new CasualQAPipelineFactory(sp.GetRequiredService<ILoggerFactory>()));
        _ = services.AddSingleton<PipelineRegistry>();

        // Data source factory
        _ = services.AddSingleton<DataSourceFactory>();

        // Server lifecycle services (for managing llama-server processes)
        _ = services.AddSingleton<LlamaServerArgBuilder>();
        _ = services.AddSingleton<GpuDetector>();
        _ = services.AddSingleton<LlamaServerDownloader>();
        _ = services.AddSingleton(_ => new HttpClient() { Timeout = TimeSpan.FromHours(6) });
        _ = services.AddSingleton<LlamaServerManager>();
        _ = services.AddSingleton<IServerLifecycleService, DefaultServerLifecycleService>();

        // Runner service (UI version uses logging instead of console)
        _ = services.AddSingleton<IRunnerService, DefaultRunnerService>();

        // File picker service with dialog directory persistence
        _ = services.AddSingleton<IDialogDirectoryService, DialogDirectoryService>();
        _ = services.AddSingleton<IFilePickerService>(sp =>
            new DefaultFilePickerService(
                null,
                sp.GetRequiredService<IDialogDirectoryService>()));

        // Toast notification service
        _ = services.AddSingleton<IToastService, ToastService>();

        // Shell Script Exporter
        _ = services.AddSingleton<IShellScriptExporter, ShellScriptExporterWrapper>();

        // ViewModels
        _ = services.AddSingleton<IWizardViewModel>(static sp => new WizardViewModel(sp.GetRequiredService<IFilePickerService>(), sp.GetRequiredService<IToastService>()));
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
        _ = services.AddSingleton(sp =>
        {
            var evalGenService = sp.GetRequiredService<IEvalGenService>();
            var logger = sp.GetRequiredService<ILogger<EvalGenRunViewModel>>();
            return new EvalGenRunViewModel(evalGenService, logger);
        });
        _ = services.AddSingleton(sp =>
        {
            var evalGenService = sp.GetRequiredService<IEvalGenService>();
            var runViewModel = sp.GetRequiredService<EvalGenRunViewModel>();
            var filePicker = sp.GetService<IFilePickerService>();
            var logger = sp.GetRequiredService<ILogger<EvalGenViewModel>>();
            // Factory function to get judge config from MainWindowViewModel (resolved at call time, avoiding circular dependency)
            // Uses ResolveCurrentConfigExcludingWizard() to avoid including WizardState settings
            JudgeConfig? getJudgeConfig()
            {
                var mainWindowViewModel = sp.GetRequiredService<MainWindowViewModel>();
                var result = mainWindowViewModel.ResolveCurrentConfig(false);
                return result.IsSuccess ? result.Value.Judge : null;
            }
            return new EvalGenViewModel(evalGenService, runViewModel, filePicker, logger, getJudgeConfig);
        });
        _ = services.AddSingleton<MainWindowViewModel>();
    }
}
