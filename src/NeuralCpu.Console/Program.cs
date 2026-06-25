using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NeuralCpu.Core;
using Serilog;
using Terminal.Gui;

namespace NeuralCpu.Console;

internal class Program
{
    static void Main(string[] args)
    {
        // Configure Serilog before building the host
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File(
                path: "logs/neural-cpu-.log",
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: 10 * 1024 * 1024, // 10MB
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            var builder = Host.CreateApplicationBuilder(args);

            // Add Serilog
            builder.Services.AddSerilog();

            // Register Neural CPU core services
            builder.Services.AddNeuralCpu();

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
