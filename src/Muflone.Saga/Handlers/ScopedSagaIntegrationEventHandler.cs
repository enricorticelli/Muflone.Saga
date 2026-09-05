#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Muflone.Messages.Events;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Muflone.Saga.Handlers;

/// <summary>
/// <see cref="SagaIntegrationEventHandler{TEvent}" /> for sagas whose dependencies are scoped. See
/// <see cref="ScopedSagaStartedByCommandHandler{TCommand}" /> for when the distinction matters.
/// </summary>
public sealed class ScopedSagaIntegrationEventHandler<TEvent> : IIntegrationEventHandlerAsync<TEvent>
	where TEvent : Event, IIntegrationEvent
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger _logger;

	public ScopedSagaIntegrationEventHandler(IServiceScopeFactory scopeFactory, ILoggerFactory loggerFactory)
	{
		_scopeFactory = scopeFactory;
		_logger = loggerFactory.CreateLogger(typeof(ScopedSagaIntegrationEventHandler<TEvent>));
	}

	public async Task HandleAsync(TEvent message, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		await using var scope = _scopeFactory.CreateAsyncScope();
		var handlers = scope.ServiceProvider.GetServices<ISagaEventHandlerAsync<TEvent>>();

		await SagaFanOut.DeliverAsync(handlers, message, (h, m) => h.HandleAsync(m), _logger);
	}

	public void Dispose()
	{
	}
}
