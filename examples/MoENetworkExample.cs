// Example: Creating and using a Mixture of Experts neural network

using NeuralCpu.Network.Neural;
using TorchSharp;
using static TorchSharp.torch;

namespace NeuralCpu.Examples;

/// <summary>
/// Demonstrates how to create, configure, and use the Mixture of Experts network.
/// </summary>
public static class MoENetworkExample
{
    public static void BasicUsage()
    {
        // 1. Build a standard MoE network with 4 experts (ALU, Memory, Stack, Control)
        var network = new MoENetworkBuilder()
            .WithInputDim(inputDim: 227)    // State vector size (7 regs + 192 events + 8 stack + 16 mem + 1 cycle)
            .WithHiddenDim(256)             // Hidden layer dimension
            .WithOutputDim(100)             // Output dimension (action space size)
            .WithStandardExperts()          // Adds ALU, Memory, Stack, Control experts
            .WithExpertDepth(3)             // 3 modules per expert (vocabulary, semantics, strategy)
            .WithTopK(2)                    // Activate top-2 experts per input
            .WithDropout(0.1)               // 10% dropout
            .Build();

        Console.WriteLine($"Network created with {network.NumExperts} experts:");
        foreach (var domain in network.ExpertDomains)
        {
            Console.WriteLine($"  - {domain}");
        }

        // 2. Create a controller for training
        var controller = new NeuralNetworkController(network, device: CPU, isTraining: true);

        // 3. Save the network
        network.Save("moe_checkpoint.pt");
        Console.WriteLine("Network saved to moe_checkpoint.pt");

        // 4. Load the network
        var loadedNetwork = new MoENetworkBuilder()
            .WithInputDim(227)
            .WithHiddenDim(256)
            .WithOutputDim(100)
            .WithStandardExperts()
            .WithExpertDepth(3)
            .WithTopK(2)
            .Build();

        loadedNetwork.Load("moe_checkpoint.pt");
        Console.WriteLine("Network loaded from moe_checkpoint.pt");
    }

    public static void ProgressiveTraining()
    {
        // Build network
        var network = new MoENetworkBuilder()
            .WithInputDim(227)
            .WithHiddenDim(256)
            .WithOutputDim(100)
            .WithStandardExperts()
            .WithExpertDepth(3)
            .WithTopK(2)
            .Build();

        // Phase 1: Train all layers
        Console.WriteLine("Phase 1: Training all layers...");
        // ... train for N epochs ...

        // Phase 2: Freeze vocabulary layers, train semantic/strategy layers
        Console.WriteLine("Phase 2: Freezing vocabulary layer, training deeper layers...");
        network.FreezeBottomLayers(count: 1);  // Freeze first module in each expert
        // ... continue training ...

        // Phase 3: Freeze semantic layers too, train strategy only
        Console.WriteLine("Phase 3: Freezing vocabulary + semantics, training strategy...");
        network.FreezeBottomLayers(count: 2);  // Freeze first 2 modules in each expert
        // ... continue training ...

        // Check frozen status
        var aluExpert = network.GetExpertByDomain("ALU");
        var frozenStatus = aluExpert!.GetFrozenStatus();
        Console.WriteLine($"ALU expert frozen status: [{string.Join(", ", frozenStatus)}]");
        // Output: [True, True, False]  -- First 2 layers frozen, last layer trainable
    }

    public static void CustomExpertConfiguration()
    {
        // Build custom expert configuration
        var network = new MoENetworkBuilder()
            .WithInputDim(227)
            .WithHiddenDim(512)      // Larger hidden dimension
            .WithOutputDim(100)
            .AddExpert("Compute")    // Custom expert domains
            .AddExpert("IO")
            .AddExpert("Control")
            .WithExpertDepth(5)      // Deeper stacks (5 modules)
            .WithTopK(1)             // Only activate single best expert (no redundancy)
            .WithDropout(0.2)        // Higher dropout
            .Build();

        Console.WriteLine($"Custom network with {network.NumExperts} experts and depth 5");
    }
}
