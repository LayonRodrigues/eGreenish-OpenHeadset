using System.Windows;
using System.Windows.Input;

namespace OpenHeadset.App;

public partial class MainWindow : Window
{
    // A janela tem 14 de margem transparente (sombra) em volta; este ajuste deixa o cartão a 8 da borda da tela.
    private const double ShadowMargin = 14;
    private const double EdgeGap = 8;

    private bool _docked;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // A altura muda sozinha (os cartões aparecem ao conectar). Sem reposicionar, a janela cresceria para fora da tela.
        SizeChanged += (_, _) => KeepOnScreen();
    }

    /// <summary>Encosta a janela no canto inferior direito da área útil, perto da bandeja, e a mantém lá se ela mudar de altura.</summary>
    public void PlaceNearTray()
    {
        _docked = true;
        KeepOnScreen();
    }

    private void KeepOnScreen()
    {
        var area = SystemParameters.WorkArea;
        var (width, height) = CurrentSize(area);

        if (_docked)
        {
            Left = area.Right - width + ShadowMargin - EdgeGap;
            Top = area.Bottom - height + ShadowMargin - EdgeGap;
        }

        // Em qualquer caso, nunca deixa a parte visível fora da área útil (também depois de arrastar).
        Left = Math.Clamp(Left, area.Left - ShadowMargin, Math.Max(area.Left - ShadowMargin, area.Right - width + ShadowMargin));
        Top = Math.Clamp(Top, area.Top - ShadowMargin, Math.Max(area.Top - ShadowMargin, area.Bottom - height + ShadowMargin));
    }

    /// <summary>Tamanho real se já foi desenhada; antes de aparecer, mede o conteúdo para não abrir no lugar errado.</summary>
    private (double Width, double Height) CurrentSize(Rect area)
    {
        if (ActualWidth > 0 && ActualHeight > 0)
            return (ActualWidth, ActualHeight);

        Measure(new Size(Width, area.Height + 2 * ShadowMargin));
        return (Width, DesiredSize.Height);
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
            return;

        _docked = false; // quem arrasta escolhe a posição; só garantimos que não saia da tela
        DragMove();
        KeepOnScreen();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();
}
