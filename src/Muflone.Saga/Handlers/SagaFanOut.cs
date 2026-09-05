#nullable enable
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Muflone.Saga.Handlers;

/// <summary>
/// Hands one message to every saga interested in it, saying which one failed.
/// </summary>
/// <remarks>
/// The exception is rethrown, not swallowed: the transport has to see the failure so the message is
/// nacked rather than silently dropped. What the log adds is <i>which</i> saga broke — without it, a
/// message wired to several sagas fails opaquely.
/// </remarks>
internal static class SagaFanOut
{
	public static async Task DeliverAsync<THandler, TMessage>(
		IEnumerable<THandler> handlers,
		TMessage message,
		Func<THandler, TMessage, Task> deliver,
		ILogger logger)
	{
		foreach (var handler in handlers)
		{
			try
			{
				await deliver(handler, message);
			}
			catch (Exception ex)
			{
				logger.LogError(ex,
					"Error while forwarding {MessageType} to saga handler {SagaHandlerType}.",
					typeof(TMessage).Name,
					handler?.GetType().FullName);
				throw;
			}
		}
	}
}
