#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Muflone.Saga.Persistence;

/// <summary>
/// Finds the <b>open</b> saga states watching a given entity, looking them up by a field of the state
/// rather than by correlation id.
/// </summary>
/// <remarks>
/// A saga that stays open past its own flow has to react to events raised <b>outside</b> it — an entity
/// it is watching completed by a scheduled job, cancelled through another route, changed by an operator.
/// Those events carry the correlation id of the process that produced them, by construction not the
/// saga's, so <see cref="ISagaRepository.GetByIdAsync{TSagaState}" /> does not find them and the message
/// is dropped without a trace. The business key is the only thing the two processes share, and therefore
/// the only address a long-running saga can be reached by.
/// <para>
/// This is a <b>fallback</b>, not a replacement for the lookup by correlation id: an event born inside
/// the saga's own flow already carries the right correlation id, and that path is a read by primary key.
/// </para>
/// <para>
/// It returns a list rather than a single state: the same business key can legitimately be watched by
/// more than one open saga.
/// </para>
/// </remarks>
public interface ISagaStateLocator
{
	/// <param name="stateField">
	/// Name of the state property to search on, passed with <c>nameof</c>. How that maps to a path in the
	/// store is up to the implementation, because it is a persistence convention.
	/// </param>
	/// <param name="value">The key to look for. <see cref="Guid.Empty" /> matches nothing.</param>
	Task<IReadOnlyList<TSagaState>> FindOpenStatesBy<TSagaState>(
		string stateField,
		Guid value,
		CancellationToken ct = default)
		where TSagaState : SagaStateBase, new();
}
