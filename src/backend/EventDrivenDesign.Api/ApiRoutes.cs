using EventDrivenDesign.Domain;
using EventDrivenDesign.Shared;

namespace EventDrivenDesign.Api;

public static class ApiRoutes
{
    /// <summary>The gateway that owns a machine, from the shop segment of its id (P1-BDY-W001 → gateway-bdy-b:8080).</summary>
    public static Result<string> GatewayFor(string machineId, Snapshot s)
    {
        var parts = machineId.Split('-');
        if (parts.Length != 3 || !MachineCatalog.Shops.Contains(parts[1]))
            return new Error(
                "api.machine.bad_id",
                $"'{machineId}' is not a machine id (expected <plant>-<shop>-<machine>, e.g. P1-BDY-W001).",
                ErrorKind.Invalid
            );
        var active = s.Gateway(parts[1]);
        return active is null
            ? new Error(
                "api.gateway.unavailable",
                $"No active gateway for {parts[1]}.",
                ErrorKind.Unavailable
            )
            : $"{active.Value.GatewayId}:8080";
    }
}
