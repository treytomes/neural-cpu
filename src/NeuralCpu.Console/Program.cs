using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NeuralCpu.Core;
using NeuralCpu.Core.Components;
using NeuralCpu.Core.Configuration;
using NeuralCpu.Core.Events;
using NeuralCpu.Network;
using Serilog;
using Serilog.Events;
using Terminal.Gui;

namespace NeuralCpu.Console;

internal class Program
{
    static void Main(string[] args)
    {
        try
        {
            var builder = Host.CreateApplicationBuilder(args);

            // Add YAML configuration support
            builder.Configuration.AddYamlFile("appsettings.yaml", optional: false, reloadOnChange: true);

            // Bind configuration to options
            var neuralCpuOptions = new NeuralCpuOptions();
            builder.Configuration.Bind(neuralCpuOptions);
            builder.Services.Configure<NeuralCpuOptions>(builder.Configuration);

            // Configure Serilog from options
            var loggingOptions = neuralCpuOptions.Logging;
            var logLevel = Enum.Parse<LogEventLevel>(loggingOptions.MinimumLevel, ignoreCase: true);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Is(logLevel)
                .WriteTo.Console()
                .WriteTo.File(
                    path: loggingOptions.FilePath,
                    rollingInterval: RollingInterval.Day,
                    rollOnFileSizeLimit: true,
                    fileSizeLimitBytes: loggingOptions.FileSizeLimitMB * 1024 * 1024,
                    retainedFileCountLimit: loggingOptions.RetainedFileCountLimit,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            // Add Serilog
            builder.Services.AddSerilog();

            // Register Neural CPU core services with configuration
            builder.Services.AddNeuralCpu(neuralCpuOptions);

            // Register Network components
            builder.Services.AddSingleton(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<MockNetwork>>();
                var options = neuralCpuOptions.Network.TaskRunner;
                return new MockNetwork(logger, options);
            });

            builder.Services.AddSingleton(sp =>
            {
                var registerFile = sp.GetRequiredService<RegisterFile>();
                var stack = sp.GetRequiredService<Stack>();
                var memory = sp.GetRequiredService<Memory>();
                var stateVectorOptions = neuralCpuOptions.Network.StateVector;
                var taskRunnerOptions = neuralCpuOptions.Network.TaskRunner;
                return new StateVectorBuilder(registerFile, stack, memory, stateVectorOptions, taskRunnerOptions);
            });

            builder.Services.AddSingleton(sp =>
            {
                var eventBus = sp.GetRequiredService<IEventBus>();
                var clock = sp.GetRequiredService<Clock>();
                var registerFile = sp.GetRequiredService<RegisterFile>();
                var stack = sp.GetRequiredService<Stack>();
                var memory = sp.GetRequiredService<Memory>();
                var controller = sp.GetRequiredService<MockNetwork>();
                var stateVectorBuilder = sp.GetRequiredService<StateVectorBuilder>();
                var options = neuralCpuOptions.Network.TaskRunner;
                var logger = sp.GetRequiredService<ILogger<TaskRunner>>();
                return new TaskRunner(eventBus, clock, registerFile, stack, memory, controller, stateVectorBuilder, options, logger);
            });

            // Register TUI application
            builder.Services.AddSingleton<NeuralCpuApp>();

            var host = builder.Build();

            Log.Information("Neural CPU starting...");

            // Initialize Terminal.Gui
            Application.Init();

            try
            {
                // Get the TUI app from DI container
                var app = host.Services.GetRequiredService<NeuralCpuApp>();

                // Run the application
                Application.Run(app);
            }
            finally
            {
                // Cleanup
                Application.Shutdown();
                host.Dispose();
                Log.Information("Neural CPU shutdown complete");
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
