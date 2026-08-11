using System.Diagnostics.Tracing;
using Godot;

/// <summary>
/// Метки границ циклов кадра для EventPipe. В Speedscope почти не видны (там sampling
/// стеков); в Chromium / Perfetto Start/Stop складываются в отрезки на timeline.
///
/// Пишутся только когда на источник есть подписчик (<see cref="EventSource.IsEnabled"/>):
/// при обычной игре без среза вызовы пустые. Провайдер подключает
/// <see cref="DotnetTraceCapture"/> на время записи.
/// </summary>
[EventSource(Name = ProviderName)]
public sealed class FrameTraceMarks : EventSource
{
    public const string ProviderName = "RedPlanet-FrameMarks";

    public static readonly FrameTraceMarks Log = new();

    public static class Tasks
    {
        public const EventTask Physics = (EventTask)1;
        public const EventTask Process = (EventTask)2;
    }

    [Event(1, Level = EventLevel.Informational, Task = Tasks.Physics, Opcode = EventOpcode.Start)]
    public void PhysicsStart(long frame) => WriteEvent(1, frame);

    [Event(2, Level = EventLevel.Informational, Task = Tasks.Physics, Opcode = EventOpcode.Stop)]
    public void PhysicsStop(long frame) => WriteEvent(2, frame);

    [Event(3, Level = EventLevel.Informational, Task = Tasks.Process, Opcode = EventOpcode.Start)]
    public void ProcessStart(long frame) => WriteEvent(3, frame);

    [Event(4, Level = EventLevel.Informational, Task = Tasks.Process, Opcode = EventOpcode.Stop)]
    public void ProcessStop(long frame) => WriteEvent(4, frame);

    /// <summary>Начало физического прогона систем и уборки индекса.</summary>
    public static void BeginPhysics()
    {
        if (!Log.IsEnabled())
            return;

        Log.PhysicsStart((long)Engine.GetPhysicsFrames());
    }

    /// <summary>Конец физического прогона.</summary>
    public static void EndPhysics()
    {
        if (!Log.IsEnabled())
            return;

        Log.PhysicsStop((long)Engine.GetPhysicsFrames());
    }

    /// <summary>Начало графического прогона систем.</summary>
    public static void BeginProcess()
    {
        if (!Log.IsEnabled())
            return;

        Log.ProcessStart((long)Engine.GetProcessFrames());
    }

    /// <summary>Конец графического прогона.</summary>
    public static void EndProcess()
    {
        if (!Log.IsEnabled())
            return;

        Log.ProcessStop((long)Engine.GetProcessFrames());
    }
}
