namespace EventDrivenDesign.Contracts;

public static class TopicNames
{
    public const string Readings = "machine.readings";
    public const string Alarms = "machine.alarms";
    public const string CentralReadingsPattern = @"^plant[0-9]+\.machine\.readings$";
    public const string CentralAlarmsPattern = @"^plant[0-9]+\.machine\.alarms$";

    /// <summary>One partition, never written to: the members of <c>gateway-lease-&lt;shop&gt;</c> contend for it to win the right to number.</summary>
    public static string Lease(string shop) => $"gateway.lease.{shop.ToLowerInvariant()}";
}
