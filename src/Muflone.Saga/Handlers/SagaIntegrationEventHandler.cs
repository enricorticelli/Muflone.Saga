#nullable enable
using Microsoft.Extensions.Logging;
using Muflone.Messages.Events;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Muflone.Saga.Handlers;

/// <summary>
/// Lets an integration event reach a saga through Muflone's ordinary event pipeline.
/// </summary>
/// <remarks>
/// The counterpart of <see cref="SagaStartedByCommandHandler{TCommand}" /> for the steps after the first.
/// </remarks>
public sealed class SagaIntegrationEventHandler<TEvent> : IIntegrationEventHandlerAsync<TEvent>
	where TEvent : Event, IIntegrationEvent
{
	private readonly ISagaEventHandlerAsync<TEvent>[] _handlers;
	private readonly ILogger _logger;

	public SagaIntegrationEventHandler(
		IEnumerable<ISagaEventHandlerAsync<TEvent>> sagaEventHandlers,
		ILoggerFactory loggerFactory)
	{
		_handlers = sagaEventHandlers.ToArray();
		_logger = loggerFactory.CreateLogger(typeof(SagaIntegrationEventHandler<TEvent>));
	}

	public Task HandleAsync(TEvent message, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		return SagaFanOut.DeliverAsync(_handlers, message, (h, m) => h.HandleAsync(m), _logger);
	}

	public void Dispose()
	{
	}
}
