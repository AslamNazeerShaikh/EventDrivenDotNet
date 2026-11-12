namespace EventDrivenDesign.Contracts;

public static class TopicNames
{
    public const string Readings = "machine.readings";
    public const string Alarms = "machine.alarms";
    public const string CentralReadingsPattern = @"^plant\d+\.machine\.readings$";
    public const string CentralAlarmsPattern = @"^plant\d+\.machine\.alarms$";
}
