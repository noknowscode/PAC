namespace PacConsole.Controls;

/// <summary>A command a control offers its own templates (the plant's camera tools): always available, never parameterised.</summary>
internal sealed class ViewCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute();
}
