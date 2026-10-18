using System.Windows.Input;

namespace OppoPodsManager.UI.Tray;

// Native H.NotifyIcon menus invoke ICommand rather than MenuFlyoutItem.Click.
internal sealed class TrayCommand(Action execute) : ICommand
{
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => AppDispatcher.Post(execute);
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}
