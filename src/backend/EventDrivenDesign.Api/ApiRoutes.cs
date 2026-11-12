using EventDrivenDesign.Domain;
using EventDrivenDesign.Shared;

namespace EventDrivenDesign.Api;

public static class ApiRoutes
{
    /// <summary>The gateway that owns a machine, from the shop segment of its id (P1-BDY-W001 → gateway-bdy:8080).</summary>
    public static Result<string> GatewayFor(string machineId)
    {
        var parts = machineId.Split('-');
        return parts.Length == 3 && MachineCatalog.Shops.Contains(parts[1])
            ? $"gateway-{parts[1].ToLowerInvariant()}:8080"
            : new Error(
                "api.machine.bad_id",
                $"'{machineId}' is not a machine id (expected <plant>-<shop>-<machine>, e.g. P1-BDY-W001).",
                ErrorKind.Invalid
            );
    }
}
