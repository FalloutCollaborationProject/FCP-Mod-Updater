using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FCPModUpdater.Gui.Services;
using FCPModUpdater.Models;
using FCPModUpdater.Services;

namespace FCPModUpdater.Gui.ViewModels;

public partial class RepoCardViewModel(RemoteRepo repo) : ObservableObject
{
    public RemoteRepo Repo { get; } = repo;
    public string Name => Repo.Name;
    public string Description => Repo.Description?.Trim() ?? "";
    public bool HasDescription => Description.Length > 0;
    public bool NotRecommended { get; } = ModCatalog.IsNotRecommended(repo);
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

public partial class LocalFolderViewModel(InstalledMod mod) : ObservableObject
{
    public InstalledMod Mod { get; } = mod;
    public string Folder => Mod.Name;
    public string Repo => Mod.MatchedRepoName ?? "";
    public string Url => $"github.com/FalloutCollaborationProject/{Repo}";

    [ObservableProperty] private bool _isChecked;
}

/// <summary>
/// "Install new" page: clone repositories from GitHub, or convert local (ZIP) folders into clones.
/// </summary>
public partial class InstallViewModel(MainWindowViewModel shell) : ObservableObject
{
    private List<RepoCardViewModel> _allRepos = [];
    private int _orgRepoCount;

    public ObservableCollection<RepoCardViewModel> Repos { get; } = [];
    public ObservableCollection<LocalFolderViewModel> Locals { get; } = [];

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
        ? "Local copies that match an FCP repository. Migrating turns each into a Git clone so it can be updated."
        : $"{_orgRepoCount} {(_orgRepoCount == 1 ? "repository" : "repositories")} tagged rimworld-mod · {_allRepos.Count} not installed";

    public int SelectedCount => IsConvert ? Locals.Count(l => l.IsChecked) : _allRepos.Count(r => r.IsChecked);

    public string Cta => IsConvert
        ? $"Migrate {Humanize.Count(SelectedCount, "mod")}"
        : $"Install {Humanize.Count(SelectedCount, "mod")}";

    public bool IsEmpty => IsConvert ? Locals.Count == 0 : Repos.Count == 0;

    public string EmptyText => IsConvert
        ? "No local mods found that match FCP repositories."
        : _allRepos.Count == 0
            ? "All FCP mods are already installed."
            : "No repositories match your search.";

    /// <summary>Called by the shell after every scan.</summary>
    public void OnModsChanged()
    {
        var checkedRepos = _allRepos.Where(r => r.IsChecked).Select(r => r.Name).ToHashSet();
        var checkedLocals = Locals.Where(l => l.IsChecked).Select(l => l.Mod.Path).ToHashSet();

        foreach (RepoCardViewModel card in _allRepos)
            card.PropertyChanged -= OnItemChanged;
        foreach (LocalFolderViewModel local in Locals)
            local.PropertyChanged -= OnItemChanged;

        _orgRepoCount = shell.OrgRepos.Count;
        _allRepos = ModCatalog.GetAvailableInstallRepos(shell.OrgRepos, shell.Mods.Select(m => m.Name))
            .Select(r => new RepoCardViewModel(r) { IsChecked = checkedRepos.Contains(r.Name) })
            .ToList();

        Locals.Clear();
        foreach (InstalledMod mod in ModCatalog.GetConvertibleMods(shell.Mods))
            Locals.Add(new LocalFolderViewModel(mod) { IsChecked = checkedLocals.Contains(mod.Path) });

        foreach (RepoCardViewModel card in _allRepos)
            card.PropertyChanged += OnItemChanged;
        foreach (LocalFolderViewModel local in Locals)
            local.PropertyChanged += OnItemChanged;

        ApplySearch();
        OnPropertyChanged(nameof(Subheading));
        RefreshSelection();
    }

    partial void OnSearchTextChanged(string value) => ApplySearch();

    private void ApplySearch()
    {
        var search = SearchText.Trim();
        Repos.Clear();

        foreach (RepoCardViewModel card in _allRepos.Where(r =>
                     search.Length == 0
                     || r.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                     || r.Description.Contains(search, StringComparison.OrdinalIgnoreCase)))
        {
            Repos.Add(card);
        }

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
        foreach (RepoCardViewModel card in _allRepos)
            card.IsChecked = !card.NotRecommended;
    }

    [RelayCommand]
    private void Clear()
    {
        if (IsConvert)
        {
            foreach (LocalFolderViewModel local in Locals)
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
            shell.ShowInstallDialog(_allRepos.Where(r => r.IsChecked).Select(r => r.Repo).ToList());
    }
}
