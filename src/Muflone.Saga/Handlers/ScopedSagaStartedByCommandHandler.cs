#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Muflone.Messages.Commands;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Muflone.Saga.Handlers;

/// <summary>
/// <see cref="SagaStartedByCommandHandler{TCommand}" /> for sagas whose dependencies are scoped.
/// </summary>
/// <remarks>
/// The non-scoped handler resolves its sagas once, when it is built. That is correct only if the handler
/// itself is resolved per message; if the transport keeps one instance alive, the sagas it captured -
/// and everything they depend on, a database session included - outlive the scope they were meant for.
/// This one opens a scope per message instead, which costs a resolution and removes the question.
/// </remarks>
public sealed class ScopedSagaStartedByCommandHandler<TCommand> : ICommandHandlerAsync<TCommand>
	where TCommand : Command
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger _logger;

	public ScopedSagaStartedByCommandHandler(IServiceScopeFactory scopeFactory, ILoggerFactory loggerFactory)
	{
		_scopeFactory = scopeFactory;
		_logger = loggerFactory.CreateLogger(typeof(ScopedSagaStartedByCommandHandler<TCommand>));
	}

	public async Task HandleAsync(TCommand message, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		await using var scope = _scopeFactory.CreateAsyncScope();
		var handlers = scope.ServiceProvider.GetServices<ISagaStartedByAsync<TCommand>>();

		await SagaFanOut.DeliverAsync(handlers, message, (h, m) => h.StartedByAsync(m), _logger);
	}

	public void Dispose()
	{
	}
}
