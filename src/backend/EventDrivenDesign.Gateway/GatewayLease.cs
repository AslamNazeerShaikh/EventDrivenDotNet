using Confluent.Kafka;
using EventDrivenDesign.Contracts;
using EventDrivenDesign.Messaging;
using EventDrivenDesign.Persistence;

namespace EventDrivenDesign.Gateway;

public enum LeaseDecision
{
    None,
    StartTerm,
    StartNewTerm,
    StopTerm,
    FlushSender,
}

/// <summary>The term rules of the lease, pure and unit-tested (doc 03 R3 rules 2-7).</summary>
public sealed class LeaseTerm
{
    private readonly object _gate = new();
    private bool _active;
    private bool _wasLost;

    public bool Active
    {
        get
        {
            lock (_gate)
                return _active;
        }
    }
    public bool WasLost
    {
        get
        {
            lock (_gate)
                return _wasLost;
        }
    }

    /// <summary>
    /// Assigned with (or without) the lease partition. StartTerm starts a term, StartNewTerm stops the old term
    /// first (the session was lost), StopTerm stops the running term, None changes nothing.
    /// </summary>
    public LeaseDecision Assigned(bool holdsLease)
    {
        lock (_gate)
        {
            if (!holdsLease)
            {
                if (!_active)
                    return LeaseDecision.None;
                _active = false;
                _wasLost = false;
                return LeaseDecision.StopTerm;
            }

            if (_active && _wasLost)
            {
                _wasLost = false;
                return LeaseDecision.StartNewTerm;
            }
            if (_active)
                return LeaseDecision.None; // a cooperative rebalance that changed nothing for this member
            _active = true;
            return LeaseDecision.StartTerm;
        }
    }

    public LeaseDecision Revoked(bool heldLease)
    {
        lock (_gate)
        {
            if (!heldLease || !_active)
                return LeaseDecision.None;
            _active = false;
            _wasLost = false;
            return LeaseDecision.StopTerm;
        }
    }

    public LeaseDecision Lost()
    {
        lock (_gate)
        {
            if (!_active)
                return LeaseDecision.None;
            _wasLost = true; // unchanged: the instance keeps its lease until a revocation
            return LeaseDecision.FlushSender;
        }
    }
}

/// <summary>
/// Runs the active/standby lease for one shop (doc 02 §4.3). Nothing is ever produced to the lease topic;
/// the consume loop only drives heartbeats and rebalances. Exactly one member of <c>gateway-lease-&lt;shop&gt;</c>
/// holds partition 0 and runs a term. On host stop the term is stopped as on a revocation, then the consumer
/// is closed; a static member sends no LeaveGroup, so the standby is granted the lease after the 6 s session
/// timeout. A faulted term is fatal: the exception rethrows out of <see cref="ExecuteAsync"/>, as a faulted
/// <see cref="CollectorService"/> does today.
/// </summary>
public sealed class GatewayLease(
    string bootstrap,
    string shop,
    string instanceId,
    GatewayBuffer buffer,
    IReadOnlyCollection<string> machines,
    Func<
        CancellationToken /* term */
        ,
        CancellationToken /* sender */
        ,
        Task
    > whileActive,
    ILogger log
) : BackgroundService
{
    private const int LeasePartition = 0;
    private static readonly TimeSpan ConsumeTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan StandbyRetry = TimeSpan.FromSeconds(10);

    // One gate for a standby's reconcile, a term's reconcile and a term start, so a term start waits for an in-flight standby reconcile to finish.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly LeaseTerm _term = new();
    private readonly List<TopicPartition> _assigned = new(1); // handlers only, under _assignedLock
    private readonly object _assignedLock = new();
    private volatile bool _termRunning;
    private Task? _termTask;
    private CancellationTokenSource? _termCts;
    private CancellationTokenSource? _senderCts;

    /// <summary>
    /// From the term's <see cref="MarkActive"/> (numbering reconciled and resumed) until a revocation; unchanged
    /// by a lost session. Before that, /metrics would report the boot-time numbering as the shop's.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>Called by the term once its numbering is reconciled and resumed.</summary>
    public void MarkActive() => IsActive = _termRunning;

    /// <summary>The gate the standby loop reconciles under; a term's reconcile runs under it too.</summary>
    public SemaphoreSlim StandbyGate => _gate;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var standbyStop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var standby = Task.Run(() => StandbyLoopAsync(standbyStop.Token));
        try
        {
            await Task.Factory.StartNew(
                () => ConsumeLoop(stoppingToken),
                stoppingToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default
            );
        }
        finally
        {
            standbyStop.Cancel();
            await standby;
        }
    }

    private void ConsumeLoop(CancellationToken stoppingToken)
    {
        using var consumer = new ConsumerBuilder<Ignore, byte[]>(
            KafkaConfigs.Lease(bootstrap, shop, instanceId)
        )
            .SetPartitionsAssignedHandler((_, partitions) => Assigned(partitions))
            .SetPartitionsRevokedHandler((_, partitions) => Revoked(partitions))
            .SetPartitionsLostHandler((_, partitions) => Lost(partitions))
            .Build();
        consumer.Subscribe(TopicNames.Lease(shop));
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ThrowIfTermFaulted();
                consumer.Consume(ConsumeTimeout);
            }
        }
        finally
        {
            StopTerm(clearActive: true); // rule 9 / rule 8: stop the term as in a revocation, then Close
            consumer.Close();
        }
    }

    private async Task StandbyLoopAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                if (!_termRunning)
                {
                    await _gate.WaitAsync(stop);
                    try
                    {
                        if (!_termRunning)
                            await PlantLogReader.ReconcileBufferAsync(
                                buffer,
                                bootstrap,
                                machines,
                                log,
                                stop
                            );
                    }
                    finally
                    {
                        _gate.Release();
                    }
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e) when (!stop.IsCancellationRequested)
            {
                // A standby has nothing to lose by waiting.
                log.LogWarning(e, "The standby reconcile failed; retrying after the next 10 s");
            }
            try
            {
                await Task.Delay(StandbyRetry, TimeProvider.System, stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                break;
            }
        }
    }

    // Cooperative-sticky delivers only the *incremental* assignment, so an event with no new partitions can
    // mean "nothing changed" and must not read as "the lease partition is gone". Held partitions are tracked locally.
    private bool HoldsLease()
    {
        lock (_assignedLock)
            return _assigned.Any(p => p.Partition.Value == LeasePartition);
    }

    private void Assigned(List<TopicPartition> partitions)
    {
        lock (_assignedLock)
        {
            foreach (var p in partitions)
                if (!_assigned.Contains(p))
                    _assigned.Add(p);
        }
        var holdsLease = HoldsLease();
        log.LogInformation(
            "Assigned partitions [{Assigned}] to {Instance}; the lease partition is {Held}held",
            string.Join(",", partitions.Select(p => p.Partition.Value)),
            instanceId,
            holdsLease ? "" : "not "
        );
        switch (_term.Assigned(holdsLease))
        {
            case LeaseDecision.StartTerm:
                StartTerm();
                break;
            case LeaseDecision.StartNewTerm: // rule 4: every assignment after a lost session fences, reconciles, resumes and backfills
                log.LogWarning(
                    "{Instance} was re-assigned the lease after a lost session; stopping the old term and starting a new one",
                    instanceId
                );
                StopTerm(clearActive: false);
                StartTerm();
                break;
            case LeaseDecision.StopTerm: // rule 6
                log.LogWarning(
                    "{Instance} no longer holds the lease partition; stopping the term",
                    instanceId
                );
                StopTerm(clearActive: true);
                break;
        }
    }

    private void Revoked(List<TopicPartitionOffset> partitions)
    {
        lock (_assignedLock)
        {
            foreach (var p in partitions)
                _assigned.Remove(p.TopicPartition);
        }
        var heldLease = partitions.Any(p => p.TopicPartition.Partition.Value == LeasePartition);
        if (_term.Revoked(heldLease) != LeaseDecision.StopTerm)
            return;
        log.LogWarning(
            "The lease partition was revoked from {Instance}; stopping the term",
            instanceId
        );
        StopTerm(clearActive: true);
    }

    private void Lost(List<TopicPartitionOffset> partitions)
    {
        lock (_assignedLock)
            _assigned.Clear();
        var heldLease = partitions.Any(p => p.TopicPartition.Partition.Value == LeasePartition);
        if (!heldLease)
            return;
        if (_term.Lost() != LeaseDecision.FlushSender)
            return;
        log.LogWarning(
            "The lease session for {Instance} expired; the sender is flushing and the collector keeps writing",
            instanceId
        );
        // Rule 7: cancel only the sender token. Off this thread: Cancel() would run the sender's flush (up to
        // 20 s with the broker gone) inline, and librdkafka cannot rejoin until this handler returns.
        _ = _senderCts?.CancelAsync();
    }

    private void ThrowIfTermFaulted()
    {
        if (_termTask is { IsFaulted: true } term)
        {
            var exception = term.Exception?.InnerException ?? term.Exception!;
            log.LogError(
                exception,
                "The term for {Instance} faulted; the host is stopping",
                instanceId
            );
            throw exception;
        }
    }

    private void StartTerm()
    {
        _gate.Wait(); // a term start waits for an in-flight standby reconcile to finish (rule 12)
        try
        {
            var termCts = new CancellationTokenSource();
            var senderCts = CancellationTokenSource.CreateLinkedTokenSource(termCts.Token);
            _termCts = termCts;
            _senderCts = senderCts;
            _termRunning = true;
            log.LogInformation("The gateway {Instance} of shop {Shop} is active", instanceId, shop);
            _termTask = Task.Run(() => whileActive(termCts.Token, senderCts.Token));
        }
        finally
        {
            _gate.Release();
        }
    }

    private void StopTerm(bool clearActive)
    {
        var termCts = _termCts;
        if (termCts is null)
            return;
        var senderCts = _senderCts;
        var term = _termTask;
        // The term token stops the collector and ends the term; the sender token goes with it (rule 10).
        termCts.Cancel();
        senderCts?.Cancel();
        try
        {
            term?.Wait(); // wait, inside the handler, until whileActive returns, so the next holder is assigned only after this one has stopped
        }
        catch (AggregateException) { } // a faulted term surfaces from the consume loop (rule 8)
        catch (ObjectDisposedException) { }
        _termCts = null;
        _senderCts = null;
        _termTask = null;
        _termRunning = false;
        termCts.Dispose();
        senderCts?.Dispose();
        if (clearActive)
            IsActive = false; // only after whileActive has returned (rule 5)
    }
}
