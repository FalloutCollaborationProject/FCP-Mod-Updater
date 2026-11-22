using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FCPModUpdater.Gui.Services;
using FCPModUpdater.Models;
using FCPModUpdater.Services;

namespace FCPModUpdater.Gui.ViewModels;

public partial class RepoCardViewModel(RemoteRepo repo, IReadOnlyList<ModCategory>? categories = null) : ObservableObject
{
    public RemoteRepo Repo { get; } = repo;
    private readonly ModCategory? _category = ModCategories.Find(categories ?? ModCategories.BuiltIn, repo.CategoryId);

    /// <summary>The repo's GitHub page.</summary>
    public string Url => !string.IsNullOrWhiteSpace(Repo.HtmlUrl)
        ? Repo.HtmlUrl
        : $"{(_category ?? ModCategories.Fcp).OrgUrl}/{Repo.Name}";
    public string Name => Repo.Name;
    public string Description => Repo.Description?.Trim() ?? "";
    public bool HasDescription => Description.Length > 0;
    public bool NotRecommended { get; } = ModCatalog.IsNotRecommended(repo, categories);
    public double CardOpacity => NotRecommended ? 0.85 : 1;

    public string Meta
    {
        get
        {
            var parts = new List<string>();
            if (Repo.PushedAt is { } pushed) parts.Add($"Updated {Humanize.Ago(pushed)}");
            if (Repo.SizeKb is { } size) parts.Add(Humanize.Size(size));
            return string.Join(" · ", parts);
        }
    }

    [ObservableProperty] private bool _isChecked;
}

public partial class LocalFolderViewModel(InstalledMod mod, IReadOnlyList<ModCategory>? categories = null) : ObservableObject
{
    private readonly ModCategory _category =
        ModCategories.Find(categories ?? ModCategories.BuiltIn, mod.CategoryId) ?? ModCategories.Fcp;

    public InstalledMod Mod { get; } = mod;
    public string Folder => Mod.Name;
    public string Repo => Mod.MatchedRepoName ?? "";
    public string Url => $"github.com/{_category.Organization}/{Repo}";

    [ObservableProperty] private bool _isChecked;
}

/// <summary>
/// "Install new" page: clone repositories from GitHub, or convert local (ZIP) folders into clones.
/// </summary>
public partial class InstallViewModel(MainWindowViewModel shell) : ObservableObject
{
    private List<RepoCardViewModel> _allRepos = [];
    private List<LocalFolderViewModel> _allLocals = [];
    private int _orgRepoCount;

    public ObservableCollection<RepoCardViewModel> Repos { get; } = [];
    public ObservableCollection<LocalFolderViewModel> Locals { get; } = [];

    /// <summary>"All categories" followed by each enabled category.</summary>
    [ObservableProperty] private IReadOnlyList<CategoryOption> _categories = [CategoryOption.All];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subheading))]
    private CategoryOption _selectedCategory = CategoryOption.All;

    /// <summary>The dropdown and group headers only show when there is more than one category.</summary>
    public bool HasCategories => Categories.Count > 2;

    private readonly HashSet<string> _collapsedRepoGroups = CategoryGroups.CollapsedSet();
    private readonly HashSet<string> _collapsedLocalGroups = CategoryGroups.CollapsedSet();

    /// <summary>The visible cards per category, reverse alphabetical. Collapsed groups still count and install.</summary>
    public ObservableCollection<RepoGroupViewModel> RepoGroups { get; } = [];
    public ObservableCollection<LocalGroupViewModel> LocalGroups { get; } = [];

    [RelayCommand]
    private void OpenRepo(RepoCardViewModel? card)
    {
        if (card is not null)
            shell.Platform.OpenUrl(card.Url);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstall))]
    [NotifyPropertyChangedFor(nameof(Heading))]
    [NotifyPropertyChangedFor(nameof(Subheading))]
    [NotifyPropertyChangedFor(nameof(SelectedCount))]
    [NotifyPropertyChangedFor(nameof(Cta))]
    [NotifyPropertyChangedFor(nameof(EmptyText))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private bool _isConvert;

    [ObservableProperty] private string _searchText = "";

    partial void OnIsConvertChanged(bool value) => shell.OnInstallTabChanged();

    public bool IsInstall => !IsConvert;

    public string Heading => IsConvert ? "Migrate local mods to Git" : "Install new mods";

    public string Subheading => IsConvert
        ? "Local copies that match a mod repository. Migrating turns each into a Git clone so it can be updated."
        : $"{_orgRepoCount} mod {(_orgRepoCount == 1 ? "repository" : "repositories")}{CategorySuffix} · {VisibleRepos.Count()} not installed";

    private string CategorySuffix => SelectedCategory.Category is { } c ? $" in {c.DisplayName}" : "";

    private IEnumerable<RepoCardViewModel> VisibleRepos
        => _allRepos.Where(r => SelectedCategory.Id is not { } id || r.Repo.CategoryId.Equals(id, StringComparison.OrdinalIgnoreCase));

    private IEnumerable<LocalFolderViewModel> VisibleLocals
        => _allLocals.Where(l => SelectedCategory.Id is not { } id || string.Equals(l.Mod.CategoryId, id, StringComparison.OrdinalIgnoreCase));

    // Hidden (filtered-out) cards stay checked; only what is visible is installed.
    public int SelectedCount => IsConvert ? Locals.Count(l => l.IsChecked) : Repos.Count(r => r.IsChecked);

    public string Cta => IsConvert
        ? $"Migrate {Humanize.Count(SelectedCount, "mod")}"
        : $"Install {Humanize.Count(SelectedCount, "mod")}";

    public bool IsEmpty => IsConvert ? Locals.Count == 0 : Repos.Count == 0;

    public string EmptyText => IsConvert
        ? "No local mods found that match a mod repository."
        : !VisibleRepos.Any()
            ? "All mods are already installed."
            : "No repositories match your search.";

    /// <summary>Called by the shell after every scan.</summary>
    public void OnModsChanged()
    {
        var checkedRepos = _allRepos.Where(r => r.IsChecked).Select(r => r.Name).ToHashSet();
        var checkedLocals = _allLocals.Where(l => l.IsChecked).Select(l => l.Mod.Path).ToHashSet();

        foreach (RepoCardViewModel card in _allRepos)
            card.PropertyChanged -= OnItemChanged;
        foreach (LocalFolderViewModel local in _allLocals)
            local.PropertyChanged -= OnItemChanged;

        IReadOnlyList<ModCategory> categories = shell.Categories;
        (Categories, SelectedCategory) = CategoryOption.Build(categories, SelectedCategory);
        OnPropertyChanged(nameof(HasCategories));

        _allRepos = ModCatalog.GetAvailableInstallRepos(shell.OrgRepos, shell.Mods.Select(m => m.Name), categories: categories)
            .Select(r => new RepoCardViewModel(r, categories) { IsChecked = checkedRepos.Contains(r.Name) })
            .ToList();

        _allLocals = ModCatalog.GetConvertibleMods(shell.Mods)
            .Select(m => new LocalFolderViewModel(m, categories) { IsChecked = checkedLocals.Contains(m.Path) })
            .ToList();

        foreach (RepoCardViewModel card in _allRepos)
            card.PropertyChanged += OnItemChanged;
        foreach (LocalFolderViewModel local in _allLocals)
            local.PropertyChanged += OnItemChanged;

        ApplySearch();
        OnPropertyChanged(nameof(Subheading));
        RefreshSelection();
    }

    partial void OnSearchTextChanged(string value) => ApplySearch();

    partial void OnSelectedCategoryChanged(CategoryOption value)
    {
        ApplySearch();
        RefreshSelection();
    }

    private void ApplySearch()
    {
        var search = SearchText.Trim();
        Repos.Clear();
        Locals.Clear();

        _orgRepoCount = shell.OrgRepos.Count(r => SelectedCategory.Id is not { } id || r.CategoryId.Equals(id, StringComparison.OrdinalIgnoreCase));

        foreach (LocalFolderViewModel local in VisibleLocals)
            Locals.Add(local);

        foreach (RepoCardViewModel card in VisibleRepos.Where(r =>
                     search.Length == 0
                     || r.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                     || r.Description.Contains(search, StringComparison.OrdinalIgnoreCase)))
        {
            Repos.Add(card);
        }

        IReadOnlyList<ModCategory> categories = shell.Categories;
        RepoGroups.Clear();
        foreach (var (category, items) in CategoryGroups.Group(Repos, r => r.Repo.CategoryId, categories))
            RepoGroups.Add(new RepoGroupViewModel(category, items, _collapsedRepoGroups));
        LocalGroups.Clear();
        foreach (var (category, items) in CategoryGroups.Group(Locals, l => l.Mod.CategoryId, categories))
            LocalGroups.Add(new LocalGroupViewModel(category, items, _collapsedLocalGroups));

        OnPropertyChanged(nameof(Subheading));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }

    private void OnItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RefreshSelection();

    private void RefreshSelection()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(Cta));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ShowInstall() => IsConvert = false;

    [RelayCommand]
    private void ShowConvert() => IsConvert = true;

    [RelayCommand]
    private void SelectAllRecommended()
    {
        foreach (RepoCardViewModel card in Repos)
            card.IsChecked = !card.NotRecommended;
    }

    [RelayCommand]
    private void Clear()
    {
        if (IsConvert)
        {
            foreach (LocalFolderViewModel local in _allLocals)
                local.IsChecked = false;
        }
        else
        {
            foreach (RepoCardViewModel card in _allRepos)
                card.IsChecked = false;
        }
    }

    private bool CanConfirm() => SelectedCount > 0;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        if (IsConvert)
            shell.ShowConvertDialog(Locals.Where(l => l.IsChecked).Select(l => l.Mod).ToList());
        else
            shell.ShowInstallDialog(Repos.Where(r => r.IsChecked).Select(r => r.Repo).ToList());
    }
}
