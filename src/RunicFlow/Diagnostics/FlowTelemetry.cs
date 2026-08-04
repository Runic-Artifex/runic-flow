using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace RunicFlow;

/// <summary>Owns BCL diagnostics; logging integration is supplied by a future adapter.</summary>
internal static class FlowTelemetry
{
    public const string InstrumentationName = "RunicFlow";
    public const string NavigateActivityName = "flow.navigate";
    public const string DialogActivityName = "flow.dialog";
    public const string OperationActivityName = "flow.operation";
    public const string WorkflowActivityName = "flow.workflow";
    public const string PresentActivityName = "flow.present";

    public static readonly ActivitySource ActivitySource = new(InstrumentationName);
    public static readonly Meter Meter = new(InstrumentationName);
    public static readonly Counter<long> Transitions = Meter.CreateCounter<long>("flow.transitions");
    public static readonly Counter<long> Outcomes = Meter.CreateCounter<long>("flow.outcomes");
    public static readonly Counter<long> Faults = Meter.CreateCounter<long>("flow.faults");
    public static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("flow.duration", "s");
    public static readonly Histogram<double> QueueWait = Meter.CreateHistogram<double>("flow.queue.wait", "s");
}
