using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Muflone.Core;
using Muflone.Messages;
using Muflone.Messages.Events;
using Muflone.Saga.Handlers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Muflone.Saga.Tests
{
	public class SagaMessageHandlerTests
	{
		private readonly Guid _correlationId = Guid.NewGuid();

		[Fact]
		public async Task SagaStartedByCommandHandler_ForwardsTheCommandToEverySaga()
		{
			// Several sagas can legitimately be started by the same command; the handler must not stop
			// at the first one.
			var counter = new Counter();
			var handler = new SagaStartedByCommandHandler<StartOrderProcess>(
				new ISagaStartedByAsync<StartOrderProcess>[] { new RecordingStarter(counter), new RecordingStarter(counter) },
				NullLoggerFactory.Instance);

			await handler.HandleAsync(StartCommand());

			Assert.Equal(2, counter.Value);
		}

		[Fact]
		public async Task SagaIntegrationEventHandler_ForwardsTheEventToEverySaga()
		{
			var first = new RecordingEventSaga();
			var second = new RecordingEventSaga();
			var handler = new SagaIntegrationEventHandler<OrderCompleted>(
				new ISagaEventHandlerAsync<OrderCompleted>[] { first, second }, NullLoggerFactory.Instance);

			await handler.HandleAsync(new OrderCompleted(new OrderId(Guid.NewGuid()), _correlationId));

			Assert.Equal(1, first.Calls);
			Assert.Equal(1, second.Calls);
		}

		[Fact]
		public async Task SagaIntegrationEventHandler_WhenASagaThrows_RethrowsSoTheMessageIsNacked()
		{
			// Swallowing here would ack a message nobody handled.
			var handler = new SagaIntegrationEventHandler<OrderCompleted>(
				new ISagaEventHandlerAsync<OrderCompleted>[] { new ThrowingEventSaga() }, NullLoggerFactory.Instance);

			await Assert.ThrowsAsync<InvalidOperationException>(
				() => handler.HandleAsync(new OrderCompleted(new OrderId(Guid.NewGuid()), _correlationId)));
		}

		[Fact]
		public async Task SagaIntegrationEventHandler_WhenCancelledBeforeStarting_DoesNotReachAnySaga()
		{
			var saga = new RecordingEventSaga();
			var handler = new SagaIntegrationEventHandler<OrderCompleted>(
				new ISagaEventHandlerAsync<OrderCompleted>[] { saga }, NullLoggerFactory.Instance);

			await Assert.ThrowsAsync<OperationCanceledException>(() => handler.HandleAsync(
				new OrderCompleted(new OrderId(Guid.NewGuid()), _correlationId), new CancellationToken(true)));

			Assert.Equal(0, saga.Calls);
		}

		[Fact]
		public async Task ScopedSagaIntegrationEventHandler_ResolvesTheSagaOncePerMessage()
		{
			// The reason the scoped variant exists: the non-scoped one captures its sagas when built, so
			// a long-lived handler keeps a scoped saga - and its dependencies - alive past their scope.
			var counter = new Counter();
			var services = new ServiceCollection()
				.AddSingleton(counter)
				.AddScoped<ISagaEventHandlerAsync<OrderCompleted>, CountingEventSaga>()
				.BuildServiceProvider();
			var handler = new ScopedSagaIntegrationEventHandler<OrderCompleted>(
				services.GetRequiredService<IServiceScopeFactory>(), NullLoggerFactory.Instance);

			await handler.HandleAsync(new OrderCompleted(new OrderId(Guid.NewGuid()), _correlationId));
			await handler.HandleAsync(new OrderCompleted(new OrderId(Guid.NewGuid()), _correlationId));

			Assert.Equal(2, counter.Value);
		}

		[Fact]
		public async Task ScopedSagaStartedByCommandHandler_ForwardsTheCommand()
		{
			var counter = new Counter();
			var services = new ServiceCollection()
				.AddSingleton(counter)
				.AddSagaStarter<StartOrderProcess, RecordingStarter>()
				.BuildServiceProvider();
			var handler = new ScopedSagaStartedByCommandHandler<StartOrderProcess>(
				services.GetRequiredService<IServiceScopeFactory>(), NullLoggerFactory.Instance);

			await handler.HandleAsync(StartCommand());

			Assert.Equal(1, counter.Value);
		}

		[Fact]
		public void AddSagaEventHandler_RegistersTheSagaUnderTheSagaInterface()
		{
			var services = new ServiceCollection()
				.AddSagaEventHandler<OrderCompleted, RecordingEventSaga>()
				.BuildServiceProvider();

			Assert.NotNull(services.GetService<ISagaEventHandlerAsync<OrderCompleted>>());
		}

		[Fact]
		public void AddSagaEventHandler_CalledForTwoSagas_RegistersBoth()
		{
			// Registering the saga separately from the Muflone handler is what allows this.
			var services = new ServiceCollection()
				.AddSingleton(new Counter())
				.AddSagaEventHandler<OrderCompleted, RecordingEventSaga>()
				.AddSagaEventHandler<OrderCompleted, CountingEventSaga>()
				.BuildServiceProvider();

			Assert.Equal(2, services.GetServices<ISagaEventHandlerAsync<OrderCompleted>>().Count());
		}

		private StartOrderProcess StartCommand()
			=> new StartOrderProcess(new OrderId(Guid.NewGuid()), _correlationId, Guid.NewGuid());

		public class OrderCompleted : IntegrationEvent
		{
			public OrderCompleted(IDomainId aggregateId, Guid correlationId) : base(aggregateId, correlationId)
			{
			}
		}

		/// <summary>
		/// Shared counter. It is a service and not a static field because xUnit gives no order to the
		/// tests in a class, and a static would make one test's count depend on which ran first.
		/// </summary>
		public sealed class Counter
		{
			private int _value;

			public int Value => Volatile.Read(ref _value);

			public void Increment() => Interlocked.Increment(ref _value);
		}

		public sealed class RecordingStarter : ISagaStartedByAsync<StartOrderProcess>
		{
			private readonly Counter _counter;

			public RecordingStarter(Counter counter) => _counter = counter;

			public Task StartedByAsync(StartOrderProcess command)
			{
				_counter.Increment();
				return Task.CompletedTask;
			}

			public void Dispose()
			{
			}
		}

		public sealed class RecordingEventSaga : ISagaEventHandlerAsync<OrderCompleted>
		{
			public int Calls;

			public Task HandleAsync(OrderCompleted @event)
			{
				Calls++;
				return Task.CompletedTask;
			}

			public void Dispose()
			{
			}
		}

		/// <summary>Counts how many times it is built, to show when a new scope was opened.</summary>
		public sealed class CountingEventSaga : ISagaEventHandlerAsync<OrderCompleted>
		{
			public CountingEventSaga(Counter counter) => counter.Increment();

			public Task HandleAsync(OrderCompleted @event) => Task.CompletedTask;

			public void Dispose()
			{
			}
		}

		public sealed class ThrowingEventSaga : ISagaEventHandlerAsync<OrderCompleted>
		{
			public Task HandleAsync(OrderCompleted @event) => throw new InvalidOperationException("saga failed");

			public void Dispose()
			{
			}
		}
	}
}
