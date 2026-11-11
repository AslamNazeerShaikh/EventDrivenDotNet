namespace EventDrivenDesign.Application;

/// <summary>
/// Handles one in-process message. Dispatch over contract unions stays in plain C#
/// via exhaustive switch expressions at the call sites (spec section 11).
/// </summary>
/// <typeparam name="T">The message type to handle.</typeparam>
public interface IMessageHandler<in T>
{
    /// <summary>
    /// Handles a single message.
    /// </summary>
    /// <param name="message">The message to handle.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when handling finishes.</returns>
    ValueTask HandleAsync(T message, CancellationToken ct);
}
