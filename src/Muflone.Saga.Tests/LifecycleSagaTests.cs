using Muflone.Saga.Tests.Persistence;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Muflone.Saga.Tests
{
	public class LifecycleSagaTests
	{
		private readonly Guid _correlationId = Guid.NewGuid();
		private readonly Guid _orderId = Guid.NewGuid();
		private readonly InMemoryStateLocatorRepository _repository = new InMemoryStateLocatorRepository();
		private readonly RecordingServiceBus _serviceBus = new RecordingServiceBus();

		[Fact]
		public async Task LoadState_WhenNothingWasEverSaved_ReturnsNull()
		{
			var saga = SagaWithLocator();

			Assert.Null(await saga.State(_correlationId));
		}

		[Fact]
		public async Task StartedByAsync_SavesTheStateBeforeSendingTheCommand()
		{
			// The state is only durable once saved. A step that sends first runs again on every
			// redelivery, because the accepted message id is not on the stored state yet.
			var saga = SagaWithLocator();

			await saga.StartedByAsync(StartCommand());

			Assert.Equal($"save:{_correlationId}", _repository.Operations.First());
			Assert.Single(_serviceBus.SentCommands.OfType<ShipOrder>());
		}

		[Fact]
		public async Task TryAcceptEvent_WhenTheSameMessageIsRedelivered_ActsOnlyOnce()
		{
			var saga = SagaWithLocator();
			await saga.StartedByAsync(StartCommand());
			var shipped = new OrderShipped(new OrderId(_orderId), _correlationId);

			await saga.HandleAsync(shipped);
			await saga.HandleAsync(shipped);

			Assert.Single(_repository.Operations, o => o == $"complete:{_correlationId}");
		}

		[Fact]
		public async Task TryAcceptEvent_WhenTheMessageIsNew_ActsOnIt()
		{
			var saga = SagaWithLocator();
			await saga.StartedByAsync(StartCommand());

			await saga.HandleAsync(new OrderShipped(new OrderId(_orderId), _correlationId));

			var state = await saga.State(_correlationId);
			Assert.Equal(SagaStatus.Completed, state.Status);
		}

		[Fact]
		public async Task CompleteSaga_SavesTheFinalStateBeforeRemovingTheDocument()
		{
			// Otherwise a store that keeps history records the disappearance and not the outcome.
			var saga = SagaWithLocator();
			await saga.StartedByAsync(StartCommand());

			await saga.HandleAsync(new OrderShipped(new OrderId(_orderId), _correlationId));

			var tail = _repository.Operations.TakeLast(2).ToList();
			Assert.Equal(new[] { $"save:{_correlationId}", $"complete:{_correlationId}" }, tail);
			Assert.True(_repository.WasCompleted(_correlationId));
		}

		[Fact]
		public async Task CancelSaga_KeepsTheDocumentAndRecordsTheReason()
		{
			// A cancellation is a fact about the process: an event arriving afterwards must find it
			// rather than reopen the saga.
			var saga = SagaWithLocator();
			await saga.StartedByAsync(StartCommand());

			await saga.Cancel(_correlationId, await saga.State(_correlationId), "cancelled by operator");

			var state = await saga.State(_correlationId);
			Assert.Equal(SagaStatus.Cancelled, state.Status);
			Assert.Equal("cancelled by operator", state.FailureReason);
			Assert.False(_repository.WasCompleted(_correlationId));
		}

		[Fact]
		public async Task FailSaga_KeepsTheDocumentSoTheFailureCanBeCounted()
		{
			var saga = SagaWithLocator();
			await saga.StartedByAsync(StartCommand());

			await saga.Fail(_correlationId, await saga.State(_correlationId), "carrier rejected the shipment");

			var state = await saga.State(_correlationId);
			Assert.Equal(SagaStatus.Failed, state.Status);
			Assert.Equal("carrier rejected the shipment", state.FailureReason);
			Assert.False(_repository.WasCompleted(_correlationId));
		}

		[Fact]
		public async Task FailSaga_WithoutAReason_KeepsTheOneAlreadyRecorded()
		{
			var saga = SagaWithLocator();
			await saga.StartedByAsync(StartCommand());
			await saga.Fail(_correlationId, await saga.State(_correlationId), "the original cause");

			await saga.Fail(_correlationId, await saga.State(_correlationId), null);

			var state = await saga.State(_correlationId);
			Assert.Equal("the original cause", state.FailureReason);
		}

		[Fact]
		public async Task LoadStatesByBusinessKey_FindsTheOpenSagaWatchingThatKey()
		{
			// The case this exists for: an event raised outside the saga's own flow carries somebody
			// else's correlation id, so the business key is the only shared address.
			var saga = SagaWithLocator();
			await saga.StartedByAsync(StartCommand());

			var states = await saga.OpenStatesByOrder(_orderId);

			Assert.Equal(_correlationId, Assert.Single(states).CorrelationId);
		}

		[Fact]
		public async Task LoadStatesByBusinessKey_DoesNotReturnASagaThatAlreadyEnded()
		{
			var saga = SagaWithLocator();
			await saga.StartedByAsync(StartCommand());
			await saga.Cancel(_correlationId, await saga.State(_correlationId), "cancelled");

			Assert.Empty(await saga.OpenStatesByOrder(_orderId));
		}

		[Fact]
		public async Task LoadStatesByBusinessKey_ForAnotherKey_FindsNothing()
		{
			var saga = SagaWithLocator();
			await saga.StartedByAsync(StartCommand());

			Assert.Empty(await saga.OpenStatesByOrder(Guid.NewGuid()));
		}

		[Theory]
		[InlineData(null)]
		[InlineData("00000000-0000-0000-0000-000000000000")]
		public async Task LoadStatesByBusinessKey_WithoutAKey_FindsNothing(string rawKey)
		{
			var saga = SagaWithLocator();
			await saga.StartedByAsync(StartCommand());

			Assert.Empty(await saga.OpenStatesByOrder(rawKey is null ? (Guid?)null : Guid.Parse(rawKey)));
		}

		[Fact]
		public async Task LoadStatesByBusinessKey_WithoutALocator_FindsNothingInsteadOfThrowing()
		{
			// Misconfiguration, not data. The saga keeps working for everything it can address by
			// correlation id; only the fallback is unavailable.
			var saga = new OrderProcessSaga(_serviceBus, _repository);
			await saga.StartedByAsync(StartCommand());

			Assert.Empty(await saga.OpenStatesByOrder(_orderId));
		}

		private OrderProcessSaga SagaWithLocator() => new OrderProcessSaga(_serviceBus, _repository, _repository);

		private StartOrderProcess StartCommand()
			=> new StartOrderProcess(new OrderId(_orderId), _correlationId, _orderId);
	}
}
