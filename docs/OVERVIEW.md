# EasyWinMenu — Easy Windows & Menus

> **Regra de manutenção — leia antes de mexer no código:** este arquivo e
> [`PROMPT.md`](PROMPT.md) descrevem o estado atual do produto. Toda mudança de
> comportamento, visual ou arquitetura precisa atualizar os dois no mesmo commit
> que muda o código. Um recurso que existe no app e não está aqui, ou uma
> decisão registrada aqui que o código já não segue, é a documentação falhando
> em fazer o que ela existe para fazer.

## O que é

Um organizador de área de trabalho para Windows, no espírito das telas do
Android: grupos de atalhos, arquivos e pastas, exibidos como pastas de app
fecháveis (mosaico 3×3 + selo de contagem) que abrem numa folha centralizada,
ou como painéis livres com ícones soltos — a critério de cada grupo.

Nome exibido: **Easy Windows & Menus**. O nome interno do projeto,
`EasyWinMenu`, continua em uso na pasta de dados
(`%AppData%\EasyWinMenu`), no nome do processo e no repositório —
trocar isso quebraria configurações já salvas dos usuários, então foi
deixado de propósito fora do rebranding.

## Estrutura

```
EasyWinMenu.slnx
VERSION                          ← versão única do produto, ver "Versionamento"
src/
  Directory.Build.props          ← lê VERSION e aplica a src/EasyWinMenu.*
  EasyWinMenu.Core/               modelos e persistência, sem UI
  EasyWinMenu.App/                WPF: toda a interface e os serviços do Windows
tools/
  IconForge/                      gera o .ico oficial do app (não versionado)
  DeskProbe/                      *(não commitado — vive fora do repo)*
docs/
  OVERVIEW.md                     este arquivo
  PROMPT.md                       o prompt único equivalente a tudo isto
```

`EasyWinMenu.Core` não depende de WPF nem de Win32: só modelos
(`Models/`) e o `ConfigurationStore` que serializa tudo em JSON. Tudo que
sabe desenhar uma janela ou chamar uma API do Windows vive em `EasyWinMenu.App`.

## Compilar

```
mkfile d src/EasyWinMenu.App/EasyWinMenu.App.csproj
```

`mkfile` não reconhece o `.slnx` da raiz — precisa apontar para o `.csproj` do
App (ele arrasta o `EasyWinMenu.Core` pela referência de projeto).

## Instalador e distribuição

```
mkfile p src/EasyWinMenu.App/EasyWinMenu.App.csproj
```

`mkfile package` num projeto .NET olha primeiro se existe
`tools\build_installer.ps1` ao lado do `.csproj` — a mesma convenção que o
lado Flutter do `mkfile` já usa para `tools\build_apk.py`. Quando existe, ele
manda no que "empacotar" quer dizer; aqui isso é:

1. `dotnet publish` **self-contained, single-file, win-x64** — de propósito,
   não framework-dependent: o instalador precisa rodar numa máquina que pode
   não ter o runtime do .NET 10 instalado, já que o objetivo é justamente
   baixar e rodar fora desta rede.
2. Compilar `tools\EasyWinMenu.iss` com o **Inno Setup** (`ISCC.exe`,
   instalado via `winget install --id JRSoftware.InnoSetup`), passando a
   versão lida do `VERSION` como `/DMyAppVersion=...`.

O instalador resultante (`dist\EasyWinMenuSetup-<versão>.exe`, pasta fora do
controle de versão) instala por usuário em
`%LocalAppData%\Programs\EasyWinMenu` — sem pedir elevação — com atalho no
menu iniciar, ícone de área de trabalho opcional e entrada em
Adicionar/Remover Programas. O desinstalador não toca em
`%AppData%\EasyWinMenu` (configuração e grupos do usuário) de propósito.

Este repositório precisou de um `NuGet.Config` próprio (na raiz) que
adiciona ao `nuget.org` o mapeamento dos pacotes de runtime que o publish
self-contained baixa (`Microsoft.NETCore.App.Runtime.*`,
`Microsoft.WindowsDesktop.App.Runtime.*` etc.) — a máquina de
desenvolvimento tem um `NuGet.Config` global com `packageSourceMapping`
restrito a uma lista fixa de outros projetos, que não inclui esses pacotes.
O arquivo local só acrescenta ao mapeamento, nunca mexe no global.

O instalador é publicado como **release do GitHub** (`alxchs/EasyWinMenu`,
repositório público), anexado à tag da versão — é o link estável que
funciona de fora desta rede, sem custo.

## Áreas do código

### Desktop (`src/EasyWinMenu.App/Desktop/`)

- **`DesktopGroupWindow`** — a janela de um grupo. Guarda os dois modos de
  exibição (`DesktopGroupDisplayMode.Panel` / `.AppFolder`) na mesma janela,
  alternando por visibilidade em vez de reconstruir; isso é o que permite
  voltar de um modo para o outro nas mesmas dimensões e posição anteriores
  (`MenuCategory.PanelX/PanelY` guarda onde o painel estava).
  - Redimensiona por qualquer ponto da borda, não só por um canto: oito tiras
    invisíveis (`BuildResizeHandles`) cobrem os quatro lados e os quatro
    cantos do painel, cada uma com seu próprio cursor e sua própria borda
    oposta como âncora fixa (`OnResizeMouseMove`), do mesmo jeito que uma
    janela comum do Windows.
  - Dentro do painel, Ctrl+A seleciona tudo, Ctrl+C/Ctrl+X colocam os
    arquivos por trás dos ícones selecionados na área de transferência real
    do Windows (formato `CF_HDROP`, com o `Preferred DropEffect` que o
    Explorer também usa para diferenciar copiar de recortar), e Ctrl+V aceita
    de volta qualquer arquivo copiado em qualquer lugar do Windows, fixando-o
    como um novo ícone — o mesmo caminho que o arrastar-e-soltar já usava.
    Subpastas do grupo ficam de fora: são um agrupamento do próprio app, não
    uma pasta real em disco, então não há o que copiar. Um Ctrl+X **não**
    remove o ícone na hora — ele fica meio-opaco (como o Explorer faz) e só
    sai do grupo quando algo de fato aceita o "colar": um Ctrl+V em outro
    grupo deste mesmo app finaliza a remoção na hora; para um paste externo de
    verdade (Explorer), não existe um sinal do Windows para "terminou" — o
    grupo faz *polling* a cada 2s (`CheckPendingCutsAgainstDisk`) e só
    remove quando o arquivo realmente some do caminho original, o que só
    acontece se algo o moveu de fato.
  - Ícones no modo painel são organizados por um dos três modos "vivos" de
    `IconArrangement` (grade automática, por nome, por tipo) ou por posição
    livre (`None`, o padrão). O modo escolhido é salvo em
    `MenuCategory.IconArrangement` e reaplicado sozinho (`ReapplyArrangementIfActive`
    via `FinishStructuralChange`) toda vez que algo muda — soltar um arquivo,
    colar, apagar, renomear — não só no momento em que o menu foi clicado.
  - A borda do painel não vem do tema: é derivada da cor de fundo efetiva,
    10% mais clara (ou 10% mais escura, quando o fundo já é branco ou quase
    branco) — `ComputeGroupBorderColor`, recalculada toda vez que o fundo
    muda.
  - Passar o mouse sobre um ícone dá um leve aumento de escala e um tingimento
    do fundo, animados (`AttachHoverEffect`), do jeito que um cartão numa
    página web reage ao hover — sem competir com o destaque mais forte da
    seleção.
  - **Ctrl+F**, só no modo painel, abre uma barra de busca sob o título
    (`BuildSearchBar`) com contador de resultados ao vivo, destaque do trecho
    buscado dentro do próprio nome do ícone, e uma lista suspensa
    (`Popup` + `ListBox`) navegável com seta para cima/baixo; Enter num
    resultado tem o mesmo efeito que dar duplo clique nele e fecha a busca.
    Esc também fecha. Os dois únicos momentos em que o texto buscado é
    lembrado para a próxima vez são exatamente esses — Enter num resultado ou
    Esc — nunca um simples perder o foco.
- **`AppFolderTile`** — o ladrilho fechado: mosaico 3×3 dos primeiros ícones,
  selo de contagem, nome do grupo. Puramente desenho, sem estado. Ao ser
  colocado como o ícone do grupo na área de trabalho (`DesktopGroupWindow`,
  modo App Folder), é desenhado **40% maior** via `LayoutTransform` — o
  tamanho de fábrica do `AppFolderTile` é usado sem escala em qualquer outro
  lugar que venha a reutilizá-lo.
- **`GroupOverlayWindow`** — a folha que abre ao tocar o ladrilho. Tamanho:
  `min(2× a largura/altura do painel, 60%/50% da área útil do monitor)`,
  centralizada, nunca em tela cheia. Navega para dentro de subpastas sem abrir
  janelas novas; Esc sobe um nível e só fecha quando já está na raiz.
- **`GroupEntries`** — enumera os itens de um grupo (subpastas + itens fixados)
  na mesma ordem para o ladrilho, a folha e o painel concordarem sobre "o que
  tem dentro".
- **`MonitorPlacement`** — geometria pura (sem WPF) para resgatar grupos
  presos num monitor que sumiu, e para o Win+Shift+seta. Testada com cenários
  simulados de dois monitores (ver `PROMPT.md` → seção de testes).
- **`DisplayInventory`** — lê `Screen.AllScreens` e converte para os pixels
  independentes de dispositivo que uma `Window` usa.
- **`IDesktopGroupCommands`** — os comandos que um grupo pode pedir mas não
  pode fazer sozinho (duplicar-se, copiar sua aparência para os outros,
  virar o padrão, aplicar o papel de parede). Implementado em `App.xaml.cs`,
  porque só quem possui a configuração inteira pode reescrever os outros
  grupos.
- **`DesktopContextMenuBuilder`** — **atualmente sem uso.** Foi escrito para
  desenhar um menu de clique direito temático na área de trabalho vazia, mas
  depois que a seção "Grupos não vivem dentro da área de trabalho" (abaixo)
  decidiu não reparentar os grupos no `Progman`/`SHELLDLL_DefView`, o app não
  tem como interceptar o clique direito na área de trabalho real — não sobrou
  chamador para esta classe. A integração real com o clique direito do
  Windows é outra, ver `DesktopContextMenuRegistration` logo abaixo.

### Serviços (`src/EasyWinMenu.App/Services/`)

- **`DesktopContextMenuRegistration`** — registra um submenu de verdade sob
  `HKCU\...\DesktopBackground\Shell` (o truque clássico de verbos por
  registro — sem extensão COM, sem hook) com quatro comandos (novo grupo,
  colapsar/expandir todos, todos em App Folder, todos em painel). Cada verbo
  só relança o próprio `.exe` com um argumento `--desktop-action=...`; quem
  decide se isso inicia o app do zero ou entrega o comando ao processo já
  aberto é o `SingleInstanceCoordinator`. Roda em todo startup (idempotente),
  então os rótulos acompanham o idioma atual do app.
- **`SingleInstanceCoordinator`** — um `Mutex` nomeado decide quem é a
  primeira instância; qualquer instância seguinte manda a ação recebida por
  um named pipe (`EasyWinMenu.DesktopAction`) e sai imediatamente, sem abrir
  um segundo ícone de bandeja ou duplicar os grupos.
- **`IconCacheService`** — ícones vêm da lista de imagens do shell
  (`SHGetImageList`, tamanho jumbo/256px), com fallback em
  `Icon.ExtractAssociatedIcon`. O cache em disco é **PNG**, não `.ico`:
  salvar como `.ico` e reler achatava o canal alfa, deixando os ícones com
  halo preto ou branco.
- **`ShellCommands`** — os comandos de arquivo do menu de um item (executar
  como administrador, abrir local do arquivo, copiar como caminho,
  propriedades). Só aparecem quando o alvo é um arquivo/pasta real.
- **`ShellContextMenu`** — hospeda o `IContextMenu` de verdade do Explorer
  ("Menu padrão"), com extensões de terceiros e submenus. É desenhado pelo
  Windows, não pelo tema do app — é inerente a ser o menu real.
- **`WallpaperService`** — lê `SPI_GETDESKWALLPAPER`, com fallback no
  `TranscodedWallpaper` para quando o Windows está em modo slideshow.
- **`GlobalHotkeyService`** — registra **dois** atalhos globais
  independentes: abrir o menu da bandeja, e restaurar grupos escondidos.
  Veja "Atalho de restaurar grupos" abaixo para o porquê do segundo existir.

### Tema (`src/EasyWinMenu.App/Theming/`)

Paleta e métricas de controle portadas do projeto irmão **IGCParam**
(`Dark.xaml`/`Light.xaml`): comandos de 28px, campos de 27px, cantos de
4–6px, anel de foco de 2px. `Icons.xaml` + `AppIcons.cs` são um conjunto de
~28 ícones de linha (grade 24×24, traço 1.7, pontas arredondadas) que
substituiu os glifos do Segoe MDL2 usados antes — os dois apps agora falam a
mesma linguagem visual.

Convenções de menu seguidas: toda linha reserva a coluna de ícone mesmo sem
ícone (para os textos ficarem alinhados); um item ligado usa `IconCheck` na
coluna, nunca um caractere colado no texto; reticências (`...`) só em comandos
que pedem mais informação antes de agir (Renomear..., Cor de fundo...); nunca
em ações diretas ou que já mostram uma confirmação própria.

### O bug do clique-direito que travava o Windows

Uma versão anterior tinha um hook `WH_MOUSE_LL` que fazia
`SendMessage` bloqueante para o Explorer dentro do callback do hook — callback
de hook de baixo nível tem um prazo rígido do Windows (~300ms), e uma chamada
síncrona entre processos ali trava o mouse do sistema inteiro até o Explorer
responder, ou faz o Windows remover o hook silenciosamente quando estoura o
prazo. **Foi removido**, e nenhum hook voltou desde então — inclusive o
menu de contexto real que o app hoje coloca na área de trabalho
(`DesktopContextMenuRegistration`) é puro registro do Windows: o Explorer lê
as chaves e decide sozinho quando mostrar o item, sem o app interceptar nada
do clique. As ações que esse menu antigo oferecia (recolher tudo, reunir
tudo) também estão na bandeja.

## Decisões de arquitetura

### Grupos não vivem "dentro" da área de trabalho — de propósito

Foi investigado ancorar as janelas de grupo como filhas da janela real da
área de trabalho (`SHELLDLL_DefView`, sob o `Progman`), o que as tornaria
imunes a "Mostrar área de trabalho" e a ficar atrás de outra janela. Uma sonda
em C# (não commitada, ver seção de testes) provou que:

- Uma janela Win32 comum reparentada assim **pinta e recebe clique**.
- Uma janela **WPF com `AllowsTransparency=true` reparentada perde toda a
  transparência** — vira opaca, mesmo mantendo as flags internas de janela em
  camadas. Confirmado comparando lado a lado com a mesma janela sem
  reparentar (que mistura de verdade com o que está atrás).

Como o produto usa transparência em vários lugares (sliders de opacidade por
grupo, papel de parede como fundo visto através da área, a folha translúcida
do modo pasta de app), a decisão foi **não ancorar**: os grupos continuam
como janelas flutuantes normais, e o problema do "Mostrar área de trabalho"
foi resolvido de outro jeito (próxima seção).

### Atalho de restaurar grupos (padrão: Win+Ctrl+Alt+D)

"Mostrar área de trabalho" (e Win+M) **não minimiza de verdade** uma janela
sem botão na barra de tarefas — o que os grupos são, de propósito
(`ShowInTaskbar=false`). Medido diretamente (`IsIconic`, ordem Z): o Windows
só traz a janela do Progman para o topo da ordem Z e deixa os grupos
**visíveis, no lugar certo, mas enterrados atrás dela** — `WindowState`
nunca muda para `Minimized`. A correção não checa `WindowState`; ela alterna
`Topmost` (true e depois false), o truque padrão para forçar uma janela de
volta ao topo da sua faixa de ordem Z sem roubar o foco. Testado
ponta a ponta: Win+D esconde, o atalho traz de volta na posição exata.

### Menu do shell convive com o menu temático, não o substitui

O menu de contexto de um ícone é construído pelo app (mesmo tema, mesmos
comandos do dia a dia), com uma entrada final **"Menu padrão"** que abre o
`IContextMenu` real do Explorer via `ShellContextMenu`. As duas coisas
coexistem porque o menu real traz extensões de terceiros (7-Zip,
antivírus, Git) que um menu temático nunca replicaria, mas ele é
desenhado pelo Windows e não pode seguir o tema do app.

## Multi-monitor

Regras (`MonitorPlacement.cs`, sem estado, testável sem dois monitores
físicos):

- Um monitor some → todo grupo nele é levado para o monitor restante mantendo
  o **lugar relativo** (canto superior direito → canto superior direito).
  Essa posição de resgate **não é salva**: o grupo guarda a posição original
  (`DesktopX`/`DesktopY`) como "casa" e volta sozinho quando o monitor volta.
  Um arrasto manual durante o resgate vira a nova casa normalmente.
- **Win+Shift+←/→** move o grupo em foco para o monitor vizinho, dando a
  volta nas pontas — a mesma convenção do Windows.
- Só testado com simulação (um monitor físico disponível nesta máquina); veja
  a seção de testes.

## Ícone e identidade visual

`tools/IconForge` desenha o `.ico` oficial em código (não é um arquivo de
imagem editado à mão): quatro ladrilhos 2×2 representando janelas/grupos, um
deles com uma estrelinha de destaque. Cada resolução de 16 a 256px é
desenhada separadamente — nunca reduzida de uma maior — porque um ícone
reduzido vira uma mancha cinza bem no tamanho em que a bandeja o mostra.

```
dotnet run --project tools/IconForge -- src/EasyWinMenu.App/Assets/EasyWin.ico
dotnet run --project tools/IconForge -- saida.ico pasta-de-previa cyan   # variante alternativa
```

Variantes disponíveis: `tiles` (azul, a oficial), `cyan` (preto/ciano, no
espírito do launcher Android que inspirou o projeto) e `violet`.

## Versionamento

Um único arquivo `VERSION` na raiz (`a.b.c.d`), igual à convenção já usada
nos projetos Delphi do Alexandre. `src/Directory.Build.props` lê esse arquivo
e aplica `Version`/`AssemblyVersion`/`FileVersion` a todo projeto dentro de
`src/` — `EasyWinMenu.Core` e `EasyWinMenu.App` seguem a mesma versão
automaticamente. As ferramentas em `tools/` ficam de fora de propósito (não
fazem parte do produto entregue).

Para lançar uma versão nova: editar o `VERSION`, recompilar, commitar.

## O que ainda está pendente ou não verificado

- **Multi-monitor real**: toda a lógica foi validada por simulação
  (`MonitorPlacement`) e por um teste manual forçando coordenadas fora da
  tela; nunca foi visto um grupo atravessar para um segundo monitor físico
  nesta máquina (só há um disponível).
- **Descrição do processo no Gerenciador de Tarefas** ainda mostra
  `EasyWinMenu.App` — não é caption de janela, então ficou fora do
  rebranding, mas é uma bandeira solta se quiser fechar 100%.
- **Importar configurações só entra em vigor ao clicar em Salvar** na tela de
  Configurações — para um restore de backup isso é fácil de esquecer.

## Testes

Não há projeto de teste automatizado. A verificação nesta fase do projeto foi
toda manual: compilar com `mkfile`, rodar o app com uma configuração de teste
temporária (nunca a do usuário — sempre copiada de volta ao original depois),
e observar via screenshot + sondas Win32 escritas em C# quando PowerShell
não bastava (ver nota abaixo). `MonitorPlacement` teve sua matemática
verificada por um pequeno programa de console com ~15 asserções cobrindo
resgate, proporção mantida e monitores de tamanhos diferentes — não commitado
neste repositório.

> **Nota sobre PowerShell para diagnóstico Win32:** `FindWindow("Progman",
> $null)` em PowerShell retorna identificador vazio mesmo quando a janela
> existe — a marshalling do PowerShell não converte `$null` para `NULL` num
> parâmetro `string` de P/Invoke. Isso já produziu duas conclusões erradas
> nesta investigação (achar que o `Progman` não existia; achar que o
> `SetParent` entre processos não funcionava mais). Uma sonda escrita em C#
> puro deu a resposta certa nas duas vezes. Para qualquer diagnóstico Win32
> daqui para frente, escrever a sonda em C#, não em PowerShell.
