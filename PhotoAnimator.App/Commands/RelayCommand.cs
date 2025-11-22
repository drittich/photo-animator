using System;
using System.Windows.Input;

namespace PhotoAnimator.App.Commands
{
    /// <summary>
    /// Simple synchronous relay command implementation for WPF MVVM patterns.
    /// Wraps an <see cref="Action"/> delegate for execution and an optional
    /// <see cref="Func{Boolean}"/> delegate for query of executability.
    /// Thread-safe invocation of <see cref="CanExecuteChanged"/> is not required for this use case.
    /// No asynchronous variant is provided per scope instructions.
    /// </summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        /// <summary>
        /// Initializes a new instance of <see cref="RelayCommand"/>.
        /// </summary>
        /// <param name="execute">Action to invoke when the command executes (must not be null).</param>
        /// <param name="canExecute">Optional predicate returning true when the command can execute.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="execute"/> is null.</exception>
        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <inheritdoc />
        public event EventHandler? CanExecuteChanged;

        /// <summary>
        /// Raises <see cref="CanExecuteChanged"/> to notify the UI that the executability may have changed.
        /// </summary>
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

        /// <inheritdoc />
        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        /// <inheritdoc />
        public void Execute(object? parameter) => _execute();
    }
}