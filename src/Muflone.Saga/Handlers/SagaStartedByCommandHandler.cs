#nullable enable
using Microsoft.Extensions.Logging;
using Muflone.Messages.Commands;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Muflone.Saga.Handlers;

/// <summary>
/// Lets a command start a saga through Muflone's ordinary command pipeline.
/// </summary>
/// <remarks>
/// A saga is started by <see cref="ISagaStartedByAsync{TCommand}" />, which no transport knows how to
/// route on its own. Registering this open generic closed over the command makes the saga reachable by
/// whatever transport is configured, with no transport-specific consumer to write and host.
/// </remarks>
public sealed class SagaStartedByCommandHandler<TCommand> : ICommandHandlerAsync<TCommand>
	where TCommand : Command
{
	private readonly ISagaStartedByAsync<TCommand>[] _handlers;
	private readonly ILogger _logger;

	public SagaStartedByCommandHandler(
		IEnumerable<ISagaStartedByAsync<TCommand>> sagaStartedByHandlers,
		ILoggerFactory loggerFactory)
	{
		_handlers = sagaStartedByHandlers.ToArray();
		_logger = loggerFactory.CreateLogger(typeof(SagaStartedByCommandHandler<TCommand>));
	}

	public Task HandleAsync(TCommand message, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		return SagaFanOut.DeliverAsync(_handlers, message, (h, m) => h.StartedByAsync(m), _logger);
	}

	public void Dispose()
	{
	}
}
