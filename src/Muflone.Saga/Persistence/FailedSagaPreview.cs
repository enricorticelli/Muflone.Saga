#nullable enable
using System;

namespace Muflone.Saga.Persistence;

/// <summary>
/// The fields a failed saga is named by in an alert.
/// </summary>
/// <remarks>
/// The saga type says <i>what</i> broke — one kind of failure can mean a lost order, another can be
/// harmless — and the correlation id is the only key its trace can be found by in the logs. A bare count
/// allows neither.
/// </remarks>
/// <param name="CorrelationId">Correlation id of the saga.</param>
/// <param name="SagaType">Saga state type, from the discriminator stored alongside the state.</param>
public sealed record FailedSagaPreview(Guid? CorrelationId, string SagaType);
