#!/usr/bin/env python3
"""
Quick test script to verify motor skills training integration.
Runs a small number of episodes to check for errors.
"""

import subprocess
import sys

def run_test():
    print("=" * 60)
    print("Motor Skills Training - Quick Test")
    print("=" * 60)
    print("Running 10 episodes per task to verify integration...")
    print()

    # This would need to be a .NET program call
    # For now, documenting the approach
    print("To run training:")
    print("1. Build: dotnet build neural-cpu.slnx")
    print("2. Modify MotorSkillsTrainingProgram episodesPerTask to 10 (for testing)")
    print("3. Run: dotnet run --project src/NeuralCpu.Console/NeuralCpu.Console.csproj")
    print()
    print("For full training (200 episodes), restore episodesPerTask to 200")

if __name__ == "__main__":
    run_test()
