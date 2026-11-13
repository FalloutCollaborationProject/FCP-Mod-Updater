using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using FCPModUpdater.Gui.ViewModels;

namespace FCPModUpdater.Gui.Views;

public partial class InstalledModsView : UserControl
{
    private InstalledModsViewModel? _viewModel;

    public InstalledModsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as InstalledModsViewModel);
    }

    private void Attach(InstalledModsViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.VisibleLines.CollectionChanged -= OnLinesChanged;
            _viewModel.CloseMenusRequested -= HideOpenMenu;
        }

        _viewModel = viewModel;

        if (_viewModel is not null)
        {
            _viewModel.VisibleLines.CollectionChanged += OnLinesChanged;
            _viewModel.CloseMenusRequested += HideOpenMenu;
        }
    }

    private FlyoutBase? _openMenu;

    private void OnMenuOpened(object? sender, EventArgs e) => _openMenu = sender as FlyoutBase;

    private void OnMenuClosed(object? sender, EventArgs e)
    {
        if (sender is FlyoutBase { Target.DataContext: ModRowViewModel { Menu: { } menu } })
        {
            menu.IsBranchOpen = false;
            menu.IsCommitOpen = false;
        }

        if (ReferenceEquals(_openMenu, sender))
            _openMenu = null;
    }

    private void HideOpenMenu() => _openMenu?.Hide();

    // Keep the newest git output in view, like a terminal.
    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || _viewModel is null)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            var count = _viewModel.VisibleLines.Count;
            if (count > 0)
                LogList.ScrollIntoView(count - 1);
        }, DispatcherPriority.Background);
    }

    private void OnTableSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Table.SelectedItems is { } selected)
            _viewModel?.SetSelection(selected.OfType<ModRowViewModel>().ToList());
    }
}
