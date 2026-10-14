using Microsoft.Win32;
using OpenHeadset.Transport;

namespace OpenHeadset.Tests.Transport;

/// <summary>Usa uma chave de teste própria no registro do usuário, apagada ao final: nunca toca no Run de verdade.</summary>
public sealed class StartupRegistrationTests : IDisposable
{
    private readonly string _root = $@"Software\OpenHeadset.Tests\{Guid.NewGuid():N}";
    private readonly StartupRegistration _startup;

    public StartupRegistrationTests() => _startup = new StartupRegistration("Teste", _root + @"\Run");

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);

    [Fact]
    public void Starts_disabled()
    {
        Assert.False(_startup.IsEnabled());
        Assert.Null(_startup.RegisteredCommand());
    }

    [Fact]
    public void Enable_registers_the_quoted_path_with_the_tray_argument()
    {
        _startup.Enable(@"E:\Tools\Open Headset\OpenHeadset.exe");

        Assert.True(_startup.IsEnabled());
        Assert.Equal("\"E:\\Tools\\Open Headset\\OpenHeadset.exe\" --tray", _startup.RegisteredCommand());
    }

    [Fact]
    public void Disable_removes_the_entry_and_is_safe_to_repeat()
    {
        _startup.Enable(@"C:\a\OpenHeadset.exe");
        _startup.Disable();
        _startup.Disable();

        Assert.False(_startup.IsEnabled());
    }

    [Fact]
    public void Disable_without_ever_enabling_does_not_throw()
    {
        _startup.Disable();
        Assert.False(_startup.IsEnabled());
    }

    [Fact]
    public void RefreshPath_never_turns_the_feature_on()
    {
        _startup.RefreshPath(@"C:\novo\OpenHeadset.exe");
        Assert.False(_startup.IsEnabled(), "Desligado deve continuar desligado.");
    }

    [Fact]
    public void RefreshPath_repairs_an_entry_whose_executable_no_longer_exists()
    {
        _startup.Enable(@"C:\pasta-que-nao-existe\OpenHeadset.exe");
        _startup.RefreshPath(@"C:\novo\OpenHeadset.exe");

        Assert.Equal(StartupRegistration.CommandFor(@"C:\novo\OpenHeadset.exe"), _startup.RegisteredCommand());
    }

    [Fact]
    public void RefreshPath_leaves_a_working_entry_alone_so_another_copy_cannot_steal_it()
    {
        var existing = Path.GetTempFileName(); // um arquivo que existe de verdade
        try
        {
            _startup.Enable(existing);
            _startup.RefreshPath(@"C:\copia-de-teste\OpenHeadset.exe");

            Assert.Equal(StartupRegistration.CommandFor(existing), _startup.RegisteredCommand());
        }
        finally
        {
            File.Delete(existing);
        }
    }

    [Fact]
    public void RefreshPath_understands_paths_with_spaces()
    {
        _startup.Enable(@"C:\pasta que nao existe\Open Headset\OpenHeadset.exe");
        _startup.RefreshPath(@"C:\novo\OpenHeadset.exe");

        Assert.Equal(StartupRegistration.CommandFor(@"C:\novo\OpenHeadset.exe"), _startup.RegisteredCommand());
    }

    [Fact]
    public void Enabling_twice_overwrites_instead_of_duplicating()
    {
        _startup.Enable(@"C:\a\OpenHeadset.exe");
        _startup.Enable(@"C:\b\OpenHeadset.exe");

        Assert.Equal(StartupRegistration.CommandFor(@"C:\b\OpenHeadset.exe"), _startup.RegisteredCommand());
    }
}
