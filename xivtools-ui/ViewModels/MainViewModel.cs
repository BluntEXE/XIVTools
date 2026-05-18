using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XivToolsUI.Models;
using XivToolsUI.Services;

namespace XivToolsUI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public AppSettings Settings { get; } = AppSettings.Load();

    [ObservableProperty] private ObservableObject? _currentPage;
    [ObservableProperty] private string  _statusText    = "Initializing...";
    [ObservableProperty] private bool    _isInitialized;
    [ObservableProperty] private string? _initError;
    [ObservableProperty] private string  _activePage    = "modlist";

    // Expose typed for cross-VM navigation
    public TextureViewModel        TextureViewModel       => Texture;
    public MaterialEditorViewModel MaterialEditorViewModel => MaterialEditor;
    public ModpackViewModel        ModpackVm              => Modpack;

    public ModListViewModel        ModList        { get; }
    public ModpackViewModel        Modpack        { get; }
    public TextureImportViewModel  TextureImport  { get; }
    public ModelViewerViewModel    ModelViewer    { get; }
    public ConflictViewModel        Conflicts      { get; }
    public ModelImportViewModel     ModelImport    { get; }
    public FileSearchViewModel      FileSearch     { get; }
    [ObservableProperty] private string _activeCollection = "Loading...";
    [ObservableProperty] private bool   _showCollectionPicker;
    public System.Collections.ObjectModel.ObservableCollection<CollectionEntry> AvailableCollections { get; } = new();
    public TextureViewModel      Texture     { get; }
    public ItemBrowserViewModel  ItemBrowser { get; }
    public MaterialEditorViewModel MaterialEditor { get; }
    public SettingsViewModel     SettingsVm  { get; }

    public MainViewModel()
    {
        ModList        = new ModListViewModel(this);
        Modpack        = new ModpackViewModel(this);
        TextureImport  = new TextureImportViewModel(this);
        ModelViewer    = new ModelViewerViewModel(this);
        Conflicts      = new ConflictViewModel(this);
        ModelImport    = new ModelImportViewModel(this);
        FileSearch     = new FileSearchViewModel(this);
        Texture        = new TextureViewModel(this);
        ItemBrowser    = new ItemBrowserViewModel(this);
        MaterialEditor = new MaterialEditorViewModel(this);
        SettingsVm     = new SettingsViewModel(this);
        CurrentPage = ModList;
        _ = InitializeFramework();
        LoadActiveCollection();
    }

    private async System.Threading.Tasks.Task InitializeFramework()
    {
        StatusText = "Initializing game cache...";
        var ok = await FrameworkService.Instance.Initialize(Settings);
        IsInitialized = ok;
        InitError     = FrameworkService.Instance.InitError;
        StatusText    = ok ? "Ready" : $"Game cache error - check Settings  ({InitError})";
        if (ok) await ModList.LoadModsAsync();
    }

    [RelayCommand] public void NavigateToModList()         { CurrentPage = ModList;        ActivePage = "modlist";    }
    [RelayCommand] public void NavigateToModpack()         { CurrentPage = Modpack;        ActivePage = "modpack";    }
    [RelayCommand] public void NavigateToTextureImport()   { CurrentPage = TextureImport;  ActivePage = "texImport";  }
    [RelayCommand] public void NavigateToModelViewer()     { CurrentPage = ModelViewer;    ActivePage = "model";      }
    [RelayCommand] public void NavigateToConflicts()       { CurrentPage = Conflicts;      ActivePage = "conflicts";  }
    [RelayCommand] public void NavigateToTexture()       { CurrentPage = Texture;        ActivePage = "texture";   }
    [RelayCommand] public void NavigateToItemBrowser()   { CurrentPage = ItemBrowser;    ActivePage = "items";     }
    [RelayCommand] public void NavigateToMaterialEditor(){ CurrentPage = MaterialEditor; ActivePage = "materials";  }
    [RelayCommand] public void NavigateToSettings()      { CurrentPage = SettingsVm;     ActivePage = "settings";  }

    public void SetStatus(string text) => StatusText = text;

    private void LoadActiveCollection()
    {
        try {
            var (_, name) = Services.PenumbraService.Instance.GetActiveCollection();
            ActiveCollection = name;
        } catch { ActiveCollection = "Unknown"; }
    }

    [RelayCommand]
    private void OpenCollectionPicker()
    {
        AvailableCollections.Clear();
        var cols = Services.PenumbraService.Instance.GetCollections();
        foreach (var kv in cols.OrderBy(c => c.Value))
            AvailableCollections.Add(new CollectionEntry { Id = kv.Key, Name = kv.Value });
        ShowCollectionPicker = true;
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task SwitchCollectionAsync(string collectionId)
    {
        ShowCollectionPicker = false;
        if (string.IsNullOrEmpty(collectionId)) return;
        try {
            Services.PenumbraService.Instance.SetActiveCollection(collectionId);
            LoadActiveCollection();
            SetStatus($"Switched to collection: {ActiveCollection}");
            ToastService.Instance.Success($"Collection: {ActiveCollection}");
            await ModList.LoadModsAsync();
            _ = Services.PenumbraService.Instance.TriggerRedrawAsync();
        } catch (Exception ex) {
            SetStatus($"Collection switch failed: {ex.Message}");
        }
    }

    [RelayCommand] private void CloseCollectionPicker() => ShowCollectionPicker = false;

    [RelayCommand] public void NavigateToFileSearch()    { CurrentPage = FileSearch;    ActivePage = "filesearch";   }
    [RelayCommand] public void NavigateToModelImport()   { CurrentPage = ModelImport;   ActivePage = "modelimport";  }

    // Re-init after settings change
    public async System.Threading.Tasks.Task Reinitialize()
    {
        IsInitialized = false;
        StatusText = "Reinitializing...";
        var ok = await FrameworkService.Instance.Initialize(Settings);
        IsInitialized = ok;
        StatusText = ok ? "Ready" : $"Game cache error - check Settings  ({FrameworkService.Instance.InitError})";
        if (ok) await ModList.LoadModsAsync();
    }
}
