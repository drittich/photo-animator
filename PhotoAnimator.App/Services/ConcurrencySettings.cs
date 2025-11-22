using System;

namespace PhotoAnimator.App.Services
{
    /// <summary>
    /// Thread-safe mutable concurrency settings controlling frame decode parallelism.
    /// Consumers read <see cref="MaxParallelDecodes"/> to determine how many decode operations
    /// may run simultaneously. Implementations validate and reject invalid values.
    /// </summary>
    public sealed class ConcurrencySettings : IConcurrencySettings
    {
        private readonly object _sync = new();
        private int _maxParallelDecodes;

        /// <summary>
        /// Initializes a new instance with a default value based on the current processor count
        /// (1..4, preferring up to 4 logical cores).
        /// </summary>
        public ConcurrencySettings()
        {
            int processors = Environment.ProcessorCount;
            _maxParallelDecodes = Math.Min(4, Math.Max(1, processors));
        }

        /// <inheritdoc />
        public int MaxParallelDecodes
        {
            get
            {
                lock (_sync)
                {
                    return _maxParallelDecodes;
                }
            }
        }

        /// <inheritdoc />
        public void SetMaxParallelDecodes(int value)
        {
            int maxAllowed = Environment.ProcessorCount * 4;
            if (value < 1 || value > maxAllowed)
                throw new ArgumentOutOfRangeException(nameof(value), $"Value must be between 1 and {maxAllowed}.");

            lock (_sync)
            {
                _maxParallelDecodes = value;
            }
        }
    }
}