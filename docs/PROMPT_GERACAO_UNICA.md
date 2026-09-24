# Prompt de geração em um passo

> Para que serve: entregue este texto inteiro a um agente de código, numa pasta vazia,
> e o resultado deve ser um aplicativo equivalente ao EasyWinMenu 1.1.1.0.
> É diferente de [`PROMPT.md`](PROMPT.md): aquele é o **histórico** do pedido, em ordem
> cronológica e com as correções que foram acontecendo. Este é a **especificação do zero**,
> já com as decisões finais, escrito para ser executado de uma vez.
>
> Todo valor numérico e todo nome citado abaixo foi lido do código da versão 1.1.1.0
> (`MenuTheme`, `MenuCategory`, `LauncherBehavior`, `DesktopGroupWindow`). Se o código
> mudar, este arquivo muda no mesmo commit (regra de manutenção do projeto).

---

## O texto do prompt

Crie um aplicativo para Windows 10/11 chamado **Easy Windows & Menus** (nome interno
`EasyWinMenu`), em **C# / .NET 10 / WPF**, que organiza a área de trabalho em **grupos de
atalhos**, no espírito das pastas de app do Android: cada grupo é uma janela solta sobre a
área de trabalho, com ícones que se arrastam, e o app vive na bandeja do sistema.

### 1. Estrutura da solução

- `EasyWinMenu.slnx` na raiz, um arquivo `VERSION` (`a.b.c.d`, começando em `1.1.1.0`) lido
  por `src/Directory.Build.props`, que aplica `Version`, `AssemblyVersion` e `FileVersion`.
- `src/EasyWinMenu.Core` (`net10.0`, **sem** WPF nem Win32): modelos e persistência.
- `src/EasyWinMenu.App` (`net10.0-windows`, WPF): toda a interface e os serviços do Windows.
- `tools/` para o instalador (Inno Setup, `EasyWinMenu.iss` + `build_installer.ps1`).
- `docs/OVERVIEW.md` (arquitetura e decisões) e `docs/PROMPT.md`, sempre em sincronia com o código.
- `NuGet.Config` local que só **acrescenta** o mapeamento dos pacotes de runtime
  (`Microsoft.NETCore.App.Runtime.*`, `Microsoft.WindowsDesktop.App.Runtime.*`) ao `nuget.org`.

### 2. Modelo de dados (Core, JSON em `%AppData%\EasyWinMenu\config.json`)

`LauncherConfiguration` tem `Categories` (grupos e pastas), `Items`, `Theme` e `Behavior`.
Serialização com `System.Text.Json`; arquivo antigo com campo a menos ou a mais **abre sem erro**.

- **`LaunchItem`**: `Name`, `Type`, `Target`, `Arguments`, `WorkingDirectory`,
  `ExecutionMode` (Normal, Minimizado ou Administrador; Normal por padrão), `Type` (Aplicativo, Arquivo, Pasta, URL ou Comando), `IconOverridePath`, `IsDesktopPinned`,
  `DesktopIconX`, `DesktopIconY`. Tem `Clone()`.
- **`MenuCategory`** (grupo): `Name`; `Id` (GUID sem hífens, nulo em config antiga, preenchido na carga e gravado; `Clone()` copia, "Duplicar grupo" gera outro); `Categories` e `Items` internos; `ThemeOverride` (um
  `MenuTheme` opcional por grupo); `IsDesktopGroup`; `DesktopX=40`, `DesktopY=40`,
  `DesktopWidth=220`, `DesktopHeight=180`; `DesktopIconScale=1.0`;
  `DesktopBackgroundImagePath`; `IconX`/`IconY` (posição do ladrilho); `IsCollapsed`;
  `IsClosed`; `AreaOpacity=1.0` e `TitleOpacity=1.0`; `DisplayMode` (`Panel` ou `AppFolder`,
  padrão `Panel`); `PanelX`/`PanelY` (onde o painel estava antes de virar ladrilho);
  `ShowBadge=true`; `IconArrangement` (`None` por padrão). `Clone()` e `CopyVisualFrom()`.
  Os JSON antigos `AreaTransparent` e `TitleTransparent`, quando verdadeiros, viram
  opacidade `0` (compatibilidade).
- **`IconArrangement`**: `None`, `Grid`, `ByName`, `ByType`. **Só `None` e `ByName` são
  usados**; `Grid` e `ByType` existem para ler configs antigas e são tratados como `ByName`.
- **`MenuTheme`** (padrões): `BackgroundColor="#1E1E1E"`, `Opacity=0.97`,
  `BorderColor="#3C3C3C"`, `CornerRadius=6`, `ShowShadow=true`, `ShadowBlurRadius=12`,
  `ShadowDepth=2`, `ShadowDirection=315`, `ShadowOpacity=0.35`, `ItemSpacing=2`,
  `ItemPadding=8`, `IconSize=18`, `TextColor="#FFFFFF"`, `HighlightColor="#3D7EB8FF"`,
  `TitleFontFamily="Segoe UI"`, `TitleFontSize=13`, `TitleBold=true`,
  `ItemFontFamily="Segoe UI"`, `ItemFontSize=13`, `AnimationDurationMs=120`.
- **`LauncherBehavior`**: `ClickMode` (clique simples por padrão), hotkey global do menu
  (desligada; Ctrl+Shift+Espaço), hotkey de restaurar grupos (ligada;
  **Win+Ctrl+Alt+D**), `AppTheme` (`System`), `Language` (nulo = idioma do Windows).

### 3. O grupo: visual

Cada grupo é uma janela WPF sem borda, `AllowsTransparency=true`, `ShowInTaskbar=false`, na
camada da área de trabalho (abaixo das janelas normais). Aparência, tudo vindo do `MenuTheme`
do grupo ou do tema global:

- Cantos arredondados (`CornerRadius`), borda de 1 px (`BorderColor`), fundo
  (`BackgroundColor` com `Opacity`), **sombra moderna** (`DropShadowEffect` com blur, profundidade,
  direção e opacidade do tema).
- **Título** na parte de cima com o nome do grupo (Segoe UI 13, negrito), com `TitleOpacity`
  próprio; a **área dos ícones** tem `AreaOpacity` próprio. Os dois podem ir a 0 (grupo
  "invisível" só com ícones sobre o papel de parede).
- Opcional: imagem de fundo do grupo, ou "usar o papel de parede como fundo" (lê
  `SPI_GETDESKWALLPAPER`).
- **Ícone de atalho**: célula de **80 × 80** (`TileSize`) vezes `DesktopIconScale`; ícone
  nítido (lista de imagens do shell, com cache em disco numa subpasta de `%AppData%\EasyWinMenu`, `ApplicationPaths.IconCacheDirectory`),
  nome abaixo em até 3 linhas, com reticências. Tamanhos de ícone no menu: **pequeno 0,75×,
  médio 1,0×, grande 1,5×**.
- Margem interna do canvas: 5% da largura, no mínimo 8 px.
- **Selo de contagem** no ladrilho fechado (`ShowBadge`): **nunca vermelho** — uma cor neutra do tema.
- **Regra visual do dono: nada vermelho**, nem combinado com amarelo, em ícones, seleções,
  logos ou artes.
- Tema do app: claro/escuro/sistema (`AppThemeService`); menus temáticos próprios (item com
  ícone, "visto" para itens marcados, separadores, submenus).

### 4. O grupo: dois modos de exibição na **mesma** janela

- **Painel** (`Panel`): a janela inteira com título e ícones soltos.
- **App Folder** (`AppFolder`): um **ladrilho fechado** com mosaico 3×3 dos primeiros ícones
  e o selo de contagem; ao tocar, abre uma **folha** centralizada (nunca em tela cheia) com a
  navegação do grupo. Alternar entre os modos é trocar visibilidade, **sem reconstruir**, para
  voltar ao mesmo tamanho e posição (`PanelX/PanelY` guardam o painel).
- O ladrilho fechado arrasta com `DragMove()`, não com contas manuais de `PointToScreen`
  (esse cálculo corrompia a posição em DPI misto).

### 5. Conteúdo do grupo: **só atalhos**

Um grupo guarda atalhos (arquivo, pasta, programa, URL, comando). Ao arrastar arquivos do
Explorer para o grupo, cada um vira um atalho. Não há arquivo "dentro" do grupo: é sempre
uma referência (`Target`).

### 6. Organização automática (regra fixa)

- Menu do grupo: **"Organizar ícones automaticamente"** (item marcável). **Não existe menu
  "Ordenar por"**: quando ligado, a ordem é **sempre por nome do atalho**
  (`StringComparer.CurrentCultureIgnoreCase`), em grade.
- A grade é **viva**: reaplicada ao soltar, colar, apagar, renomear, **redimensionar o
  painel** e **mudar o zoom** dos ícones — não só na hora do clique.
- Colunas = `(largura − 2×margem) / (80 × zoom)`, no mínimo 1. As posições ficam em
  unidades de 80 puro (o `RenderTransform` do canvas já aplica o zoom).
- Mover manualmente um ícone com a organização ligada **não** desliga: ele volta ao lugar.
  Com a organização desligada (`None`), a posição é livre e persiste.
- A dica (tooltip) do título mostra o estilo, o tamanho do ícone e, se ligado,
  "Organizar ícones automaticamente — Nome".

### 7. Interação (estilo Explorer)

- **Seleção**: clique, Ctrl+clique (alterna), Shift+clique (intervalo), **retângulo de
  seleção** (marquee) arrastando no fundo, Ctrl+A.
- **Teclado**: setas, Home e End navegam em **2D** pela grade (Shift estende a seleção);
  Enter abre os selecionados; F2 renomeia (um item selecionado); Delete remove a seleção
  (sem confirmação; uma pasta que ainda tem conteúdo fica de fora); F5 reaplica a
  organização; Esc limpa a seleção. **Ctrl+F** abre a busca por digitação, que fecha sozinha;
  Esc, ↑, ↓ e Enter navegam na busca. **Win+Shift+←/→** move o grupo ao monitor vizinho.
- **Área de transferência real**: Ctrl+C e Ctrl+X colocam os arquivos (`CF_HDROP` +
  `Preferred DropEffect`) e Ctrl+V cola de qualquer lugar do Windows como novos atalhos.
  Ctrl+X **não remove na hora**: o ícone fica meio-opaco e só sai do grupo quando o colar
  acontece (em outro grupo do app, na hora; no Explorer, por *polling* a cada 2 s até o
  arquivo sumir do caminho de origem).
- **Menu de contexto do grupo** (o **mesmo** no título, no fundo e no ladrilho): Recolher,
  Renomear…, Novo atalho…, Colar (Ctrl+V), Organizar ícones automaticamente, Tamanho do
  ícone (submenu), Ordem das janelas na área de trabalho (submenu: trazer todos para frente,
  enviar os outros para trás, enviar todos para trás), Estilo (painel/App Folder), Selo de
  contagem, Cor de fundo…, Imagem de fundo…, Remover imagem, Opacidade (submenu),
  Duplicar grupo, **Criar atalho na barra de tarefas**, Usar papel de parede como fundo, Aparência (submenu), Fechar grupo.
  É **reconstruído a cada clique direito** para os "vistos" nunca ficarem desatualizados.
  Cada ícone tem seu menu, com os itens do shell do Explorer (`IContextMenu`; Shift mostra
  os verbos estendidos).
- **Arrastar entre grupos**: uma **janela-fantasma** (`DragGhostWindow`, `VisualBrush` do
  tile, com animação de batimento) acompanha o cursor em coordenadas de tela e atravessa
  qualquer janela; a posição **da fantasma** (não a do cursor cru) decide o grupo de
  destino e a posição final, com o deslocamento de onde o usuário pegou o ícone.
  A janela de origem nunca se move durante o arrasto. Vários itens selecionados arrastam
  juntos. Ao soltar num grupo com organização ligada, reordena por nome.
- **Redimensionar** por qualquer borda ou canto (8 tiras invisíveis, cada uma com seu cursor
  e a borda oposta fixa). Fora da janela, o arrasto tem limite.
- **Mais de um monitor**: cada grupo guarda coordenadas em pixels físicos; `MonitorPlacement`
  (matemática pura) resgata um grupo que ficou fora da tela e mantém a proporção;
  `DisplayInventory` lê `Screen.AllScreens`. DPI por monitor tratado.

### 8. Bandeja, menus do sistema e atalhos globais

- **Ícone na bandeja** (`NotifyIcon`), clique simples ou duplo (configurável) abre o menu
  temático; clique direito abre o menu de contexto. O menu lista categorias e itens com
  ícone, e tem: **Grupos da área de trabalho** (submenu: abrir todos, fechar todos, e um
  item marcável por grupo), Configurações e Sair.
- **Clique direito na área de trabalho do Explorer**: submenu real registrado em
  `HKCU` (`DesktopContextMenuRegistration`) com verbos: novo grupo, abrir todos, fechar todos,
  recolher/expandir todos, todos em App Folder, todos em Painel, Configurações. **Sem hook
  de mouse global** (`WH_MOUSE_LL` travava o Windows). Cada verbo inicia o app com
  `<prefixo><ação>`; o segundo processo não consegue o mutex, manda a ação pelo **pipe
  nomeado** `EasyWinMenu.DesktopAction` e sai (`SingleInstanceCoordinator`, mutex
  `EasyWinMenu.SingleInstance`). Se o app não estiver rodando, sobe e executa a ação.
- **Atalho de grupo na barra de tarefas**: o item do menu do grupo grava um `.lnk` em
  `%AppData%\EasyWinMenu\GroupShortcuts` (via `WScript.Shell`) com destino no próprio
  executável, argumento `--desktop-action=focus-group:<id>` e o ícone do app, e abre o Explorer
  com o arquivo selecionado para o usuário arrastar à barra. Ao clicar: pelo mesmo pipe, o
  grupo abre se estiver fechado, expande se recolhido, vem para frente (`RestoreIfMinimized` +
  `Activate`) e pulsa a opacidade duas vezes; id desconhecido mostra um balão.
- **Hotkey global** do menu e **Win+Ctrl+Alt+D** para restaurar os grupos que o Windows
  escondeu (`GlobalHotkeyService`, ids `0xA1F3` e `0xA1F4`).
- **Iniciar com o Windows** (`StartupRegistration`, `HKCU\...\Run`).
- **Configurações** (`SettingsWindow`): categorias e itens, temas por pasta e global,
  hotkeys, idioma, exportar e importar configuração (a importação só vale ao salvar).

### 9. Localização

Oito idiomas em `Localization/Strings.<código>.json`: **de, en, es, it, ja, pl, pt, ru**.
`LocalizationService.Get(chave)` com plano B em inglês; extensão de marcação `LocExtension`
para XAML; idioma pela cultura do Windows, sobrescrevível em `Behavior.Language`. **Toda
chave existe nos oito arquivos.** Nenhum texto fixo no código.

### 10. Instalador e distribuição

`mkfile p src/EasyWinMenu.App/EasyWinMenu.App.csproj` (ou o script equivalente):
1. `dotnet publish` **self-contained, single-file, win-x64**;
2. Inno Setup (`ISCC.exe`) compila `tools\EasyWinMenu.iss` com `/DMyAppVersion=<VERSION>`,
   gerando `dist\EasyWinMenuSetup-<versão>.exe` (instala por usuário em
   `%LocalAppData%\Programs\EasyWinMenu`, **sem elevação**; atalho no menu Iniciar, ícone
   de desktop opcional, entrada em Adicionar/Remover Programas, idiomas Inglês e
   Português-BR). O desinstalador **não** apaga `%AppData%\EasyWinMenu`.
Publicar como release do GitHub (`alxchs/EasyWinMenu`) anexada à tag da versão.

### 11. Restrições de qualidade (obrigatórias)

- Feature com interface só está pronta depois de **abrir o app de verdade** e usar a
  função (clique, tecla, captura de tela). Teste de lógica não substitui.
- Sintoma que se repete (por exemplo, coordenada corrompida em DPI misto) exige
  investigar a causa comum antes de acrescentar mais um caso especial.
- Diagnóstico Win32 escreve-se em C#, não em PowerShell (a marshalling do PowerShell
  transforma `$null` em string vazia e já gerou conclusões erradas).
- Nunca `git push` sem autorização explícita naquele momento.

### 12. Como entregar

Compile em Release sem erro nem aviso, gere o instalador, abra o app com uma configuração de
teste de quatro grupos, e mostre por captura de tela: (a) um grupo com organização por
nome ligada, (b) o menu de contexto completo (sem "Ordenar por"), (c) um arrasto de um
ícone de um grupo para outro, (d) o mesmo grupo como App Folder.
