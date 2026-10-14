using Microsoft.Win32;

namespace OpenHeadset.Transport;

/// <summary>
/// Liga e desliga o início automático com o Windows, na chave <c>Run</c> do usuário atual
/// (não exige administrador e some junto com a conta).
/// </summary>
public sealed class StartupRegistration(string valueName, string keyPath = StartupRegistration.DefaultKeyPath)
{
    public const string DefaultKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Argumento que faz o app iniciar só na bandeja, sem abrir a janela.</summary>
    public const string TrayArgument = "--tray";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(valueName) is string { Length: > 0 };
    }

    /// <summary>Comando registrado hoje, ou nulo se o início automático está desligado.</summary>
    public string? RegisteredCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(valueName) as string;
    }

    public void Enable(string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        key.SetValue(valueName, CommandFor(exePath));
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// Se o início automático está ligado mas o executável registrado não existe mais (a pasta foi movida ou apagada),
    /// aponta para <paramref name="exePath"/>. Se o registrado ainda existe, não mexe: rodar outra cópia
    /// (por exemplo uma de teste) não pode roubar o início automático. Desligado continua desligado.
    /// </summary>
    public void RefreshPath(string exePath)
    {
        var registeredExe = ExePathOf(RegisteredCommand());
        if (registeredExe is not null && !File.Exists(registeredExe))
            Enable(exePath);
    }

    private static string? ExePathOf(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        var trimmed = command.Trim();
        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            return end > 1 ? trimmed[1..end] : null;
        }

        var space = trimmed.IndexOf(' ');
        return space > 0 ? trimmed[..space] : trimmed;
    }

    /// <summary>O caminho vai entre aspas porque pode ter espaços (por exemplo "E:\Tools\Open Headset").</summary>
    public static string CommandFor(string exePath) => $"\"{exePath}\" {TrayArgument}";
}
