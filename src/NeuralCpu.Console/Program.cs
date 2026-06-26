using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NeuralCpu.Core;
using NeuralCpu.Core.Configuration;
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
