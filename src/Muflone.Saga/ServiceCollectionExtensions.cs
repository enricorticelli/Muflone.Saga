#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Muflone.Messages.Commands;
using Muflone.Messages.Events;

namespace Muflone.Saga;

/// <summary>
/// Registers the saga side of the message pipeline.
/// </summary>
/// <remarks>
/// A saga is registered twice on purpose: once as the saga interface these methods handle, and once as
/// the Muflone handler that forwards to it - see <see cref="Handlers.SagaStartedByCommandHandler{T}" />.
/// Keeping the two apart is what lets several sagas react to the same message.
/// </remarks>
public static class ServiceCollectionExtensions
{
	/// <summary>Registers <typeparamref name="TSaga" /> as started by <typeparamref name="TCommand" />.</summary>
	public static IServiceCollection AddSagaStarter<TCommand, TSaga>(this IServiceCollection services)
		where TCommand : Command
		where TSaga : class, ISagaStartedByAsync<TCommand>
	{
		services.AddScoped<ISagaStartedByAsync<TCommand>, TSaga>();
		return services;
	}

	/// <summary>Registers <typeparamref name="TSaga" /> as a handler of <typeparamref name="TEvent" />.</summary>
	public static IServiceCollection AddSagaEventHandler<TEvent, TSaga>(this IServiceCollection services)
		where TEvent : Event, IIntegrationEvent
		where TSaga : class, ISagaEventHandlerAsync<TEvent>
	{
		services.AddScoped<ISagaEventHandlerAsync<TEvent>, TSaga>();
		return services;
	}
}
