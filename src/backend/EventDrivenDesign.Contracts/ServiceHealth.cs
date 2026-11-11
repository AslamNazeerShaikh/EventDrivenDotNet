using System.Text.Json.Serialization;

namespace EventDrivenDesign.Contracts;

/// <summary>
/// Health of a service as evaluated by the API from heartbeat age.
/// A closed hierarchy: the evaluator needs no default arm.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(Healthy), "Healthy")]
[JsonDerivedType(typeof(Degraded), "Degraded")]
[JsonDerivedType(typeof(Down), "Down")]
public closed record class ServiceHealth;

/// <summary>The service is reporting normally (heartbeat within 3 s).</summary>
public sealed record class Healthy : ServiceHealth;

/// <summary>The service has not reported for 3 s but is not yet considered down.</summary>
/// <param name="Reason">Why the service is degraded.</param>
public sealed record class Degraded(string Reason) : ServiceHealth;

/// <summary>The service has not reported for 10 s.</summary>
/// <param name="LastSeen">When the last heartbeat arrived.</param>
public sealed record class Down(DateTimeOffset LastSeen) : ServiceHealth;
