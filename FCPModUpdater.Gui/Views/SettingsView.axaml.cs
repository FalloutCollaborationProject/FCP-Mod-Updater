using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FCPModUpdater.Gui.ViewModels;

namespace FCPModUpdater.Gui.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private SettingsViewModel? ViewModel => DataContext as SettingsViewModel;

    private void OnFolderKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ViewModel is { } vm)
        {
            _ = vm.CommitFolderAsync();
            e.Handled = true;
        }
    }

    private void OnFolderLostFocus(object? sender, RoutedEventArgs e) => _ = ViewModel?.CommitFolderAsync();
}
