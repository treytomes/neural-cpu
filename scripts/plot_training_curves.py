#!/usr/bin/env python3
"""
Plot training curves from metrics CSV file.
Generates learning curve visualizations for Motor Skills training.

Usage:
    python plot_training_curves.py training_metrics/metrics_<runId>.csv
"""

import pandas as pd
import matplotlib.pyplot as plt
import seaborn as sns
import sys
from pathlib import Path

sns.set_style("whitegrid")
sns.set_palette("husl")

def plot_training_curves(csv_path):
    """Generate all training curve plots from metrics CSV."""

    # Load data
    df = pd.read_csv(csv_path)
    run_id = df['RunId'].iloc[0]
    output_dir = Path(csv_path).parent / f"plots_{run_id}"
    output_dir.mkdir(exist_ok=True)

    print(f"Loading metrics from: {csv_path}")
    print(f"Run ID: {run_id}")
    print(f"Total episodes: {len(df)}")
    print(f"Tasks: {df['TaskName'].unique().tolist()}")

    # 1. Success Rate Over Time (per task)
    plot_success_rate(df, output_dir)

    # 2. Policy Loss Over Time (per task)
    plot_policy_loss(df, output_dir)

    # 3. Cycles to Success (per task)
    plot_cycles_to_success(df, output_dir)

    # 4. Combined Dashboard
    plot_dashboard(df, output_dir)

    # 5. Task Comparison (final performance)
    plot_task_comparison(df, output_dir)

    print(f"\nPlots saved to: {output_dir}")
    return output_dir

def plot_success_rate(df, output_dir):
    """Plot success rate over episodes with rolling average."""
    fig, ax = plt.subplots(figsize=(12, 6))

    window = 20  # Rolling average window

    for task in df['TaskName'].unique():
        task_df = df[df['TaskName'] == task]
        # Rolling average of success rate
        rolling_success = task_df['Success'].rolling(window=window, min_periods=1).mean() * 100

        ax.plot(task_df['Episode'], rolling_success, label=task, linewidth=2)

    ax.set_xlabel('Episode', fontsize=12)
    ax.set_ylabel('Success Rate (%)', fontsize=12)
    ax.set_title(f'Motor Skills Training - Success Rate Over Time (Rolling Avg, Window={window})', fontsize=14)
    ax.legend(loc='best')
    ax.grid(True, alpha=0.3)
    ax.set_ylim([0, 105])

    plt.tight_layout()
    plt.savefig(output_dir / 'success_rate.png', dpi=300, bbox_inches='tight')
    plt.close()
    print("[OK] success_rate.png")

def plot_policy_loss(df, output_dir):
    """Plot policy loss over episodes."""
    fig, ax = plt.subplots(figsize=(12, 6))

    for task in df['TaskName'].unique():
        task_df = df[df['TaskName'] == task]
        # Rolling average for smoothing
        rolling_loss = task_df['PolicyLoss'].rolling(window=10, min_periods=1).mean()

        ax.plot(task_df['Episode'], rolling_loss, label=task, linewidth=2, alpha=0.8)

    ax.set_xlabel('Episode', fontsize=12)
    ax.set_ylabel('Policy Loss', fontsize=12)
    ax.set_title('Motor Skills Training - Policy Loss Over Time (Rolling Avg)', fontsize=14)
    ax.legend(loc='best')
    ax.grid(True, alpha=0.3)
    ax.set_yscale('log')  # Log scale often better for loss

    plt.tight_layout()
    plt.savefig(output_dir / 'policy_loss.png', dpi=300, bbox_inches='tight')
    plt.close()
    print("[OK] policy_loss.png")

def plot_cycles_to_success(df, output_dir):
    """Plot cycles to success for successful episodes."""
    fig, ax = plt.subplots(figsize=(12, 6))

    for task in df['TaskName'].unique():
        task_df = df[(df['TaskName'] == task) & (df['Success'] == True)]

        if len(task_df) > 0:
            # Rolling average of cycles
            rolling_cycles = task_df['CycleCount'].rolling(window=10, min_periods=1).mean()
            ax.plot(task_df['Episode'], rolling_cycles, label=task, linewidth=2, alpha=0.8)

    ax.set_xlabel('Episode', fontsize=12)
    ax.set_ylabel('Cycles to Success', fontsize=12)
    ax.set_title('Motor Skills Training - Cycles to Success (Successful Episodes Only)', fontsize=14)
    ax.legend(loc='best')
    ax.grid(True, alpha=0.3)

    plt.tight_layout()
    plt.savefig(output_dir / 'cycles_to_success.png', dpi=300, bbox_inches='tight')
    plt.close()
    print("[OK] cycles_to_success.png")

def plot_dashboard(df, output_dir):
    """Create a 2x2 dashboard of key metrics."""
    fig, axes = plt.subplots(2, 2, figsize=(16, 12))
    window = 20

    # Top-left: Success rate
    ax = axes[0, 0]
    for task in df['TaskName'].unique():
        task_df = df[df['TaskName'] == task]
        rolling_success = task_df['Success'].rolling(window=window, min_periods=1).mean() * 100
        ax.plot(task_df['Episode'], rolling_success, label=task, linewidth=2)
    ax.set_xlabel('Episode')
    ax.set_ylabel('Success Rate (%)')
    ax.set_title(f'Success Rate (Rolling Avg, Window={window})')
    ax.legend(loc='best', fontsize=8)
    ax.grid(True, alpha=0.3)
    ax.set_ylim([0, 105])

    # Top-right: Policy loss
    ax = axes[0, 1]
    for task in df['TaskName'].unique():
        task_df = df[df['TaskName'] == task]
        rolling_loss = task_df['PolicyLoss'].rolling(window=10, min_periods=1).mean()
        ax.plot(task_df['Episode'], rolling_loss, label=task, linewidth=2, alpha=0.8)
    ax.set_xlabel('Episode')
    ax.set_ylabel('Policy Loss (log scale)')
    ax.set_title('Policy Loss Over Time')
    ax.legend(loc='best', fontsize=8)
    ax.grid(True, alpha=0.3)
    ax.set_yscale('log')

    # Bottom-left: Reward distribution
    ax = axes[1, 0]
    for task in df['TaskName'].unique():
        task_df = df[df['TaskName'] == task]
        rolling_reward = task_df['Reward'].rolling(window=window, min_periods=1).mean()
        ax.plot(task_df['Episode'], rolling_reward, label=task, linewidth=2)
    ax.set_xlabel('Episode')
    ax.set_ylabel('Reward')
    ax.set_title('Average Reward Over Time')
    ax.legend(loc='best', fontsize=8)
    ax.grid(True, alpha=0.3)

    # Bottom-right: Cycles for successful episodes
    ax = axes[1, 1]
    for task in df['TaskName'].unique():
        task_df = df[(df['TaskName'] == task) & (df['Success'] == True)]
        if len(task_df) > 0:
            rolling_cycles = task_df['CycleCount'].rolling(window=10, min_periods=1).mean()
            ax.plot(task_df['Episode'], rolling_cycles, label=task, linewidth=2, alpha=0.8)
    ax.set_xlabel('Episode')
    ax.set_ylabel('Cycles to Success')
    ax.set_title('Cycles to Success (Successful Episodes)')
    ax.legend(loc='best', fontsize=8)
    ax.grid(True, alpha=0.3)

    plt.suptitle('Motor Skills Training Dashboard', fontsize=16, y=0.995)
    plt.tight_layout()
    plt.savefig(output_dir / 'dashboard.png', dpi=300, bbox_inches='tight')
    plt.close()
    print("[OK] dashboard.png")

def plot_task_comparison(df, output_dir):
    """Compare final performance across tasks."""
    fig, axes = plt.subplots(1, 3, figsize=(16, 5))

    tasks = df['TaskName'].unique()

    # Calculate final performance metrics (last 20 episodes)
    final_metrics = []
    for task in tasks:
        task_df = df[df['TaskName'] == task].tail(20)
        final_metrics.append({
            'Task': task,
            'Success Rate': task_df['Success'].mean() * 100,
            'Avg Reward': task_df['Reward'].mean(),
            'Avg Cycles': task_df[task_df['Success'] == True]['CycleCount'].mean() if task_df['Success'].any() else 0
        })

    metrics_df = pd.DataFrame(final_metrics)

    # Success rate comparison
    ax = axes[0]
    bars = ax.bar(metrics_df['Task'], metrics_df['Success Rate'], color=sns.color_palette("husl", len(tasks)))
    ax.set_ylabel('Success Rate (%)')
    ax.set_title('Final Success Rate\n(Last 20 Episodes)')
    ax.set_ylim([0, 105])
    ax.tick_params(axis='x', rotation=45)
    for bar in bars:
        height = bar.get_height()
        ax.text(bar.get_x() + bar.get_width()/2., height,
                f'{height:.1f}%', ha='center', va='bottom', fontsize=9)

    # Average reward comparison
    ax = axes[1]
    bars = ax.bar(metrics_df['Task'], metrics_df['Avg Reward'], color=sns.color_palette("husl", len(tasks)))
    ax.set_ylabel('Average Reward')
    ax.set_title('Final Average Reward\n(Last 20 Episodes)')
    ax.set_ylim([0, 1.1])
    ax.tick_params(axis='x', rotation=45)
    for bar in bars:
        height = bar.get_height()
        ax.text(bar.get_x() + bar.get_width()/2., height,
                f'{height:.2f}', ha='center', va='bottom', fontsize=9)

    # Cycles to success comparison
    ax = axes[2]
    cycles_data = metrics_df[metrics_df['Avg Cycles'] > 0]
    if len(cycles_data) > 0:
        bars = ax.bar(cycles_data['Task'], cycles_data['Avg Cycles'], color=sns.color_palette("husl", len(tasks)))
        ax.set_ylabel('Average Cycles')
        ax.set_title('Final Avg Cycles to Success\n(Successful Episodes)')
        ax.tick_params(axis='x', rotation=45)
        for bar in bars:
            height = bar.get_height()
            ax.text(bar.get_x() + bar.get_width()/2., height,
                    f'{height:.1f}', ha='center', va='bottom', fontsize=9)

    plt.suptitle('Task Performance Comparison (Final 20 Episodes)', fontsize=14)
    plt.tight_layout()
    plt.savefig(output_dir / 'task_comparison.png', dpi=300, bbox_inches='tight')
    plt.close()
    print("[OK] task_comparison.png")

def print_summary_stats(df):
    """Print summary statistics to console."""
    print("\n" + "="*60)
    print("TRAINING SUMMARY STATISTICS")
    print("="*60)

    for task in df['TaskName'].unique():
        task_df = df[df['TaskName'] == task]
        final_20 = task_df.tail(20)

        print(f"\n{task}:")
        print(f"  Total Episodes: {len(task_df)}")
        print(f"  Overall Success Rate: {task_df['Success'].mean()*100:.1f}%")
        print(f"  Final Success Rate (last 20): {final_20['Success'].mean()*100:.1f}%")
        print(f"  First Success: Episode {task_df[task_df['Success'] == True]['Episode'].min() if task_df['Success'].any() else 'None'}")

        if task_df['Success'].any():
            successful = task_df[task_df['Success'] == True]
            print(f"  Avg Cycles to Success: {successful['CycleCount'].mean():.1f}")
            print(f"  Min Cycles: {successful['CycleCount'].min()}")

        print(f"  Final Avg Policy Loss: {final_20['PolicyLoss'].mean():.6f}")

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python plot_training_curves.py <metrics_csv_path>")
        print("\nExample:")
        print("  python plot_training_curves.py training_metrics/metrics_20260627_120000.csv")
        sys.exit(1)

    csv_path = sys.argv[1]

    if not Path(csv_path).exists():
        print(f"Error: File not found: {csv_path}")
        sys.exit(1)

    # Generate plots
    output_dir = plot_training_curves(csv_path)

    # Print summary
    df = pd.read_csv(csv_path)
    print_summary_stats(df)

    print(f"\n[OK] All plots generated successfully!")
    print(f"  Output directory: {output_dir}")
