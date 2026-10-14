using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenHeadset.Core;
using OpenHeadset.Transport;

namespace OpenHeadset.App;

/// <summary>Um botão de modo de ruído.</summary>
public sealed partial class ModeItem(NoiseMode mode, string label, string automationName, Geometry? icon) : ObservableObject
{
    public NoiseMode Mode { get; } = mode;
    public string Label { get; } = label;
    public string AutomationName { get; } = automationName;
    public Geometry? Icon { get; } = icon;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>Um botão de nível de cancelamento.</summary>
public sealed partial class LevelItem(string id, string label) : ObservableObject
{
    public string Id { get; } = id;
    public string Label { get; } = label;
    public string AutomationName { get; } = $"Intensidade {label.ToLowerInvariant()}";

    [ObservableProperty]
    private bool _isSelected;
}

public sealed partial class MainViewModel : ObservableObject
{
    // Rótulos em português para os níveis conhecidos; um perfil novo cai no rótulo dele.
    private static readonly Dictionary<string, string> LevelLabels = new()
    {
        ["deep"] = "Profundo",
        ["medium"] = "Médio",
        ["light"] = "Leve",
        ["adaptive"] = "Adaptativo",
    };

    private readonly HeadsetSession _session;
    private readonly StartupRegistration _startup;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _feedbackTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private string? _profileId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ControlsEnabled), nameof(ShowLevels))]
    private bool _isConnected;

    [ObservableProperty]
    private string _statusText = "Procurando o fone pareado…";

    [ObservableProperty]
    private string _headsetName = "eGreenish OpenHeadset";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatteryText))]
    private int? _batteryPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLevels))]
    private NoiseMode? _mode;

    [ObservableProperty]
    private bool _gameMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ControlsEnabled))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFeedback))]
    private string _feedback = "";

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _hasBattery;

    [ObservableProperty]
    private bool _hasNoise;

    [ObservableProperty]
    private bool _hasGameMode;

    public ObservableCollection<ModeItem> Modes { get; } = [];

    public ObservableCollection<LevelItem> Levels { get; } = [];

    public MainViewModel(HeadsetSession session, Dispatcher dispatcher, StartupRegistration startup)
    {
        _session = session;
        _dispatcher = dispatcher;
        _startup = startup;
        _startWithWindows = SafeIsEnabled(startup);
        _feedbackTimer.Tick += (_, _) => { _feedbackTimer.Stop(); Feedback = ""; };

        _session.Changed += snapshot => _dispatcher.InvokeAsync(() => Apply(snapshot));
        Apply(_session.Snapshot);
    }

    public bool ControlsEnabled => IsConnected && !IsBusy;

    public bool ShowLevels => IsConnected && Mode == NoiseMode.Anc && Levels.Count > 0;

    public bool HasFeedback => Feedback.Length > 0;

    public string BatteryText => BatteryPercent is { } p ? $"{p}%" : "—";

    private void Apply(SessionSnapshot s)
    {
        IsConnected = s.Status == ConnectionStatus.Connected;
        StatusText = s.Message;
        HeadsetName = s.Headset?.Name ?? "eGreenish OpenHeadset";
        ApplyProfile(s.Headset?.Profile);

        if (s.State is { } state)
        {
            BatteryPercent = state.BatteryPercent;
            Mode = state.Noise?.Mode;
            GameMode = state.GameMode ?? false;
            MarkSelection(state.Noise);
        }
        else if (!IsConnected)
        {
            BatteryPercent = null;
            Mode = null;
            MarkSelection(null);
        }
    }

    /// <summary>Monta os controles a partir do que o perfil declara. Só refaz quando o perfil muda.</summary>
    private void ApplyProfile(HeadsetProfile? profile)
    {
        if (profile?.Id == _profileId)
            return;
        _profileId = profile?.Id;

        HasBattery = profile?.Capabilities.HasFlag(Capabilities.Battery) ?? false;
        HasNoise = profile?.Capabilities.HasFlag(Capabilities.NoiseControl) ?? false;
        HasGameMode = profile?.Capabilities.HasFlag(Capabilities.GameMode) ?? false;

        Modes.Clear();
        Levels.Clear();
        if (profile is null || !HasNoise)
        {
            OnPropertyChanged(nameof(ShowLevels));
            return;
        }

        foreach (var mode in profile.NoiseModes)
            Modes.Add(CreateModeItem(mode));
        foreach (var level in profile.NoiseLevels)
            Levels.Add(new LevelItem(level.Id, LevelLabels.GetValueOrDefault(level.Id, level.Label)));
        OnPropertyChanged(nameof(ShowLevels));
    }

    private void MarkSelection(NoiseSetting? noise)
    {
        foreach (var item in Modes)
            item.IsSelected = noise is not null && item.Mode == noise.Mode;
        foreach (var item in Levels)
            item.IsSelected = noise?.LevelId == item.Id;
    }

    private static ModeItem CreateModeItem(NoiseMode mode)
    {
        var (label, automation, iconKey) = mode switch
        {
            NoiseMode.Off => ("Desligado", "Cancelamento de ruído desligado", "Icon.NoiseOff"),
            NoiseMode.Anc => ("ANC", "Cancelamento de ruído ativo", "Icon.NoiseAnc"),
            _ => ("Ambiente", "Modo ambiente", "Icon.NoiseAmbient"),
        };
        return new ModeItem(mode, label, automation, Application.Current.TryFindResource(iconKey) as Geometry);
    }

    [RelayCommand]
    private Task SelectModeAsync(NoiseMode mode) =>
        RunAsync(() => _session.SetNoiseAsync(new NoiseSetting(mode)));

    [RelayCommand]
    private Task SelectLevelAsync(string levelId) =>
        RunAsync(() => _session.SetNoiseAsync(new NoiseSetting(NoiseMode.Anc, levelId)));

    [RelayCommand]
    private void ToggleStartWithWindows()
    {
        try
        {
            if (StartWithWindows)
                _startup.Disable();
            else
                _startup.Enable(Environment.ProcessPath ?? throw new InvalidOperationException("Caminho do aplicativo desconhecido."));
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException or IOException)
        {
            ShowFeedback("Não consegui mudar o início com o Windows.");
        }

        // Relê do registro: o interruptor mostra o que de fato ficou gravado, não o que se tentou gravar.
        StartWithWindows = SafeIsEnabled(_startup);
    }

    private static bool SafeIsEnabled(StartupRegistration startup)
    {
        try { return startup.IsEnabled(); }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { return false; }
    }

    [RelayCommand]
    private Task ToggleGameModeAsync() =>
        RunAsync(() => _session.SetGameModeAsync(!GameMode));

    private async Task RunAsync(Func<Task<CommandResult>> action)
    {
        if (IsBusy || !IsConnected)
            return;

        IsBusy = true;
        try
        {
            var result = await action();
            if (!result.Ok)
                ShowFeedback(result.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ShowFeedback(string text)
    {
        Feedback = text;
        _feedbackTimer.Stop();
        _feedbackTimer.Start();
    }
}
