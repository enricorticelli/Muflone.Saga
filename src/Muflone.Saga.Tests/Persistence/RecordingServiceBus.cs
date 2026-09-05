using Muflone.Messages;
using Muflone.Messages.Commands;
using Muflone.Persistence;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Muflone.Saga.Tests.Persistence
{
	/// <summary>
	/// Records what a saga sends, and nothing else.
	/// </summary>
	/// <remarks>
	/// Unlike <see cref="InProcessServiceBus" /> it does not dispatch anything back: a test of a single
	/// saga step wants to assert on the command that left, not to run the rest of the process.
	/// </remarks>
	public sealed class RecordingServiceBus : IServiceBus
	{
		private readonly List<ICommand> _sentCommands = new List<ICommand>();

		public IReadOnlyList<ICommand> SentCommands => _sentCommands;

		public Task SendAsync<T>(T command, CancellationToken cancellationToken = default) where T : class, ICommand
		{
			if (command == null)
				throw new ArgumentNullException(nameof(command));

			_sentCommands.Add(command);
			return Task.CompletedTask;
		}

		public Task RegisterHandler<T>(Action<T> handler) where T : IMessage => Task.CompletedTask;
	}
}
