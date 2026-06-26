using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace NeuralCpu.Network.Neural;

/// <summary>
/// Expert network - a vertical stack of freezable modules.
/// Each expert specializes in a domain (ALU, memory, stack, control).
/// Modules stack from vocabulary → semantics → strategy.
/// </summary>
public sealed class Expert : FreezableModule
{
    #region Fields

    private readonly ModuleList<FreezableModule> _modules;
    private readonly string _domain;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates an expert with a vertical stack of modules.
    /// </summary>
    /// <param name="domain">Expert domain (e.g., "ALU", "Memory", "Stack", "Control").</param>
    /// <param name="modules">Stack of modules from shallow (vocabulary) to deep (strategy).</param>
    public Expert(string domain, params FreezableModule[] modules)
        : base($"Expert_{domain}")
    {
        _domain = domain;
        _modules = ModuleList(modules);
        RegisterComponents();
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the expert's domain specialization.
    /// </summary>
    public string Domain => _domain;

    /// <summary>
    /// Gets the number of modules in the vertical stack.
    /// </summary>
    public int Depth => _modules.Count;

    #endregion

    #region Methods

    /// <summary>
    /// Freezes the first N modules in the stack (shallow vocabulary layers).
    /// </summary>
    public void FreezeBottomLayers(int count)
    {
        for (int i = 0; i < Math.Min(count, _modules.Count); i++)
        {
            _modules[i].Freeze();
        }
    }

    /// <summary>
    /// Freezes the last N modules in the stack (deep strategy layers).
    /// </summary>
    public void FreezeTopLayers(int count)
    {
        int start = Math.Max(0, _modules.Count - count);
        for (int i = start; i < _modules.Count; i++)
        {
            _modules[i].Freeze();
        }
    }

    /// <summary>
    /// Unfreezes all modules in the stack.
    /// </summary>
    public new void Unfreeze()
    {
        foreach (var module in _modules)
        {
            module.Unfreeze();
        }
        base.Unfreeze();
    }

    /// <summary>
    /// Gets the frozen status of each module in the stack.
    /// </summary>
    public bool[] GetFrozenStatus()
    {
        return _modules.Select(m => m.IsFrozen).ToArray();
    }

    public override Tensor forward(Tensor input)
    {
        var x = input;

        // Pass through each module in the stack
        foreach (var module in _modules)
        {
            x = module.forward(x);
        }

        return x;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _modules.Dispose();
        }
        base.Dispose(disposing);
    }

    #endregion
}
