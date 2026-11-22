using Avalonia;
using Avalonia.VisualTree;
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
        Table.TemplateApplied += (_, e) =>
        {
            if (_tableScroll is not null)
                _tableScroll.ScrollChanged -= OnTableScrolled;
            _tableScroll = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer");
            if (_tableScroll is not null)
                _tableScroll.ScrollChanged += OnTableScrolled;
        };
    }

    private ScrollViewer? _tableScroll;

    private void OnTableScrolled(object? sender, ScrollChangedEventArgs e) => UpdateStickyHeader();

    /// <summary>
    /// Pins the header of the group whose rows are at the top of the table. Hidden at the very top,
    /// where the real header is visible anyway.
    /// </summary>
    private void UpdateStickyHeader()
    {
        if (_viewModel is null || _tableScroll is null || _tableScroll.Offset.Y <= 0.5)
        {
            StickyHeader.IsVisible = false;
            return;
        }

        // First realized item whose bottom edge is below the top of the viewport.
        int top = -1;
        foreach (Control container in Table.GetRealizedContainers())
        {
            var index = Table.IndexFromContainer(container);
            if (index < 0 || container.TranslatePoint(new Avalonia.Point(0, container.Bounds.Height), Table) is not { } bottom
                || bottom.Y <= 0)
                continue;
            if (top < 0 || index < top)
                top = index;
        }

        ModGroupViewModel? group = null;
        IList<object> items = _viewModel.TableItems;
        for (var i = Math.Min(top, items.Count - 1); i >= 0 && group is null; i--)
            group = items[i] as ModGroupViewModel;

        StickyHeader.Content = group;
        StickyHeader.IsVisible = group is not null;
    }

    /// <summary>Category header rows can't take focus or show the selected/hover look of a mod row.</summary>
    // Collapsing or filtering moves rows; re-check once layout has caught up.
    private void OnTableItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Dispatcher.UIThread.Post(UpdateStickyHeader, DispatcherPriority.Background);

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

        if (_viewModel is not null)
            _viewModel.TableItems.CollectionChanged -= OnTableItemsChanged;

        _viewModel = viewModel;

        if (_viewModel is not null)
            _viewModel.TableItems.CollectionChanged += OnTableItemsChanged;

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
