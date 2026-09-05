using Microsoft.Extensions.Logging.Abstractions;
using Muflone.Core;
using Muflone.Messages;
using Muflone.Messages.Commands;
using Muflone.Messages.Events;
using Muflone.Persistence;
using Muflone.Saga.Persistence;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Muflone.Saga.Tests
{
	public class OrderId : IDomainId
	{
		public OrderId(Guid value) => Value = value.ToString();

		public string Value { get; }
	}

	public class StartOrderProcess : Command
	{
		public StartOrderProcess(IDomainId aggregateId, Guid correlationId, Guid orderId) : base(aggregateId)
		{
			OrderId = orderId;
			// A command has no Headers: the correlation id travels in the user properties.
			UserProperties[HeadersNames.CorrelationId] = correlationId.ToString();
		}

		public Guid OrderId { get; }
	}

	public class ShipOrder : Command
	{
		public ShipOrder(IDomainId aggregateId, Guid correlationId) : base(aggregateId)
			=> UserProperties[HeadersNames.CorrelationId] = correlationId.ToString();
	}

	public class OrderShipped : DomainEvent
	{
		public OrderShipped(IDomainId aggregateId, Guid correlationId) : base(aggregateId, correlationId)
		{
		}
	}

	public class OrderProcessState : SagaStateBase
	{
		public Guid OrderId { get; set; }
	}

	/// <summary>
	/// A saga that does nothing interesting, so the tests are about
	/// <see cref="LifecycleSaga{TStartCommand,TSagaState}" /> and not about a process.
	/// </summary>
	public class OrderProcessSaga : LifecycleSaga<StartOrderProcess, OrderProcessState>,
		ISagaEventHandlerAsync<OrderShipped>
	{
		public OrderProcessSaga(IServiceBus serviceBus, ISagaRepository repository)
			: base(serviceBus, repository, NullLoggerFactory.Instance)
		{
		}

		public OrderProcessSaga(IServiceBus serviceBus, ISagaRepository repository, ISagaStateLocator stateLocator)
			: base(serviceBus, repository, stateLocator, NullLoggerFactory.Instance)
		{
		}

		public override async Task StartedByAsync(StartOrderProcess command)
		{
			var correlationId = Guid.Parse(command.UserProperties[HeadersNames.CorrelationId].ToString());

			var state = new OrderProcessState
			{
				CorrelationId = correlationId,
				OrderId = command.OrderId,
				Status = SagaStatus.InProgress
			};

			await SaveState(correlationId, state);
			await SendCommand(new ShipOrder(command.AggregateId, correlationId), correlationId);
		}

		public async Task HandleAsync(OrderShipped @event)
		{
			var correlationId = @event.Headers.CorrelationId;

			var state = await LoadState(correlationId);
			if (state is null || !TryAcceptEvent(state, @event))
				return;

			await CompleteSaga(correlationId, state);
		}

		public Task<IReadOnlyList<OrderProcessState>> OpenStatesByOrder(Guid? orderId)
			=> LoadStatesByBusinessKey(nameof(OrderProcessState.OrderId), orderId);

		public Task Cancel(Guid correlationId, OrderProcessState state, string reason)
			=> CancelSaga(correlationId, state, reason);

		public Task Fail(Guid correlationId, OrderProcessState state, string reason)
			=> FailSaga(correlationId, state, reason);

		public Task<OrderProcessState> State(Guid correlationId) => LoadState(correlationId);
	}
}
