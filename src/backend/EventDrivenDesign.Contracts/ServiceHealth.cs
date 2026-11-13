using System.Text.Json.Serialization;

namespace EventDrivenDesign.Contracts;

// Source health as the API sees it. Closed, so a switch over it needs no default arm.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(Healthy), "Healthy")]
[JsonDerivedType(typeof(Degraded), "Degraded")]
[JsonDerivedType(typeof(Down), "Down")]
public closed record class ServiceHealth;

public sealed record class Healthy : ServiceHealth;

public sealed record class Degraded(string Reason) : ServiceHealth;

public sealed record class Down(DateTimeOffset LastSeen) : ServiceHealth;
