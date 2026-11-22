using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FCPModUpdater.Models;

namespace FCPModUpdater.Gui.ViewModels;

/// <summary>
/// A collapsible category header. <see cref="Category"/> is null for "Other" (mods with no known category).
/// Collapsed ids are kept by the page in <see cref="CategoryGroups.CollapsedSet"/> so a group stays collapsed across rebuilds.
/// </summary>
public abstract partial class CategoryGroupViewModel : ObservableObject
{
    private readonly ISet<string> _collapsed;
    private readonly Action? _toggled;

    protected CategoryGroupViewModel(ModCategory? category, ISet<string> collapsed, Action? toggled)
    {
        Category = category;
        _collapsed = collapsed;
        _toggled = toggled;
        _isExpanded = !collapsed.Contains(Key);
    }

    public ModCategory? Category { get; }
    public string Key => Category?.Id ?? CategoryGroups.OtherKey;
    public string Label => Category?.DisplayName ?? "Other";
    public string? Organization => Category?.Organization;
    public abstract int Count { get; }

    [ObservableProperty] private bool _isExpanded;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value) _collapsed.Remove(Key);
        else _collapsed.Add(Key);
        _toggled?.Invoke();
    }

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;
}

public sealed partial class RepoGroupViewModel : CategoryGroupViewModel
{
    public RepoGroupViewModel(ModCategory? category, IEnumerable<RepoCardViewModel> items, ISet<string> collapsed)
        : base(category, collapsed, null)
    {
        Items = new(items);
        foreach (RepoCardViewModel card in Items)
            card.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(RepoCardViewModel.IsChecked))
                    OnPropertyChanged(nameof(SelectButtonText));
            };
    }

    public ObservableCollection<RepoCardViewModel> Items { get; }
    public override int Count => Items.Count;

    /// <summary>True when every recommended card is ticked and no not-recommended one is: the button then deselects.</summary>
    public bool IsRecommendedSelected
        => Items.Any(c => !c.NotRecommended) && Items.All(c => c.IsChecked == !c.NotRecommended);

    public string SelectButtonText => IsRecommendedSelected ? "Deselect all" : "Select all recommended";

    /// <summary>
    /// Ticks this category's recommended cards and unticks its not-recommended ones; pressed again, unticks the
    /// whole category. Other categories are untouched.
    /// </summary>
    [RelayCommand]
    private void SelectRecommended()
    {
        var deselect = IsRecommendedSelected;
        foreach (RepoCardViewModel card in Items)
            card.IsChecked = !deselect && !card.NotRecommended;
    }
}

public sealed class LocalGroupViewModel(ModCategory? category, IEnumerable<LocalFolderViewModel> items, ISet<string> collapsed)
    : CategoryGroupViewModel(category, collapsed, null)
{
    public ObservableCollection<LocalFolderViewModel> Items { get; } = new(items);
    public override int Count => Items.Count;
}

/// <summary>Header row in the Installed table; its rows follow it in the table while expanded.</summary>
public sealed class ModGroupViewModel(ModCategory? category, IReadOnlyList<ModRowViewModel> rows, ISet<string> collapsed, Action toggled)
    : CategoryGroupViewModel(category, collapsed, toggled)
{
    public IReadOnlyList<ModRowViewModel> Rows { get; } = rows;
    public override int Count => Rows.Count;
    public int UpdateCount => Rows.Count(r => r.CanUpdate);
    public bool HasUpdates => UpdateCount > 0;
    public string UpdateText => $"{UpdateCount} behind";
}

public static class CategoryGroups
{
    public const string OtherKey = "\0other";

    public static HashSet<string> CollapsedSet() => new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Groups <paramref name="items"/> by category in reverse alphabetical order of display name, "Other" last.
    /// Empty groups are left out.
    /// </summary>
    public static IEnumerable<(ModCategory? Category, List<T> Items)> Group<T>(
        IEnumerable<T> items, Func<T, string?> categoryId, IReadOnlyList<ModCategory> categories)
        => items
            .GroupBy(i => ModCategories.Find(categories, categoryId(i)))
            .Select(g => (Category: g.Key, Items: g.ToList()))
            .OrderBy(g => g.Category is null)
            .ThenByDescending(g => g.Category?.DisplayName ?? "", StringComparer.OrdinalIgnoreCase);
}
