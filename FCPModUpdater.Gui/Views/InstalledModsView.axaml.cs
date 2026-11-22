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
        Table.ContainerPrepared += OnTableContainerPrepared;
    }

    /// <summary>Category header rows can't take focus or show the selected/hover look of a mod row.</summary>
    private void OnTableContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        var isHeader = Table.ItemsView.Count > e.Index && Table.ItemsView[e.Index] is ModGroupViewModel;
        e.Container.Focusable = !isHeader;
        e.Container.Classes.Set("groupRow", isHeader);
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
        // Category headers are not mods: drop them from the selection (e.g. Ctrl+A or keyboard navigation).
        foreach (ModGroupViewModel header in e.AddedItems.OfType<ModGroupViewModel>().ToList())
            Table.SelectedItems?.Remove(header);

        if (Table.SelectedItems is { } selected)
            _viewModel?.SetSelection(selected.OfType<ModRowViewModel>().ToList());
    }
}
