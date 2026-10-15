# eGreenish OpenHeadset

App pequeno de bandeja para Windows que controla fones Bluetooth sem software de desktop: bateria, controle de ruído,
modo jogo e mais. **Aberto a qualquer marca:** suportar um fone novo é acrescentar um perfil, não reescrever o app.

[Read in English](README.md)

![Janela do eGreenish OpenHeadset](docs/screenshots/main.png)

> [!IMPORTANT]
> Projeto comunitário independente. Não tem vínculo, aval nem suporte de nenhum fabricante de fone. Os nomes de produto
> são marcas dos respectivos donos e aparecem só para dizer com o que o app funciona.

## Fones suportados

| Marca | Modelo | Bateria | Controle de ruído | Modo jogo | Testado em |
|-------|--------|:-------:|:-----------------:|:---------:|------------|
| UGREEN | HiTune Max5c | sim | desligado / ANC (4 níveis) / ambiente | sim | uma unidade, Windows 10 19045 |

O perfil do HiTune Max5c também controla a **ligação dupla** (conectar dois aparelhos ao mesmo tempo), a **decodificação
Hi-Res**, o **áudio espacial**, o **equalizador** (12 presets) e os **atalhos dos botões** (o que toque, toque duplo,
triplo e longo fazem). Cada um foi enviado a um fone de verdade e lido de volta, com uma exceção: as funções dos atalhos
usam os códigos do app do fabricante, e só o play/pause foi de fato testado. Neste fone, o **Hi-Res precisa estar
desligado** para o modo jogo e a ligação dupla serem aceitos; ligado, o fone recusa os dois e o app avisa.

Só entra na lista o que foi verificado num fone de verdade. Cada controle só é enviado depois de ser visto funcionando,
e o app confere a resposta do fone em seguida; se o fone não confirmar a mudança, o app avisa e nunca tenta outros
comandos. A janela está em português por enquanto; traduções são bem-vindas.

**O seu fone não está na lista?** Dá para adicionar. Veja [Adicionar um fone](#adicionar-um-fone).

## Uso

1. Pareie o fone nas configurações de Bluetooth do Windows e conecte.
2. Abra o `OpenHeadset.exe`. A janela encontra um fone suportado e mostra o estado.
3. O botão de fechar esconde a janela na bandeja. Para sair, use o menu da bandeja. Abrir o app de novo traz a janela de volta.
4. Opcional: ligue o interruptor *Iniciar com o Windows* para o app abrir ao entrar no Windows, direto na bandeja e sem janela. Vem desligado e só mexe na sua própria conta de usuário.

Windows 10 ou superior, x64. O download é autocontido: não precisa instalar o .NET.

## Como funciona

O Windows cria uma porta serial (COM) para o serviço serial Bluetooth do fone. O app localiza essa porta pelos IDs de
dispositivo do Windows e troca mensagens curtas com o fone. Cada modelo tem um **perfil**: o nome Bluetooth, o que ele
sabe fazer e um protocolo que conhece os bytes. A janela monta os controles pelo que o perfil declara, então um fone
sem modo ambiente simplesmente não mostra esse botão.

```
OpenHeadset.Core        tipos neutros e o contrato que todo fone cumpre
OpenHeadset.Profiles    uma pasta por marca: um perfil e um protocolo para cada modelo
OpenHeadset.Transport   acha o fone no Windows e fala pela porta serial
OpenHeadset.App         a janela de bandeja
```

## Adicionar um fone

1. Pareie e rode `dotnet run --project tools/OpenHeadset.Probe -- list` para ver se ele tem porta COM.
2. Descubra o protocolo no **seu próprio** fone (a ferramenta de captura e o log Bluetooth do Android bastam).
3. Crie um perfil em `src/OpenHeadset.Profiles/<Marca>/` e uma linha em `BuiltInProfiles.cs`.
4. Escreva testes com os bytes reais que capturou; um teste de contrato já confere todos os perfis.
5. Abra um pull request.

Guia completo (em inglês): [docs/adding-a-headset.md](docs/adding-a-headset.md). Leia antes as regras de segurança e de
"sala limpa" em [CONTRIBUTING.md](CONTRIBUTING.md): só o seu próprio aparelho, só comandos observados, nada destrutivo,
e nenhum código ou chave tirado do app de um fabricante. Notas por modelo em [docs/protocols](docs/protocols).

Fones que expõem controles só por Bluetooth de baixo consumo (BLE) ainda não são suportados; veja o guia.

## Compilar

```powershell
dotnet test tests/OpenHeadset.Tests
dotnet publish src/OpenHeadset.App -c Release -r win-x64 --self-contained true -o artifacts/OpenHeadset
```

Precisa do SDK do .NET 10.

## Licença

[MIT](LICENSE). Os ícones são da [Reicon](https://reicon.dev) (MIT); veja [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
