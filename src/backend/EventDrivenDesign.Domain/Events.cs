namespace EventDrivenDesign.Domain;

/// <summary>
/// Dummy order event emitted by producer-a onto <c>demo.events</c>.
/// </summary>
/// <param name="EventId">Unique event id (ce_id, UUIDv7).</param>
/// <param name="StreamId">Producer stream the event belongs to; seq restarts at 1 per stream.</param>
/// <param name="Seq">Gap-free sequence number within the stream.</param>
/// <param name="ProducerId">Id of the producing service.</param>
/// <param name="RunId">Command id of the Start that began the run.</param>
/// <param name="OrderId">Dummy order id (also the Kafka message key).</param>
/// <param name="AmountCents">Dummy order amount in cents.</param>
/// <param name="ProducedAt">When the event was produced.</param>
public sealed record OrderPlaced(
    string EventId,
    string StreamId,
    long Seq,
    string ProducerId,
    string RunId,
    string OrderId,
    int AmountCents,
    DateTimeOffset ProducedAt);

/// <summary>
/// Dummy payment event emitted by producer-b onto <c>demo.events</c>.
/// </summary>
/// <param name="EventId">Unique event id (ce_id, UUIDv7).</param>
/// <param name="StreamId">Producer stream the event belongs to; seq restarts at 1 per stream.</param>
/// <param name="Seq">Gap-free sequence number within the stream.</param>
/// <param name="ProducerId">Id of the producing service.</param>
/// <param name="RunId">Command id of the Start that began the run.</param>
/// <param name="PaymentId">Dummy payment id.</param>
/// <param name="OrderId">Dummy order id the payment belongs to (also the Kafka message key).</param>
/// <param name="AmountCents">Dummy payment amount in cents.</param>
/// <param name="ProducedAt">When the event was produced.</param>
public sealed record PaymentCaptured(
    string EventId,
    string StreamId,
    long Seq,
    string ProducerId,
    string RunId,
    string PaymentId,
    string OrderId,
    int AmountCents,
    DateTimeOffset ProducedAt);

/// <summary>
/// One row of the producer outbox: an event durably stored before it is published.
/// </summary>
/// <param name="OutboxId">Row id.</param>
/// <param name="Topic">Target Kafka topic.</param>
/// <param name="MessageKey">Kafka message key.</param>
/// <param name="Payload">JSON event body.</param>
/// <param name="StreamId">Producer stream the event belongs to.</param>
/// <param name="Seq">Sequence number within the stream.</param>
/// <param name="CreatedAt">When the row was written.</param>
/// <param name="DispatchedAt">When the row was published; null while pending.</param>
public sealed record OutboxMessage(
    long OutboxId,
    string Topic,
    string MessageKey,
    string Payload,
    string StreamId,
    long Seq,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DispatchedAt);
