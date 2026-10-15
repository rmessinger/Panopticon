using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Panopticon.Tools.ReplaySketch.ViewModels;

/// <summary>
/// Awaitable command with execution state, cancellation, and CanExecuteChanged. Bindable
/// execution is fire-and-forget with failures raised through <see cref="UnhandledException"/>;
/// tests and internal callers await <see cref="ExecuteAsync"/> directly.
/// </summary>
public sealed class AsyncCommand : ICommand
{
    private readonly Func<CancellationToken, Task> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly CancellationTokenSource _cancellation = new();
    private bool _isExecuting;

    public event EventHandler? CanExecuteChanged;

    public event EventHandler<Exception>? UnhandledException;

    public AsyncCommand(Func<CancellationToken, Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool IsExecuting => _isExecuting;

    public CancellationTokenSource Cancellation => _cancellation;

    public bool CanExecute(object? parameter)
    {
        return !_isExecuting && (_canExecute?.Invoke() ?? true);
    }

    public async Task ExecuteAsync(object? parameter = null)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isExecuting = true;
        RaiseCanExecuteChanged();
        try
        {
            await _execute(_cancellation.Token).ConfigureAwait(true);
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    void ICommand.Execute(object? parameter)
    {
        ExecuteAsync(parameter).ContinueWith(
            task => UnhandledException?.Invoke(this, task.Exception!.GetBaseException()),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    public void Cancel()
    {
        _cancellation.Cancel();
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
