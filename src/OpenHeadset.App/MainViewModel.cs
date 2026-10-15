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

/// <summary>Uma opção da lista de funções de um gesto.</summary>
public sealed record FunctionOption(int Code, string Label);

/// <summary>Uma linha de "Atalhos dos botões": um gesto e a função que o fone tem atribuída a ele.</summary>
public sealed partial class ButtonRow(int slot, string label, IEnumerable<FunctionOption> options) : ObservableObject
{
    public int Slot { get; } = slot;
    public string Label { get; } = label;
    public string AutomationName { get; } = $"Função do gesto {label.ToLowerInvariant()}";
    public ObservableCollection<FunctionOption> Options { get; } = new(options);

    [ObservableProperty]
    private int _code = -1;

    /// <summary>Mostra o código que o fone informou. Um código fora da lista (o fone usa alguns) ganha uma opção própria, para não ser apagado.</summary>
    public void Show(int code)
    {
        if (Options.All(o => o.Code != code))
            Options.Add(new FunctionOption(code, $"Outra ({code})"));
        Code = code;
    }

    /// <summary>Força a lista a voltar ao valor do fone depois de uma escolha que o fone não aceitou.</summary>
    public void Refresh() => OnPropertyChanged(nameof(Code));
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

    [ObservableProperty]
    private bool _hasDualLink;

    [ObservableProperty]
    private bool _dualLink;

    [ObservableProperty]
    private bool _hasHighQuality;

    [ObservableProperty]
    private bool _highQuality;

    [ObservableProperty]
    private bool _hasSpatialAudio;

    [ObservableProperty]
    private bool _spatialAudio;

    [ObservableProperty]
    private bool _hasEqualizer;

    /// <summary>Id do preset que o fone informou (-1: ainda não lido).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EqPresetLabel))]
    private int _eqPresetId = -1;

    /// <summary>Nome do preset ativo, para o cabeçalho do acordeão (vazio se ainda não lido).</summary>
    public string EqPresetLabel => EqPresets.FirstOrDefault(p => p.Id == EqPresetId)?.Label ?? "";

    [ObservableProperty]
    private bool _hasButtonMapping;

    public ObservableCollection<EqPreset> EqPresets { get; } = [];

    public ObservableCollection<ButtonRow> ButtonRows { get; } = [];

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
            DualLink = state.DualLink ?? false;
            HighQuality = state.HighQuality ?? false;
            SpatialAudio = state.SpatialAudio ?? false;
            EqPresetId = state.EqPreset ?? -1;
            ShowButtons(state.Buttons);
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
        HasDualLink = profile?.Capabilities.HasFlag(Capabilities.DualLink) ?? false;
        HasHighQuality = profile?.Capabilities.HasFlag(Capabilities.HighQuality) ?? false;
        HasSpatialAudio = profile?.Capabilities.HasFlag(Capabilities.SpatialAudio) ?? false;
        HasEqualizer = profile?.Capabilities.HasFlag(Capabilities.Equalizer) ?? false;
        HasButtonMapping = profile?.Capabilities.HasFlag(Capabilities.ButtonMapping) ?? false;

        EqPresets.Clear();
        foreach (var preset in profile?.EqPresets ?? [])
            EqPresets.Add(preset);

        ButtonRows.Clear();
        if (HasButtonMapping)
        {
            var functions = (profile?.ButtonFunctions ?? []).Select(f => new FunctionOption(f.Code, f.Label)).ToList();
            foreach (var gesture in profile?.ButtonGestures ?? [])
                ButtonRows.Add(new ButtonRow(gesture.Slot, gesture.Label, functions));
        }

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

    [RelayCommand]
    private Task ToggleDualLinkAsync() =>
        RunAsync(() => _session.SetDualLinkAsync(!DualLink));

    [RelayCommand]
    private Task ToggleHighQualityAsync() =>
        RunAsync(() => _session.SetHighQualityAsync(!HighQuality));

    [RelayCommand]
    private Task ToggleSpatialAudioAsync() =>
        RunAsync(() => _session.SetSpatialAudioAsync(!SpatialAudio));

    public async Task SelectEqPresetAsync(int presetId)
    {
        await RunAsync(() => _session.SetEqPresetAsync(presetId));
        OnPropertyChanged(nameof(EqPresetId)); // se o fone recusou, a lista volta ao preset dele
    }

    public async Task SelectButtonFunctionAsync(ButtonRow row, int functionCode)
    {
        await RunAsync(() => _session.SetButtonAsync(row.Slot, functionCode));
        row.Refresh();
    }

    private void ShowButtons(IReadOnlyList<int>? codes)
    {
        if (codes is null)
            return;

        foreach (var row in ButtonRows)
        {
            if (row.Slot < codes.Count)
                row.Show(codes[row.Slot]);
        }
    }

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
