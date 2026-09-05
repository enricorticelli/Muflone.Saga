#nullable enable
using System;
using System.Collections.Generic;

namespace Muflone.Saga;

/// <summary>
/// The bookkeeping every long-running saga state needs, whatever the process it orchestrates.
/// </summary>
public abstract class SagaStateBase
{
	public Guid CorrelationId { get; set; }

	public SagaStatus Status { get; set; } = SagaStatus.Started;

	/// <summary>Why the saga stopped, when it stopped badly. Null while nothing has gone wrong.</summary>
	public string? FailureReason { get; set; }

	public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

	/// <summary>
	/// The messages this saga has already acted on, by message id. It is part of the state, and therefore
	/// persisted with it, because that is the only place a redelivery can be recognised from.
	/// </summary>
	public HashSet<Guid> ProcessedEventIds { get; set; } = new();

	/// <summary>
	/// Registers a message as handled, and says whether it is the first time.
	/// </summary>
	/// <remarks>
	/// A message with an empty id is always accepted: a transport that does not stamp message ids gives us
	/// nothing to deduplicate on, and silently dropping every such message would be far worse than
	/// handling one twice.
	/// </remarks>
	public bool TryRegisterEvent(Guid eventId) => eventId == Guid.Empty || ProcessedEventIds.Add(eventId);
}
