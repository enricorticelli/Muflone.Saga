#nullable enable
using Microsoft.Extensions.Logging;
using Muflone.Messages.Commands;
using Muflone.Messages.Events;
using Muflone.Persistence;
using Muflone.Saga.Persistence;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Muflone.Saga;

/// <summary>
/// A <see cref="Saga{TCommand,TSagaState}" /> whose state carries a lifecycle: it can be loaded, found by
/// business key, deduplicated against redeliveries, and moved to a terminal outcome.
/// </summary>
/// <remarks>
/// <see cref="Saga{TCommand,TSagaState}" /> leaves all of that to the implementer, and every long-running
/// saga ends up writing the same five helpers — usually with subtly different logging and, more
/// dangerously, with the state saved <i>after</i> the command is sent.
/// </remarks>
public abstract class LifecycleSaga<TStartCommand, TSagaState> : Saga<TStartCommand, TSagaState>
	where TStartCommand : Command
	where TSagaState : SagaStateBase, new()
{
	protected LifecycleSaga(
		IServiceBus serviceBus,
		ISagaRepository repository,
		ILoggerFactory loggerFactory)
		: this(serviceBus, repository, null, loggerFactory)
	{
	}

	/// <param name="stateLocator">
	/// Only needed by sagas that stay open past their own flow and must be reachable by an event carrying
	/// somebody else's correlation id. Sagas that do not use the other constructor.
	/// </param>
	protected LifecycleSaga(
		IServiceBus serviceBus,
		ISagaRepository repository,
		ISagaStateLocator? stateLocator,
		ILoggerFactory loggerFactory)
		: base(serviceBus, repository, loggerFactory)
	{
		Logger = loggerFactory.CreateLogger(GetType());
		SagaName = GetType().Name;
		StateLocator = stateLocator;
	}

	protected ILogger Logger { get; }

	protected string SagaName { get; }

	private ISagaStateLocator? StateLocator { get; }

	/// <summary>The saga state for this correlation id, or <c>null</c> if there is not one yet.</summary>
	protected async Task<TSagaState?> LoadState(Guid correlationId)
	{
		var state = await Repository.GetByIdAsync<TSagaState>(correlationId);
		if (state is not null) return state;

		Logger.LogDebug(
			"[{SagaName}.HandleAsync] No existing saga state found. Starting new saga. CorrelationId={CorrelationId}",
			SagaName,
			correlationId);

		return null;
	}

	/// <summary>
	/// The open states watching the given business key.
	/// </summary>
	/// <remarks>
	/// A fallback for <see cref="LoadState" />, not a replacement: see <see cref="ISagaStateLocator" />
	/// for when the two differ.
	/// </remarks>
	protected async Task<IReadOnlyList<TSagaState>> LoadStatesByBusinessKey(string stateField, Guid? value)
	{
		if (value is null || value == Guid.Empty)
		{
			Logger.LogDebug(
				"[{SagaName}.HandleAsync] No business key to resolve the saga by. StateField={StateField}",
				SagaName,
				stateField);
			return Array.Empty<TSagaState>();
		}

		if (StateLocator is null)
		{
			// Configuration, not data: this saga asks to be addressed by business key and was not given a
			// locator. Logged at Error because the symptom is otherwise an absence - the late event
			// silently discarded, which is exactly the defect this fallback exists to close.
			Logger.LogError(
				"[{SagaName}.HandleAsync] No ISagaStateLocator registered: cannot resolve the saga by {StateField}. Late events addressed to this saga are being discarded.",
				SagaName,
				stateField);
			return Array.Empty<TSagaState>();
		}

		var states = await StateLocator.FindOpenStatesBy<TSagaState>(stateField, value.Value);

		Logger.LogDebug(
			"[{SagaName}.HandleAsync] Resolved {Count} open saga(s) by {StateField}={Value}",
			SagaName,
			states.Count,
			stateField,
			value);

		return states;
	}

	/// <summary>
	/// Registers the event against the state and says whether the saga should act on it.
	/// </summary>
	/// <remarks>
	/// Returning <c>false</c> means the broker redelivered a message this saga has already handled. The
	/// state is only durable once it is saved, so a step that sends its command before saving will run
	/// again on every redelivery - the accepted message id is not on the stored state yet.
	/// </remarks>
	protected bool TryAcceptEvent(TSagaState state, Event @event)
	{
		// The saga's own correlation id, not the event's: a transport that carries it in the user
		// properties rather than in the headers would otherwise log Guid.Empty on every line.
		var correlationId = state.CorrelationId;
		var eventName = @event.GetType().Name;

		if (!state.TryRegisterEvent(@event.MessageId))
		{
			Logger.LogDebug(
				"[{SagaName}.HandleAsync] Event ignored: duplicate MessageId. EventName={EventName}, CorrelationId={CorrelationId}, MessageId={MessageId}",
				SagaName,
				eventName,
				correlationId,
				@event.MessageId);
			return false;
		}

		Logger.LogDebug(
			"[{SagaName}.HandleAsync] Event accepted: EventName={EventName}, CorrelationId={CorrelationId}, MessageId={MessageId}, Status={Status}",
			SagaName,
			eventName,
			correlationId,
			@event.MessageId,
			state.Status);

		return true;
	}

	protected async Task SendCommand(Command command, Guid correlationId)
	{
		Logger.LogDebug(
			"[{SagaName}.SendCommandAsync] Sending command: CorrelationId={CorrelationId}, CommandType={CommandType}",
			SagaName,
			correlationId,
			command.GetType().Name);

		await ServiceBus.SendAsync(command);

		Logger.LogDebug(
			"[{SagaName}.SendCommandAsync] Command sent: CorrelationId={CorrelationId}, CommandType={CommandType}",
			SagaName,
			correlationId,
			command.GetType().Name);
	}

	protected Task SaveState(Guid correlationId, TSagaState state)
	{
		state.UpdatedAtUtc = DateTime.UtcNow;

		Logger.LogDebug(
			"[{SagaName}.PersistStateAsync] Saving saga state: CorrelationId={CorrelationId}, Status={Status}",
			SagaName,
			correlationId,
			state.Status);

		return Repository.SaveAsync(correlationId, state);
	}

	/// <summary>
	/// Marks the saga done and removes it from the store.
	/// </summary>
	/// <remarks>
	/// The final state is saved before the document is dropped, so a store that keeps history records the
	/// outcome and not just the disappearance.
	/// </remarks>
	protected async Task CompleteSaga(Guid correlationId, TSagaState state)
	{
		state.Status = SagaStatus.Completed;
		state.UpdatedAtUtc = DateTime.UtcNow;

		await Repository.SaveAsync(correlationId, state);

		Logger.LogDebug(
			"[{SagaName}.PersistStateAsync] Completing saga: CorrelationId={CorrelationId}, Status={Status}",
			SagaName,
			correlationId,
			state.Status);

		await Repository.CompleteAsync(correlationId);
	}

	/// <summary>
	/// Ends the saga on a legitimate outcome it was asked for. The document stays: a cancellation is a
	/// fact about the process, and an event arriving afterwards must find it rather than reopen the saga.
	/// </summary>
	protected async Task CancelSaga(Guid correlationId, TSagaState state, string? reason = null)
	{
		state.Status = SagaStatus.Cancelled;
		state.FailureReason = string.IsNullOrWhiteSpace(reason) ? state.FailureReason : reason;
		await SaveState(correlationId, state);
	}

	/// <summary>
	/// Ends the saga on a step that could not be carried out. The document stays, so the failure can be
	/// counted and investigated instead of vanishing.
	/// </summary>
	protected async Task FailSaga(Guid correlationId, TSagaState state, string? reason = null)
	{
		state.Status = SagaStatus.Failed;
		state.FailureReason = string.IsNullOrWhiteSpace(reason) ? state.FailureReason : reason;
		await SaveState(correlationId, state);
	}
}
