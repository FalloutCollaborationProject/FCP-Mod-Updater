using CommunityToolkit.Mvvm.ComponentModel;
using FCPModUpdater.Models;

namespace FCPModUpdater.Gui.ViewModels;

/// <summary>An entry of a category dropdown; <see cref="Category"/> is null for "All categories".</summary>
public sealed record CategoryOption(ModCategory? Category)
{
    public static readonly CategoryOption All = new((ModCategory?)null);

    public string? Id => Category?.Id;
    public string Label => Category?.DisplayName ?? "All categories";
    public override string ToString() => Label;

    /// <summary>"All" followed by every category; reuses <paramref name="current"/>'s match when it still exists.</summary>
    public static (IReadOnlyList<CategoryOption> Options, CategoryOption Selected) Build(
        IReadOnlyList<ModCategory> categories, CategoryOption? current)
    {
        var options = new List<CategoryOption> { All };
        options.AddRange(categories.Select(c => new CategoryOption(c)));
        CategoryOption selected = options.FirstOrDefault(o => o.Id is not null && o.Id == current?.Id) ?? All;
        return (options, selected);
    }
}

/// <summary>Settings switch for one category.</summary>
public partial class CategoryToggleViewModel(ModCategory category, Action<ModCategory, bool> changed) : ObservableObject
{
    public ModCategory Category { get; } = category;
    public string Label => Category.DisplayName;
    public string Detail => Category.RequiredTopic is { } topic
        ? $"github.com/{Category.Organization} · repos tagged {topic}"
        : $"github.com/{Category.Organization} · every repository";

    [ObservableProperty] private bool _isEnabled = category.Enabled;

    partial void OnIsEnabledChanged(bool value) => changed(Category, value);
}
