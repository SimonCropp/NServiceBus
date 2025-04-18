namespace NServiceBus;

using System;

/// <summary>
/// Provides information about the delayed retries configuration.
/// </summary>
public class DelayedConfig
{
    /// <summary>
    /// Creates a new delayed retries configuration.
    /// </summary>
    /// <param name="maxNumberOfRetries">The maximum number of delayed retries.</param>
    /// <param name="timeIncrease">The time of increase for individual delayed retries.</param>
    public DelayedConfig(int maxNumberOfRetries, TimeSpan timeIncrease)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxNumberOfRetries);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeIncrease, TimeSpan.Zero);

        MaxNumberOfRetries = maxNumberOfRetries;
        TimeIncrease = timeIncrease;
    }

    /// <summary>
    /// Gets the configured maximum number of immediate retries.
    /// </summary>
    /// <remarks>Zero means no retries possible.</remarks>
    public int MaxNumberOfRetries { get; }

    /// <summary>
    /// Gets the configured time of increase for individual delayed retries.
    /// </summary>
    public TimeSpan TimeIncrease { get; }
}