using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FCPModUpdater.Gui.Services;
using FCPModUpdater.Models;
using FCPModUpdater.Services;

namespace FCPModUpdater.Gui.ViewModels;

public partial class UninstallDialogViewModel(MainWindowViewModel shell, IReadOnlyList<InstalledMod> mods)
    : ObservableObject
{
    public const string ConfirmWord = "DELETE";

    public IReadOnlyList<InstalledMod> Mods { get; } = mods;

    public string Title => $"Uninstall {Humanize.Count(Mods.Count, "mod")}?";
    public string DeleteLabel => $"Delete {Humanize.Count(Mods.Count, "mod")}";

    public string Description => Mods.Count == 1
        ? "This folder will be permanently deleted from your Mods directory. This can't be undone."
        : "These folders will be permanently deleted from your Mods directory. This can't be undone.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmed))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private string _confirmText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public bool IsConfirmed => ConfirmText == ConfirmWord;
    public bool HasError => !string.IsNullOrEmpty(Error);

    [RelayCommand(CanExecute = nameof(IsConfirmed))]
    private void Delete()
    {
        var failures = new List<string>();

        foreach (InstalledMod mod in Mods)
        {
            var (success, error) = ModOperations.Uninstall(mod);
            if (!success)
                failures.Add($"{mod.Name}: {error}");
        }

        if (failures.Count > 0)
        {
            // Stay open so the failure is visible; whatever was deleted shows up on the next scan.
            Error = string.Join("\n", failures);
            ConfirmText = "";
            _ = shell.RescanAsync();
            return;
        }

        shell.CloseDialog(rescan: true);
    }

    [RelayCommand]
    private void Cancel() => shell.CloseDialog(rescan: HasError);
}
