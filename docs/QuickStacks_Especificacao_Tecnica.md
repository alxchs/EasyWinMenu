# QuickStacks — Especificação Técnica

> Documento único e vivo do QuickStacks: substitui `QuickStacks_Especificacao_Completa.md` e
> `QuickStacks_Prompt.md` (removidos após a criação deste arquivo). Escrito para ser lido tanto
> por Claude quanto por Gemini, em sessões diferentes, sem depender de contexto de conversa —
> toda decisão relevante está aqui, com o porquê.
>
> Convenção herdada de `docs/OVERVIEW.md`/`docs/PROMPT.md` (EasyWinMenu): este arquivo é
> atualizado no mesmo commit que qualquer mudança relevante no QuickStacks, e admite
> explicitamente o que está pendente/não verificado em vez de omitir.

---

## 1. O que é o QuickStacks

Um único ícone na Taskbar do Windows que, ao ser clicado, abre um popup com uma estrutura
hierárquica de atalhos (pastas, executáveis, arquivos, URLs) — inspirado no conceito de
"Stacks" do macOS, com linguagem visual Fluent Design nativa do Windows 11.

É um produto **irmão e independente** do EasyWinMenu (WPF, grupos flutuantes na área de
trabalho) — mesmo repositório, código e stack completamente separados, sem nenhuma
dependência entre os dois. Nenhum arquivo do EasyWinMenu é tocado por este trabalho.

## 2. Onde o código vive

```
EasyWinMenu/                        (raiz do repositório git)
    src/EasyWinMenu.App/...         (produto existente, intocado)
    src/EasyWinMenu.Core/...
    quickstacks/                    (solução nova, isolada)
        QuickStacks.slnx
        NuGet.Config                 <- ver seção 4 (por que existe)
        Directory.Build.props        <- versão própria do QuickStacks (arquivo VERSION local)
        VERSION
        src/
            QuickStacks.Domain/           net8.0 — entidades puras, sem UI/SQLite
            QuickStacks.Application/      net8.0-windows — ViewModels (CommunityToolkit.Mvvm)
            QuickStacks.Infrastructure/   net8.0-windows — SQLite, settings
            QuickStacks.Localization/     net8.0 — JSON embutido (pt-BR/en-US/es-ES/de-DE)
            QuickStacks.UI/               net8.0-windows10.0.19041.0 — app WinUI 3
        tests/
            QuickStacks.UnitTests/         xUnit — regras do Domain, HexColor, Localization
            QuickStacks.IntegrationTests/  xUnit — SqliteMenuRepository contra banco real
```

`QuickStacks.slnx` e o `Directory.Build.props`/`VERSION` locais existem para o QuickStacks
**não** herdar o `src/Directory.Build.props` do EasyWinMenu (que leria o `VERSION` errado —
são produtos com números de versão independentes).

## 3. Stack e por quê

| Camada | Escolha | Motivo |
|---|---|---|
| UI | **WinUI 3** (Windows App SDK 1.6, não empacotado — `WindowsPackageType=None`) | Fluent Design nativo, Mica/Acrylic, controles modernos (`BreadcrumbBar`, `GridView` com reflow automático) sem reescrever o que o WPF do EasyWinMenu tinha que construir à mão. |
| Framework | .NET 8 (`net8.0-windows10.0.19041.0` na UI; `net8.0-windows`/`net8.0` puro nas demais camadas) | Piso mínimo pedido no spec original; permite ao QuickStacks.Domain ficar 100% portável (sem `-windows`), o que facilita testar regras de negócio sem nenhuma dependência do Windows. |
| MVVM | **CommunityToolkit.Mvvm** (`[ObservableProperty]`, `[RelayCommand]`) | Pedido explícito do spec original; elimina boilerplate de `INotifyPropertyChanged`/`ICommand` manual. |
| Persistência | **SQLite via `Microsoft.Data.Sqlite`, sem ORM** (repositórios escritos à mão) | Atende RNF02 (baixo consumo/startup) sem o peso de um Entity Framework completo; schema único criado por `CREATE TABLE IF NOT EXISTS` (sem migrations — produto novo, formato ainda não precisou mudar). |
| Ícone de bandeja | **`H.NotifyIcon.WinUI`** (NuGet) | WinUI 3 não tem `NotifyIcon` nativo; é o pacote de fato padrão da comunidade .NET para isso, evita reescrever `Shell_NotifyIcon` via P/Invoke à mão. |

## 4. Particularidade do ambiente de build desta máquina

O `NuGet.Config` de usuário desta máquina tem **Package Source Mapping** restrito a uma lista
de pacotes de um projeto não relacionado (MAUI/"Maestria"), e não inclui
`Microsoft.WindowsAppSDK`, `Microsoft.Data.Sqlite`, `CommunityToolkit.Mvvm` nem
`H.NotifyIcon.WinUI`. Sem tratamento, `dotnet restore` falha com `NU1100` para todos esses
pacotes.

**Solução aplicada**: `quickstacks/NuGet.Config` (mapeamento `pattern="*"` para `nuget.org`,
escopado só a essa subárvore — NuGet usa o config mais próximo na árvore de pastas, então isto
não altera o restore do EasyWinMenu.App/Core). Numa máquina com um `NuGet.Config` de usuário
sem essa restrição, este arquivo é inofensivo (só amplia o que já seria permitido).

Um segundo obstáculo, mais sério: esta máquina só tem o **dotnet SDK puro**, sem Visual
Studio instalado. O alvo WinUI 3 depende de uma ferramenta de geração de recursos (PRI/MRT —
`Microsoft.Build.Packaging.Pri.Tasks.dll`) que só é distribuída junto com o componente
"AppxPackage" do MSBuild da Visual Studio — **esse arquivo não existe em lugar nenhum desta
instalação do dotnet SDK** (confirmado por busca no disco). Como o app não é empacotado
(`WindowsPackageType=None`) e ainda não usa nenhum recurso `.resw`/MRT, a build do
`QuickStacks.UI.csproj` define:

```xml
<EnableCoreMrtTooling>false</EnableCoreMrtTooling>
```

para pular essa etapa inteira. **Decisão tomada na Fase 5**: em vez de contornar isso para
poder usar `.resw`, a internacionalização foi implementada com **JSON puro embutido como
recurso do assembly** (`QuickStacks.Localization`, sem nenhuma dependência de MRT/PRI) — ver
seção 6.7. `EnableCoreMrtTooling=false` continua valendo e não precisa mais ser revisto por
causa de i18n; só voltaria à mesa se algum recurso futuro exigir MRT de verdade (ex.:
empacotamento MSIX na Fase 7, que aí sim precisa rodar numa máquina com Visual Studio).

**Como compilar cada projeto** (a solução `.slnx` inteira falha com plataforma "Any CPU"
default — o projeto de UI só aceita x86/x64/arm64):

```
dotnet build quickstacks/src/QuickStacks.Domain
dotnet build quickstacks/src/QuickStacks.Application
dotnet build quickstacks/src/QuickStacks.Infrastructure
dotnet build quickstacks/src/QuickStacks.UI -p:Platform=x64
dotnet test  quickstacks/tests/QuickStacks.UnitTests
dotnet test  quickstacks/tests/QuickStacks.IntegrationTests
```

Todos os projetos foram compilados com sucesso nesta máquina nessas condições; os dois
projetos de teste rodaram (22/22 unitários e 16/16 de integração passando — números crescem a
cada fase; ver seção 8 para o total atual). **O que não foi verificado**: execução
real do app (ícone na bandeja aparecendo, popup abrindo, arraste de verdade para o Explorer,
redimensionamento visual) — este ambiente de desenvolvimento não tem uma sessão gráfica do
Windows interativa disponível para rodar o app. Isso fica como verificação manual pendente na
primeira vez que alguém rodar o QuickStacks numa máquina Windows normal.

## 5. Modelo de dados (SQLite)

Tabela única recursiva (self-join por `ParentId`), sem tabela separada por tipo — pasta e
folha são o mesmo registro, distinguidos por `Type`:

```sql
CREATE TABLE MenuItems (
    Id               TEXT PRIMARY KEY,
    ParentId         TEXT NULL REFERENCES MenuItems(Id) ON DELETE CASCADE,
    Name             TEXT NOT NULL,
    Description      TEXT NULL,
    Type             TEXT NOT NULL,   -- Folder | Shortcut | Url | Executable
    Path             TEXT NULL,
    Arguments        TEXT NULL,
    WorkingDirectory TEXT NULL,
    Icon             TEXT NULL,       -- ícone customizado; nulo = extrair do alvo
    SortOrder        INTEGER NOT NULL DEFAULT 0,
    IsFavorite       INTEGER NOT NULL DEFAULT 0,
    LaunchCount      INTEGER NOT NULL DEFAULT 0,   -- base de RF13 "mais utilizados"
    LastUsedUtc      TEXT NULL,                    -- base de RF12 "recentes"
    CreatedAt        TEXT NOT NULL,
    UpdatedAt        TEXT NOT NULL
);

CREATE TABLE Settings (              -- key/value: hoje só tamanho de janela por pasta
    Key   TEXT PRIMARY KEY,
    Value TEXT NOT NULL
);
```

`MenuItemType.Folder` é uma pasta/grupo **do próprio QuickStacks** (um container na árvore);
uma pasta **real** do Windows a ser aberta no Explorer é `MenuItemType.Executable` com `Path`
apontando para o diretório — `ShellExecute` (via `Process.Start(UseShellExecute:true)`) aceita
caminho de pasta normalmente.

Arquivo: `%LocalAppData%\QuickStacks\quickstacks.db`.

## 6. As 3 melhorias implementadas na Fase 1

### 6.1 Navegação dentro do grupo como pasta do Windows

Uma única janela popup, sempre a mesma instância, com uma trilha (`BreadcrumbBar` nativo do
WinUI 3) e o conteúdo do nível atual. Diferente do EasyWinMenu (que no modo Panel abria uma
**janela nova** por subpasta, e só no modo App Folder tinha navegação em trilha in-place), no
QuickStacks a navegação é **sempre in-place** — sem exceção, sem inconsistência entre modos.

Implementação: `FolderNavigationViewModel` (`QuickStacks.Application`) mantém
`ObservableCollection<BreadcrumbNodeViewModel> Breadcrumb` e `ObservableCollection<MenuEntryViewModel>
Items`; `NavigateIntoCommand`/`NavigateToBreadcrumbCommand`/`NavigateUpCommand` (todos
`[RelayCommand]`) recarregam os filhos do nível alvo via `IMenuRepository.GetChildrenAsync`.

### 6.2 Drag-and-drop corrigido: grupo→grupo e grupo→pasta do Windows

- **Entre grupos** (arrastar um item da listagem atual para um ladrilho de pasta também
  visível na mesma listagem): `GridView.DragItemsStarting` grava o Id do item arrastado num
  formato de dados customizado (`"QuickStacksItemId"`); o `StackPanel` de cada ladrilho-pasta
  aceita `Drop`/`DragOver` e chama `FolderNavigationViewModel.TryMoveIntoFolderAsync`, que
  delega a `IMenuRepository.MoveAsync` — protegido contra ciclo (mover uma pasta para dentro
  dela mesma ou de um descendente lança `InvalidOperationException`, coberto por teste de
  integração).
- **Para uma pasta real do Windows** (Explorer, área de trabalho): o mesmo
  `DragItemsStarting`, quando o item arrastado aponta para um arquivo/pasta que existe de
  verdade no disco, também registra o formato nativo `StandardDataFormats.StorageItems`
  (`DataPackage.SetDataProvider` resolvendo para um `StorageFile`/`StorageFolder` real) — é
  isso que faz o **Explorer aceitar o drop como um arraste de arquivo de verdade**, sem
  nenhum interop COM manual (diferente da investigação do EasyWinMenu, que confirmou não
  haver nenhum caminho equivalente ali). Itens sem um caminho real no disco (URL, comando) só
  carregam o formato interno — evita um drop que pareceria aceito mas não geraria nada útil.
- **Escopo desta fase**: um item por vez (não múltipla seleção), e o alvo do drop é sempre um
  ladrilho de pasta já visível na mesma janela — reordenar itens dentro do mesmo nível (drag
  para reordenar visualmente) fica para a Fase 2 (editor visual), junto com um mecanismo de
  persistência de `SortOrder` em lote.

### 6.3 Redimensionamento "como pasta do Windows"

O popup é uma `Window` de verdade, redimensionável nativamente (bordas do
`OverlappedPresenter` padrão). Os ícones ficam num `GridView` (layout de grade nativo,
`ItemsWrapGrid` por baixo) — o próprio controle recalcula quantas colunas cabem a cada
redimensionamento, sem nenhuma matemática manual de coluna (substitui o `ArrangeInGrid`
pixel-a-pixel que o WPF do EasyWinMenu precisava manter). O tamanho da janela é lembrado por
pasta (`Settings` — chave `window.size.<folderId>`, ou `window.size.root` na raiz),
persistido a cada mudança de tamanho (`AppWindow.Changed`) e restaurado ao navegar para
aquele nível.

## 6.4 Fase 2 — Editor visual (RF14/UC04/UC05)

`EditorWindow` (aberta pelo item "Editar estrutura..." do menu da bandeja): uma `TreeView`
nativa do WinUI 3 com a árvore inteira (`EditorViewModel.RootNodes`, montada por
`MenuTreeNodeViewModel.BuildTree` a partir de `IMenuRepository.GetAllAsync` — uma consulta só,
não uma por nível). Usa o padrão oficial de dados hierárquicos do WinUI 3: um `TreeViewItem`
aninhado dentro do próprio `DataTemplate`, com `ItemsSource` apontando para os filhos do nó.

- **CRUD**: Nova pasta / Novo item (diálogo com Nome/Tipo/Caminho/Argumentos/Diretório) /
  Renomear / Editar / Excluir (com confirmação; exclusão de pasta é cascata no banco — ver
  correção de bug abaixo), todos direcionados ao nó selecionado na árvore (raiz se nada
  selecionado).
- **Mover por arraste**: mesmo padrão de formato de dados customizado (`"QuickStacksItemId"`)
  já usado no popup da Fase 1, agora nos nós da `TreeView` — soltar um item sobre outra pasta
  reparenta via `IMenuRepository.MoveAsync` (mesma proteção contra ciclo).
- **Exportar/Importar** (RF17–RF20): arquivo JSON único com a árvore inteira
  (`IConfigExportService`/`ConfigExportService`), via `FileSavePicker`/`FileOpenPicker`
  nativos. Importar **substitui toda a estrutura atual** — o diálogo de confirmação deixa
  isso explícito antes de agir.
- **Reordenar dentro do mesmo nível**: `IMenuRepository.ReorderChildrenAsync` já existe e tem
  teste de integração, mas ainda **não está ligado a nenhum gesto de arraste na `TreeView`**
  desta fase (arrastar entre pastas diferentes funciona; arrastar só para trocar a ordem entre
  irmãos ainda não). Fica como pendência para uma próxima iteração da UI do editor.

**Bug de Fase 1 corrigido nesta fase**: `SqliteMenuRepository.Open()` nunca executava `PRAGMA
foreign_keys = ON` nas conexões normais (só a conexão descartável do `EnsureCreated`, no
construtor, tinha isso ligado) — ou seja, **`ON DELETE CASCADE` nunca era aplicado de
verdade** em nenhuma operação real de exclusão desde a Fase 1, só não tinha sido percebido
porque nenhum teste exercitava exclusão de uma pasta com filhos. Corrigido (o pragma agora
roda em toda conexão aberta) e coberto por um novo teste
(`DeleteAsync_OnFolder_CascadesToChildren`).

## 6.5 Fase 3 — Busca, favoritos, recentes, mais usados (RF08–RF13)

`FolderNavigationViewModel` ganhou um `BrowseMode` (`Folder | Favorites | Recent | MostUsed |
Search`): `Items` passa a vir de `IMenuRepository.GetFavoritesAsync`/`GetRecentAsync`/
`GetMostUsedAsync`/`SearchAsync` em vez de `GetChildrenAsync` conforme o modo. No popup, uma
caixa de busca e três botões (★ Favoritos, 🕐 Recentes, 🔥 Mais usados) no topo trocam de
modo; a `BreadcrumbBar` some quando o modo não é `Folder` (não faz sentido mostrar uma trilha
de pasta para uma lista de resultados que pode vir de qualquer nível da árvore).

- **Busca (RF10) é global**, não escopada ao nível atual — diferente do EasyWinMenu, que só
  buscava dentro do grupo aberto. Abrir uma pasta encontrada por busca/favoritos/recentes
  reconstrói a trilha real até ela (`IMenuRepository.GetAncestorsAsync`) em vez de empilhar em
  cima da trilha antiga, que não teria relação com o caminho real do resultado.
- **Type-ahead (RF08)** continua sendo uma camada separada da busca — mesma distinção de duas
  camadas que o EasyWinMenu já usava: digitar sem abrir a caixa de busca pula a seleção para o
  primeiro item cujo nome começa com o texto digitado, mas só entre os itens do **nível atual**
  (`PopupWindow.ItemsGrid_KeyDown`, buffer reiniciado após ~1s sem digitar). Limitação
  conhecida: só reconhece A-Z/0-9/espaço (`VirtualKeyToChar`) — sem suporte a acentos ou
  layouts de teclado não latinos por enquanto.
- **Favoritar**: menu de contexto (clique direito) num ícone com "Favoritar/Desfavoritar",
  chamando `IMenuRepository.SetFavoriteAsync`; um ícone favoritado ganha uma estrela dourada
  sobreposta no canto.
- **RegisterLaunchAsync já existia desde a Fase 1** (`LaunchService.LaunchAsync` chama isso a
  cada execução) — a Fase 3 só adicionou a UI que consome `LaunchCount`/`LastUsedUtc`, sem
  mudar como são gravados.

**Detalhe de plataforma descoberto nesta fase** (`Microsoft.UI.Xaml.Window` não é um
`FrameworkElement` — `x:Bind` com `Mode=OneWay`/`Converter=` no conteúdo raiz de uma `Window`
não compila): ver a explicação completa e as duas saídas usadas ao final da seção 6.6, que
esbarrou no mesmo problema de novo (checkmark do submenu Tema).

## 6.6 Fase 4 — Temas

- **Claro/Escuro/Seguir Windows**: submenu "Tema" no menu da bandeja, com
  `RadioMenuFlyoutItem`s (checkmark estilo Windows, mutuamente exclusivos por
  `GroupName`). `ThemeService` (`QuickStacks.UI`) guarda o `ElementTheme` atual, persiste em
  `Settings` (`"theme.mode"`) e reaplica em toda janela registrada (`ThemeService.Register`,
  chamado no construtor de `PopupWindow`/`EditorWindow`) quando o usuário troca de tema —
  sem precisar reiniciar o app. "Seguir Windows" é `ElementTheme.Default`, que o WinUI 3 já
  resolve sozinho a partir do tema do sistema.
- **Cor de fundo por pasta**: tabela nova `FolderAppearance` (`FolderId` PK com
  `ON DELETE CASCADE`, `BackgroundColorHex`) + `IMenuRepository.GetFolderBackgroundColorAsync`/
  `SetFolderBackgroundColorAsync`. No popup, clique direito num ladrilho de pasta → "Cor de
  fundo desta pasta..." abre um diálogo com um campo de hex (`#RRGGBB`); ao entrar naquela
  pasta, o fundo da janela usa essa cor em vez do tema padrão (campo vazio remove o override).
  Validação do formato (`HexColor.IsValid`, em `QuickStacks.Domain` — sem depender de nenhum
  tipo de UI) roda tanto na UI quanto no repositório (defesa em profundidade: o repositório
  lança `ArgumentException` para uma cor mal formada, mesmo se algo além da UI tentar gravar).
- **Escopo desta fase**: não inclui cores customizáveis de outros elementos (texto, destaque,
  bordas) nem um seletor de cor visual — só um campo de texto hex, equivalente ao "MVP" desse
  recurso. O `MenuTheme` completo do EasyWinMenu (sombra, opacidade, fontes, etc.) não foi
  portado; se algum desses detalhes for pedido depois, entra como incremento desta mesma fase.

**Detalhe de plataforma descoberto nesta fase**: `Microsoft.UI.Xaml.Window` **não é** um
`FrameworkElement`. Um `x:Bind` com `Mode=OneWay` (chamada de função) ou com `Converter=`
declarado diretamente no conteúdo raiz de uma `Window` não compila (`SetConverterLookupRoot`/
o hook de atualização via `Loaded` exigem um `FrameworkElement`, e `this` ali é a `Window`).
Duas saídas usadas: (1) para `bool → Visibility`, usar o `x:Bind` direto sem `Converter=` — o
compilador do WinUI 3 já faz essa coerção de tipo automaticamente; (2) para visibilidade que
depende de um valor que muda em runtime (a `BreadcrumbBar` conforme o `Mode`), assinar
`ViewModel.PropertyChanged` no code-behind e setar a propriedade à mão, em vez de `x:Bind`.
Vale a pena ter isso em mente em qualquer novo `Window` do QuickStacks — dentro de um `Page`/
`UserControl` normal (que É `FrameworkElement`) esse problema não existe. Pelo mesmo motivo,
`RadioMenuFlyoutItem.IsChecked` do submenu Tema é setado à mão em `TrayIconWindow` (não por
bind) ao construir a janela.

## 6.7 Fase 5 — Internacionalização completa (RF15)

`QuickStacks.Localization`: projeto novo, `pt-BR`/`en-US`/`es-ES`/`de-DE`, cada um num JSON
(`Strings.<código>.json`) **embutido como recurso do assembly** (`EmbeddedResource`, lido via
`Assembly.GetManifestResourceStream`) — deliberadamente **não** `.resw`, para não depender da
ferramenta MRT/PRI que esta máquina não tem (seção 4). Mesma ideia do `LocalizationService` do
EasyWinMenu (JSON com fallback), adaptada de recurso WPF para recurso de assembly puro, já que
`QuickStacks.Localization` é uma classlib sem UI.

- `LocalizationService.Get(key)` sempre resolve — chave ausente no idioma atual cai para
  `pt-BR` (idioma nativo do documento-fonte); chave ausente em `pt-BR` também devolve a
  própria chave em vez de lançar exceção ou mostrar `null`.
- **Troca dinâmica sem reiniciar**: `SetLanguage` dispara `LanguageChanged`; cada `Window`
  (`TrayIconWindow`/`PopupWindow`/`EditorWindow`) assina esse evento e re-executa um
  `RefreshTexts()` que resseta os textos que já desenhou — mesmo padrão de "reconstruir a UI
  ao trocar idioma" que o EasyWinMenu já usava, só que aqui não há `LocExtension`/binding
  XAML nenhum: **todo texto localizado é setado imperativamente no code-behind**, porque XAML
  puro não tem como ler o dicionário de idioma (isso é o que `x:Uid` faria via `.resw`/MRT,
  que foi deliberadamente evitado). Idioma persiste em `Settings` (`"language.code"`);
  detecção inicial via `CultureInfo.CurrentUICulture`, com fallback para `pt-BR`.
- **Menu de contexto por item** (favoritar/cor de fundo, no popup) usa o evento `Opening` do
  `MenuFlyout` para reaplicar os textos a cada abertura, já que cada ladrilho instancia sua
  própria cópia do `MenuFlyoutItem` a partir do `DataTemplate` — não há uma instância única
  para re-textualizar de fora.
- **Nomes dos idiomas no submenu "Idioma"** ficam escritos no próprio idioma de cada um
  ("Português (Brasil)", "English (US)", etc.) — não são traduzidos, seguindo a convenção
  usual de seletores de idioma (o nome de um idioma não muda por causa do idioma da UI).
- **Teste de paridade**: `LocalizationServiceTests.EveryLanguage_HasEveryKeyThatDefaultLanguageHas`
  compara os dicionários crus dos 4 arquivos (via um acesso `internal` exposto só para teste)
  para garantir que nenhuma chave existe em `pt-BR` mas falta numa tradução — sem esse teste,
  uma chave faltando ficaria escondida pelo próprio fallback do `Get()`.
- **Escopo desta fase**: cobre as strings de UI já existentes (bandeja, popup, editor). Não
  cobre mensagens de erro de baixo nível (exceptions/stack traces) nem textos que ainda vão
  nascer nas próximas fases (`.lnk`, distribuição) — essas entram traduzidas desde o início
  quando forem implementadas, seguindo o mesmo padrão de chaves.

**Detalhe de plataforma descoberto nesta fase**: `MenuFlyoutSubItem`/`RadioMenuFlyoutItem`
dentro de um `MenuFlyout` anexado como `ContextFlyout`/`TaskbarIcon.ContextFlyout` **são**
`FrameworkElement`s próprios (diferente da `Window` — ver seção 6.6), então setar `.Text`/
`.IsChecked` neles a partir do code-behind da janela funciona sem nenhuma restrição especial;
a única armadilha real foi o `MenuFlyoutItem` dentro de um `DataTemplate` de item de lista
(GridView), que não tem uma instância única — daí o uso do evento `Opening` em vez de nomear
os itens.

## 7. O que ainda não existe (roteiro, em ordem)

- **Fase 6 — Importação de `.lnk`** (RF08): resolver `.lnk` via `IShellLinkW` (COM) para
  extrair alvo/argumentos/diretório/ícone reais. O EasyWinMenu nunca fez isso (confirmado –
  hoje ele só classifica `.lnk` pela extensão, sem nunca abrir o arquivo).
- **Fase 7 — Distribuição corporativa**: empacotamento MSIX (precisa da máquina com Visual
  Studio por causa da seção 4), documentação de implantação/atualização.

## 8. Testes automatizados

Diferente do EasyWinMenu (que documenta explicitamente não ter nenhum teste automatizado —
só verificação manual), o QuickStacks já nasce com:

- `QuickStacks.UnitTests`: regras do `MenuItem` (fábricas, `RegisterLaunch`), de `HexColor`
  (formato `#RRGGBB`) e do `LocalizationService` (paridade de chaves entre os 4 idiomas,
  fallback para `pt-BR`, `SetLanguage`/`LanguageChanged`, `DetectLanguage` nunca devolve um
  código não suportado). 22/22 passando.
- `QuickStacks.IntegrationTests`: `SqliteMenuRepository`/`ConfigExportService` contra um
  arquivo SQLite real e descartável por teste — proteção contra ciclo (mover uma pasta para
  dentro de si mesma/de um descendente), cascata de exclusão (incluindo `FolderAppearance`),
  reordenar irmãos, importar/exportar (inclusive com o pai fora de ordem na lista de
  entrada), busca global (incluindo escape de coringas do `LIKE`), favoritos, recentes, mais
  usados e cor de fundo por pasta (incluindo rejeição de hex mal formado). 16/16 passando.

Nenhum teste de UI/WinUI 3 ainda (a interação de drag-and-drop e o comportamento do ícone de
bandeja só têm a cobertura de "compila e o tipo confere", não de comportamento real — ver
ressalva da seção 4 sobre não haver sessão gráfica disponível neste ambiente).
