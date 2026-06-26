namespace NeuralCpu.Network;

/// <summary>
/// Available tasks for the mock network.
/// </summary>
public enum MockNetworkTask
{
    /// <summary>Copy input to output (tests LOAD/STORE)</summary>
    Echo,

    /// <summary>Add 1 to each input value (tests ALU)</summary>
    Increment,

    /// <summary>Reverse the input sequence (tests Stack)</summary>
    Reverse,
}
