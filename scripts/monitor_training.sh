#!/bin/bash
# Monitor training progress in real-time

METRICS_FILE=$(ls -t training_metrics/metrics_*.csv 2>/dev/null | head -1)

if [ -z "$METRICS_FILE" ]; then
    echo "No metrics file found"
    exit 1
fi

echo "Monitoring: $METRICS_FILE"
echo "Press Ctrl+C to stop monitoring"
echo ""

while true; do
    clear
    echo "========================================"
    echo "Motor Skills Training Progress"
    echo "========================================"
    echo ""

    # Total episodes
    TOTAL=$(tail -n +2 "$METRICS_FILE" | wc -l)
    echo "Total episodes completed: $TOTAL / 1000"

    # Current task
    CURRENT_TASK=$(tail -1 "$METRICS_FILE" | cut -d',' -f2)
    CURRENT_EP=$(tail -1 "$METRICS_FILE" | cut -d',' -f3)
    echo "Current task: $CURRENT_TASK (episode $CURRENT_EP / 200)"
    echo ""

    # Success rates per task
    echo "Task Success Rates:"
    for task in RegisterActivation StackGrowth ALUActivation InputRead OutputWrite; do
        SUCCESS=$(grep ",$task," "$METRICS_FILE" | grep ",True," | wc -l)
        ATTEMPTS=$(grep ",$task," "$METRICS_FILE" | wc -l)

        if [ "$ATTEMPTS" -gt 0 ]; then
            RATE=$(echo "scale=1; 100 * $SUCCESS / $ATTEMPTS" | bc)
            printf "  %-20s: %3d/%3d (%5.1f%%)\n" "$task" "$SUCCESS" "$ATTEMPTS" "$RATE"
        fi
    done

    echo ""
    echo "Last 5 episodes:"
    tail -5 "$METRICS_FILE" | awk -F',' 'NR>1 {printf "  Ep %3d: %s = %s (reward %.2f, %2d cycles)\n", $3, $2, ($4=="True"?"SUCCESS":"FAIL"), $5, $6}'

    sleep 5
done
