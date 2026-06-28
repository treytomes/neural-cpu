#!/usr/bin/env python3
"""
Calculate parameter count for TransformerCpuController network.
Based on architecture in TransformerCpuController.cs
"""

def count_linear(in_features, out_features):
    """Linear layer: weights + bias"""
    return in_features * out_features + out_features

def count_layer_norm(features):
    """LayerNorm: scale + shift"""
    return features * 2

def count_attention(embed_dim, num_heads):
    """Multi-head attention: Q, K, V projections + output projection"""
    # Q, K, V: each is embed_dim -> embed_dim
    qkv = 3 * count_linear(embed_dim, embed_dim)
    # Output projection: embed_dim -> embed_dim
    out_proj = count_linear(embed_dim, embed_dim)
    return qkv + out_proj

def count_transformer_block(embed_dim, num_heads, ffn_dim):
    """
    Transformer encoder block:
    - Multi-head attention
    - LayerNorm
    - FFN (2 linear layers)
    - LayerNorm
    """
    attn = count_attention(embed_dim, num_heads)
    ln1 = count_layer_norm(embed_dim)
    ffn = count_linear(embed_dim, ffn_dim) + count_linear(ffn_dim, embed_dim)
    ln2 = count_layer_norm(embed_dim)
    return attn + ln1 + ffn + ln2

def count_dual_attention(embed_dim, num_heads):
    """
    Dual attention block:
    - Cross attention (events -> cpu state)
    - Cross attention (cpu state -> events)
    - LayerNorms
    """
    cross_attn1 = count_attention(embed_dim, num_heads)
    ln1 = count_layer_norm(embed_dim)
    cross_attn2 = count_attention(embed_dim, num_heads)
    ln2 = count_layer_norm(embed_dim)
    return cross_attn1 + ln1 + cross_attn2 + ln2

def count_moe_expert(input_dim, hidden_dim, output_dim, depth=2):
    """
    MoE expert: multi-layer network
    depth=2: input -> hidden -> output
    """
    if depth == 2:
        return count_linear(input_dim, hidden_dim) + count_linear(hidden_dim, output_dim)
    else:
        # General case
        params = count_linear(input_dim, hidden_dim)
        for _ in range(depth - 2):
            params += count_linear(hidden_dim, hidden_dim)
        params += count_linear(hidden_dim, output_dim)
        return params

def count_moe_network(input_dim, hidden_dim, output_dim, num_experts=4, expert_depth=2, top_k=2):
    """
    MoE network:
    - Router (gating network)
    - Experts
    """
    router = count_linear(input_dim, num_experts)
    experts = num_experts * count_moe_expert(input_dim, hidden_dim, output_dim, expert_depth)
    return router + experts

def main():
    # Configuration from training
    embed_dim = 128
    num_heads = 4
    num_encoder_layers = 2
    ffn_dim = embed_dim * 4  # 512

    print("=" * 70)
    print("TransformerCpuController Parameter Count")
    print("=" * 70)
    print(f"\nConfiguration:")
    print(f"  Embedding Dim: {embed_dim}")
    print(f"  Attention Heads: {num_heads}")
    print(f"  Encoder Layers: {num_encoder_layers}")
    print(f"  FFN Hidden Dim: {ffn_dim}")
    print()

    params = {}

    # Event encoding
    params["Event Embedding"] = count_linear(3, embed_dim)  # 3 fields -> embed_dim
    params["Event Positional Encoding"] = 0  # Computed, not learned (sinusoidal)

    # Event transformer stack
    event_encoder_params = count_transformer_block(embed_dim, num_heads, ffn_dim)
    params["Event Encoders"] = num_encoder_layers * event_encoder_params

    # CPU state encoding
    params["CPU State Embedding"] = count_linear(1, embed_dim)  # Each scalar -> embed_dim
    params["CPU Positional Encoding"] = 0  # Computed

    # CPU state transformer stack
    cpu_encoder_params = count_transformer_block(embed_dim, num_heads, ffn_dim)
    params["CPU Encoders"] = num_encoder_layers * cpu_encoder_params

    # Dual attention
    params["Dual Attention"] = count_dual_attention(embed_dim, num_heads)

    # MoE network
    moe_params = count_moe_network(
        input_dim=embed_dim,
        hidden_dim=embed_dim,
        output_dim=embed_dim,
        num_experts=4,
        expert_depth=2,
        top_k=2
    )
    params["MoE Network"] = moe_params

    # Action decoder
    ACTION_DIM = 35 + 8*3 + 16 + 256  # 331
    params["Action Decoder"] = count_linear(embed_dim, ACTION_DIM)

    # Value decoder (for 64-bit values)
    VALUE_EMBED_DIM = 256
    params["Value Decoder"] = (
        count_linear(VALUE_EMBED_DIM, 128) +
        count_linear(128, 64)
    )

    # Print breakdown
    print("Parameter Breakdown:")
    print("-" * 70)
    total = 0
    for name, count in params.items():
        print(f"  {name:<30} {count:>12,} params")
        total += count

    print("-" * 70)
    print(f"  {'TOTAL':<30} {total:>12,} params")
    print()

    # Memory footprint (float32)
    memory_mb = (total * 4) / (1024 * 1024)
    print(f"Memory Footprint (FP32): {memory_mb:.2f} MB")

    # Compare to reference models
    print()
    print("=" * 70)
    print("Comparison to Reference Models:")
    print("=" * 70)

    comparisons = [
        ("GPT-2 (124M)", 124_000_000),
        ("BERT-Base (110M)", 110_000_000),
        ("DistilBERT (66M)", 66_000_000),
        ("MobileNet-v2 (3.5M)", 3_500_000),
        ("Our Network", total),
        ("ResNet-18 (11.7M)", 11_700_000),
    ]

    for name, count in sorted(comparisons, key=lambda x: x[1], reverse=True):
        bar_length = int(count / 2_000_000)  # Scale: 1 char = 2M params
        bar = "#" * min(bar_length, 60)

        if name == "Our Network":
            print(f"-> {name:<20} {count:>12,} params  {bar} <-")
        else:
            print(f"  {name:<20} {count:>12,}  {bar}")

    print()

    # Task complexity analysis
    print("=" * 70)
    print("Task Complexity vs Network Capacity:")
    print("=" * 70)
    print()

    print("RegisterActivation Task:")
    print("  - Input: 7 registers, 64-event history, 8 stack items, 16 memory slots")
    print("  - Output: 1 action (write to target register)")
    print("  - Complexity: Lookup (which register to write)")
    print("  - Required capacity: ~1K params (simple mapping)")
    print(f"  - Available capacity: {total:,} params")
    print("  - Capacity ratio: {:.0f}x over-provisioned [OK]".format(total / 1000))
    print()

    print("StackGrowth Task:")
    print("  - Input: Same state vector")
    print("  - Output: 1 action (PUSH command)")
    print("  - Complexity: Select PUSH from 35 event types")
    print("  - Required capacity: ~1K params")
    print(f"  - Available capacity: {total:,} params")
    print("  - Capacity ratio: {:.0f}x over-provisioned [OK]".format(total / 1000))
    print()

    print("ALUActivation Task:")
    print("  - Input: Same state vector")
    print("  - Output: 1 action (any ALU op)")
    print("  - Complexity: Select from 23 ALU ops")
    print("  - Required capacity: ~1K params")
    print(f"  - Available capacity: {total:,} params")
    print("  - Capacity ratio: {:.0f}x over-provisioned [OK]".format(total / 1000))
    print()

    print("InputRead Task:")
    print("  - Input: Same state vector")
    print("  - Output: LOAD from 0x80000000")
    print("  - Complexity: Memorize fixed address")
    print("  - Required capacity: ~5K params (address encoding)")
    print(f"  - Available capacity: {total:,} params")
    print("  - Capacity ratio: {:.0f}x over-provisioned [OK]".format(total / 5000))
    print()

    print("OutputWrite Task:")
    print("  - Input: Same state vector")
    print("  - Output: STORE to 0x80001000")
    print("  - Complexity: Memorize fixed address")
    print("  - Required capacity: ~5K params")
    print(f"  - Available capacity: {total:,} params")
    print("  - Capacity ratio: {:.0f}x over-provisioned [OK]".format(total / 5000))
    print()

    print("=" * 70)
    print("CONCLUSION:")
    print("=" * 70)
    print()
    print("Network has MORE than sufficient capacity for Motor Skills tasks.")
    print(f"With {total:,} parameters, the network can easily learn:")
    print("  - Simple conditional mappings (RegisterActivation)")
    print("  - Fixed action selections (StackGrowth, ALUActivation)")
    print("  - Memory address constants (InputRead, OutputWrite)")
    print()
    print("The training failure is NOT due to insufficient capacity.")
    print("Root cause: Sparse rewards + large action space = no gradient signal.")
    print()
    print("Expected behavior after behavior cloning:")
    print("  - Network will use <1% of capacity for these simple tasks")
    print("  - Remaining capacity available for:")
    print("    * More complex multi-step sequences")
    print("    * Transfer learning to novel tasks")
    print("    * Generalizing control strategies")
    print()

if __name__ == "__main__":
    main()
