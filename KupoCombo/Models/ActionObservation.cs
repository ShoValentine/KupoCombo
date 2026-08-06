using System.Diagnostics;
using System.Threading;

namespace KupoCombo.Models;

public enum ActionObservationSource
{
    ClientExecution,
    Simulation
}

public readonly record struct ActionObservation
{
    private static long nextSequence;

    private ActionObservation(
        long sequence,
        long timestampTicks,
        uint executedActionId,
        ulong targetId,
        ActionObservationSource source)
    {
        Sequence = sequence;
        TimestampTicks = timestampTicks;
        ExecutedActionId = executedActionId;
        TargetId = targetId;
        Source = source;
    }

    public long Sequence { get; }

    public long TimestampTicks { get; }

    public uint ExecutedActionId { get; }

    public ulong TargetId { get; }

    public ActionObservationSource Source { get; }

    public static ActionObservation ClientExecution(
        uint executedActionId,
        ulong targetId = 0)
    {
        return Create(
            executedActionId,
            targetId,
            ActionObservationSource.ClientExecution);
    }

    public static ActionObservation Simulated(uint executedActionId)
    {
        return Create(
            executedActionId,
            0,
            ActionObservationSource.Simulation);
    }

    private static ActionObservation Create(
        uint executedActionId,
        ulong targetId,
        ActionObservationSource source)
    {
        return new ActionObservation(
            Interlocked.Increment(ref nextSequence),
            Stopwatch.GetTimestamp(),
            executedActionId,
            targetId,
            source);
    }
}
