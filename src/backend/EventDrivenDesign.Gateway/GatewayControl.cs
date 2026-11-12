using EventDrivenDesign.Contracts;
using EventDrivenDesign.Persistence;
using EventDrivenDesign.Shared;

namespace EventDrivenDesign.Gateway;

/// <summary>Applies control-room commands. Every command is idempotent (doc 02 §4.6).</summary>
public sealed class GatewayControl(Collector collector, GatewayBuffer buffer)
{
    public async Task<Result> ApplyAsync(GatewayCommand command, CancellationToken ct)
    {
        switch (command)
        {
            case Pause:
                await buffer.SetPausedAsync(true, ct);
                return Result.Success();
            case Resume:
                await buffer.SetPausedAsync(false, ct);
                return Result.Success();
            case InjectFault f:
                return collector.InjectFault(f.MachineId, f.Kind)
                    ? Result.Success()
                    : collector.LastSeqByMachine.ContainsKey(f.MachineId)
                        ? new Error("gateway.fault.not_applicable", $"{f.Kind} does not apply to {f.MachineId}.", ErrorKind.Invalid)
                        : new Error("gateway.machine.unknown", $"No machine {f.MachineId} on this gateway.", ErrorKind.NotFound);
            case null:
                throw new ArgumentNullException(nameof(command));
        }
    }
}
