namespace Muflone.Saga;

/// <summary>
/// Where a saga is in its life.
/// </summary>
/// <remarks>
/// The three terminal values are not interchangeable. <see cref="Completed" /> is the saga doing what it
/// was started for; <see cref="Cancelled" /> is a legitimate outcome the process asked for; and
/// <see cref="Failed" /> is a step that could not be carried out. Collapsing the last two into one value
/// makes a healthy system indistinguishable from a broken one at a glance.
/// </remarks>
public enum SagaStatus
{
	Started = 0,
	InProgress = 1,
	Completed = 2,
	Cancelled = 3,
	Failed = 4
}
