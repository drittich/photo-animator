using System;

namespace PhotoAnimator.App.Services
{
    /// <summary>
    /// Provides thread-safe access to mutable concurrency settings affecting frame decode parallelism.
    /// </summary>
    public interface IConcurrencySettings
    {
        /// <summary>
        /// Gets the current maximum number of parallel frame decode operations.
        /// </summary>
        int MaxParallelDecodes { get; }

        /// <summary>
        /// Sets the maximum number of parallel frame decode operations.
        /// Implementations must validate the value and throw <see cref="ArgumentOutOfRangeException"/> if invalid.
        /// </summary>
        /// <param name="value">New maximum parallel decodes (validated range implementation-dependent).</param>
        void SetMaxParallelDecodes(int value);
    }
}