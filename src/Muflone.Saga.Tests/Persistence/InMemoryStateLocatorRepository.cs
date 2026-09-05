using Muflone.Saga.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Muflone.Saga.Tests.Persistence
{
	/// <summary>
	/// An <see cref="ISagaRepository" /> that is also an <see cref="ISagaStateLocator" />, so a state saved
	/// by a test is found again by business key without a second double to keep in step.
	/// </summary>
	/// <remarks>
	/// State is per instance, unlike <see cref="InMemorySagaRepository" />, and it keeps an ordered journal
	/// of its writes: the order of save and complete is part of the contract a saga has to honour, so a
	/// test has to be able to see it.
	/// </remarks>
	public sealed class InMemoryStateLocatorRepository : ISagaRepository, ISagaStateLocator
	{
		private readonly Dictionary<Guid, object> _activeStates = new Dictionary<Guid, object>();
		private readonly Dictionary<Guid, object> _completedStates = new Dictionary<Guid, object>();
		private readonly HashSet<Guid> _completedCorrelationIds = new HashSet<Guid>();
		private readonly List<string> _operations = new List<string>();

		/// <summary>Ordered journal of the writes, as <c>save:{id}</c> and <c>complete:{id}</c>.</summary>
		public IReadOnlyList<string> Operations => _operations;

		public Task SaveAsync<TSagaState>(Guid correlationId, TSagaState sagaState) where TSagaState : class, new()
		{
			_activeStates[correlationId] = sagaState;
			_operations.Add($"save:{correlationId}");
			return Task.CompletedTask;
		}

		public Task<TSagaState> GetByIdAsync<TSagaState>(Guid id) where TSagaState : class, new()
		{
			if (_activeStates.TryGetValue(id, out var active) && active is TSagaState typedActive)
				return Task.FromResult(typedActive);

			if (_completedStates.TryGetValue(id, out var completed) && completed is TSagaState typedCompleted)
				return Task.FromResult(typedCompleted);

			return Task.FromResult<TSagaState>(null);
		}

		public Task CompleteAsync(Guid correlationId)
		{
			if (_activeStates.TryGetValue(correlationId, out var state))
			{
				_activeStates.Remove(correlationId);
				_completedStates[correlationId] = state;
			}

			_completedCorrelationIds.Add(correlationId);
			_operations.Add($"complete:{correlationId}");
			return Task.CompletedTask;
		}

		public Task<IReadOnlyList<TSagaState>> FindOpenStatesBy<TSagaState>(
			string stateField,
			Guid value,
			CancellationToken ct = default)
			where TSagaState : SagaStateBase, new()
		{
			if (string.IsNullOrWhiteSpace(stateField) || value == Guid.Empty)
				return Task.FromResult<IReadOnlyList<TSagaState>>(Array.Empty<TSagaState>());

			// Open states only, like a real store would filter: a cancelled or failed document stays in
			// the collection and must not be reopened by a late event.
			var states = _activeStates.Values
				.OfType<TSagaState>()
				.Where(state => state.Status == SagaStatus.Started || state.Status == SagaStatus.InProgress)
				.Where(state => Equals(PropertyValue(state, stateField), value))
				.ToList();

			return Task.FromResult<IReadOnlyList<TSagaState>>(states);
		}

		public bool WasCompleted(Guid correlationId) => _completedCorrelationIds.Contains(correlationId);

		private static object PropertyValue(object state, string propertyName)
			=> state.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(state);
	}
}
