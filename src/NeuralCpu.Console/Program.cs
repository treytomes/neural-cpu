using NeuralCpu.Core.Components;
using NeuralCpu.Core.Events;

namespace NeuralCpu.Console;

internal class Program
{
    static async Task Main(string[] args)
    {
        System.Console.WriteLine("Neural CPU - Clock Module Test");
        System.Console.WriteLine("================================\n");

        // Create event bus and clock
        var eventBus = new EventBus();
        var clock = new Clock(eventBus);

        // Subscribe to clock events
        var subscription = eventBus.Subscribe<ClockPhaseChanged>(new ClockObserver());

        System.Console.WriteLine("Clock created at 1 Hz (default)");
        System.Console.WriteLine("\nPress 's' to start, 'p' to pause, 'SPACE' to step, '+/-' to adjust frequency, 'q' to quit\n");

        bool running = true;
        while (running)
        {
            if (System.Console.KeyAvailable)
            {
                var key = System.Console.ReadKey(intercept: true);
                switch (key.Key)
                {
                    case ConsoleKey.S:
                        clock.Start();
                        System.Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Clock started at {clock.FrequencyHz} Hz");
                        break;

                    case ConsoleKey.P:
                        clock.Stop();
                        System.Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Clock paused (Cycle {clock.CycleCount})");
                        break;

                    case ConsoleKey.Spacebar:
                        System.Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Manual step...");
                        clock.Step();
                        break;

                    case ConsoleKey.Add:
                    case ConsoleKey.OemPlus:
                        clock.FrequencyHz = Math.Min(clock.FrequencyHz + 1, 100);
                        System.Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Frequency: {clock.FrequencyHz} Hz");
                        break;

                    case ConsoleKey.Subtract:
                    case ConsoleKey.OemMinus:
                        clock.FrequencyHz = Math.Max(clock.FrequencyHz - 1, 1);
                        System.Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Frequency: {clock.FrequencyHz} Hz");
                        break;

                    case ConsoleKey.I:
                        // Test interrupt
                        eventBus.Publish(new HardwareInterrupt(0x10, "Test interrupt"));
                        System.Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Hardware interrupt sent (Vector 0x10)");
                        break;

                    case ConsoleKey.Q:
                        running = false;
                        break;
                }
            }

            await Task.Delay(50);
        }

        clock.Stop();
        subscription.Dispose();
        clock.Dispose();

        System.Console.WriteLine("\nClock stopped. Exiting...");
    }

    private sealed class ClockObserver : IObserver<ClockPhaseChanged>
    {
        public void OnNext(ClockPhaseChanged value)
        {
            var phaseSymbol = value.Phase == ClockPhase.High ? "↑" : "↓";
            var color = value.Phase == ClockPhase.High ? ConsoleColor.Green : ConsoleColor.DarkGray;

            var originalColor = System.Console.ForegroundColor;
            System.Console.ForegroundColor = color;
            System.Console.WriteLine($"  {phaseSymbol} {value}");
            System.Console.ForegroundColor = originalColor;
        }

        public void OnError(Exception error)
        {
            System.Console.WriteLine($"ERROR: {error.Message}");
        }

        public void OnCompleted()
        {
            System.Console.WriteLine("Clock observer completed.");
        }
    }
}
