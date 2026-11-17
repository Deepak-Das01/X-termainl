using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace XTerminal.App.Services;

public partial class NotificationService : ObservableObject
{
    public ObservableCollection<AppToast> Toasts { get; } = new();
    public bool Enabled { get; set; } = true;

    public void Show(string title, string message, string? actionLabel = null, Action? action = null)
    {
        if (!Enabled) return;
        var toast = new AppToast
        {
            Title = title,
            Message = message,
            ActionLabel = actionLabel,
            Action = action
        };
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            Toasts.Insert(0, toast);
            while (Toasts.Count > 4) Toasts.RemoveAt(Toasts.Count - 1);
        });

        _ = DismissLaterAsync(toast);
    }

    public void Error(string title, string message, string? actionLabel = null, Action? action = null) =>
        Show(title, message, actionLabel, action);

    private async Task DismissLaterAsync(AppToast toast)
    {
        await Task.Delay(6000);
        System.Windows.Application.Current?.Dispatcher.Invoke(() => Toasts.Remove(toast));
    }

    [RelayCommand]
    private void Dismiss(AppToast? toast)
    {
        if (toast is not null) Toasts.Remove(toast);
    }

    [RelayCommand]
    private void InvokeAction(AppToast? toast)
    {
        toast?.Action?.Invoke();
        if (toast is not null) Toasts.Remove(toast);
    }
}

public sealed class AppToast
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? ActionLabel { get; init; }
    public Action? Action { get; init; }
}
