namespace OpenHeadset.Core;

/// <summary>Transforma bytes recebidos em mensagens. Uma instância por conexão (guarda bytes parciais).</summary>
public interface IMessageParser
{
    /// <summary>Alimenta com os bytes lidos e devolve as mensagens completas, na ordem. Deve tolerar mensagens partidas, várias por leitura e lixo.</summary>
    IReadOnlyList<object> Feed(ReadOnlySpan<byte> data);

    /// <summary>Descarta bytes parciais (usado ao reconectar).</summary>
    void Reset();

    /// <summary>
    /// Chamado quando a porta ficou em silêncio com bytes ainda pendentes. Devolve o que der para interpretar deles
    /// (por exemplo, um quadro curto cujo tamanho o cabeçalho não descreve) e descarta o resto.
    /// </summary>
    IReadOnlyList<object> Flush() => [];
}

/// <summary>Um quadro a enviar e como reconhecer a resposta a ele.</summary>
/// <param name="Frame">Bytes exatamente como vão para o fone.</param>
/// <param name="IsReply">Diz se uma mensagem recebida é a resposta a este pedido.</param>
/// <param name="IsAccepted">Diz se a resposta significa "aceito". Nulo: qualquer resposta conta como aceita.</param>
/// <param name="RefusedMessage">Texto para o usuário quando o fone recusa, se o perfil sabe o motivo. Nulo: mensagem genérica.</param>
public sealed record ProtocolRequest(
    byte[] Frame,
    Func<object, bool> IsReply,
    Func<object, bool>? IsAccepted = null,
    string? RefusedMessage = null);

/// <summary>
/// O protocolo de um fone. É aqui que mora tudo o que é específico de modelo: formato dos quadros, comandos,
/// leitura do estado. Não deve ter nenhuma dependência de interface, de porta serial ou de Windows,
/// para poder ser testado só com bytes.
/// </summary>
public interface IHeadsetProtocol
{
    IMessageParser CreateParser();

    /// <summary>Pedido de leitura do estado. A resposta é decodificada por <see cref="DecodeState"/>.</summary>
    ProtocolRequest ReadState();

    /// <summary>Decodifica a resposta ao <see cref="ReadState"/>. Devolve nulo se a mensagem não for um estado válido.</summary>
    HeadsetState? DecodeState(object message);

    /// <summary>
    /// Pedido para mudar o ruído, ou nulo se o perfil não suporta. <paramref name="current"/> é o último estado lido
    /// (pode ser nulo) e serve, por exemplo, para manter o nível ao trocar de modo.
    /// </summary>
    ProtocolRequest? SetNoise(NoiseSetting desired, HeadsetState? current);

    /// <summary>Pedido para ligar ou desligar o modo jogo, ou nulo se o perfil não suporta.</summary>
    ProtocolRequest? SetGameMode(bool enabled);

    /// <summary>Pedido para ligar ou desligar a ligação dupla (conexão simultânea a dois aparelhos), ou nulo se o perfil não suporta.</summary>
    ProtocolRequest? SetDualLink(bool enabled) => null;

    /// <summary>Pedido para ligar ou desligar a decodificação de alta qualidade (Hi-Res), ou nulo se o perfil não suporta.</summary>
    ProtocolRequest? SetHighQuality(bool enabled) => null;

    /// <summary>Pedido para ligar ou desligar o áudio espacial, ou nulo se o perfil não suporta.</summary>
    ProtocolRequest? SetSpatialAudio(bool enabled) => null;

    /// <summary>Pedido para escolher um preset de equalizador (<see cref="EqPreset.Id"/>), ou nulo se o perfil não suporta.</summary>
    ProtocolRequest? SetEqPreset(int presetId) => null;

    /// <summary>
    /// Pedido para atribuir <paramref name="functionCode"/> ao gesto <paramref name="slot"/>, ou nulo se o perfil não suporta
    /// ou se o estado atual dos botões não é conhecido. Fones que mandam todas as atribuições de uma vez usam
    /// <paramref name="current"/> para não alterar os outros gestos.
    /// </summary>
    ProtocolRequest? SetButton(int slot, int functionCode, HeadsetState? current) => null;

    /// <summary>Verdadeiro se a mensagem é um aviso espontâneo do fone que indica que o estado mudou.</summary>
    bool IsStateChangeHint(object message);
}
