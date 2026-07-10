using BenchmarkDotNet.Running;

internal class Program
{
    private static async Task Main(string[] args)
    {
        // Check if the user explicitly requested benchmarks from the terminal command
        if (args.Length > 0 && (args[0] == "--bench" || args[0] == "-b"))
        {
            Console.WriteLine("[SYS] Diverting execution path to Benchmark.NET...");
            
            // Fix: Do NOT pass the custom "--bench" string down to BenchmarkDotNet.
            // Passing an empty array tells the switcher to display an on-demand interactive menu.
            string[] benchmarkArgs = args.Length > 1 ? args[1..] : Array.Empty<string>();
            
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(benchmarkArgs);
            return; 
        }

        // ---------------------------------------------------------------------
        // DEFAULT FALLBACK PATH: Run Live Venue Simulation
        // ---------------------------------------------------------------------
        // 1. Create a global cancellation token source for graceful shutdown
        using var cts = new CancellationTokenSource();

        // 2. Wire up the CTRL+C / Break event to trigger cancellation
        Console.CancelKeyPress += (sender, eventArgs) =>
        {
            Console.WriteLine("\n[SYS] Shutdown signal received. Tweaking engine down...");
            eventArgs.Cancel = true; // Prevents the OS from crashing the app instantly
            cts.Cancel();            // Signals our background loops to stop
        };

        try
        {
            // 3. Initialize your decoupled components
            var chaosEngine = new ChaosEngine(seed: 42);
            
            // Using standard local loopback multicast group and an enterprise data port
            var simulator = new VenueMulticastSimulator(multicastIp: "239.255.0.1", port: 14000, chaosEngine);

            // 4. Fire up the background workers and the hot-path execution thread
            simulator.Start();

            Console.WriteLine("[SYS] Boulevard Venue Simulator online.");
            Console.WriteLine("[SYS] Press CTRL+C to gracefully exit.\n");

            // 5. THE FIX: Asynchronously pause the Main thread indefinitely until CTRL+C is pressed
            await Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
        }
        catch (TaskCanceledException)
        {
            // This is expected behavior when Task.Delay is interrupted by CTRL+C
            Console.WriteLine("[SYS] Simulator stopped cleanly.");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FATAL ERROR] Engine crashed: {ex.Message}");
            Console.ResetColor();
        }
        finally
        {
            Console.WriteLine("[SYS] Context destroyed. Goodbye.");
        }
    }
}

