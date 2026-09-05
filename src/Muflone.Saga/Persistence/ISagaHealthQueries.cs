#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Muflone.Saga.Persistence;

/// <summary>
/// Reads the health of the sagas currently in the store.
/// </summary>
/// <remarks>
/// These queries are cheap because of how <see cref="ISagaRepository" /> is meant to be used: completing
/// a saga saves the final state and then removes the document, while failing or cancelling one leaves it
/// in place. Every surviving document is therefore a saga that is still open, failed or cancelled, and
/// counting them is a single filter rather than a walk through history.
/// </remarks>
public interface ISagaHealthQueries
{
	/// <summary>How many sagas ended in failure.</summary>
	Task<int> CountFailed(CancellationToken ct = default);

	/// <summary>
	/// A preview of the sagas counted by <see cref="CountFailed" />, at most <paramref name="limit" /> of
	/// them: type and correlation id, the only two facts a failure is investigated from.
	/// </summary>
	Task<List<FailedSagaPreview>> GetFailedPreview(int limit, CancellationToken ct = default);
}
