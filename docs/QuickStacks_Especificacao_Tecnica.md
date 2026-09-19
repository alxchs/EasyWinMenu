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

Um segundo obstáculo, mais sério: o alvo WinUI 3 depende de uma ferramenta de geração de
recursos (PRI/MRT — `Microsoft.Build.Packaging.Pri.Tasks.dll`) distribuída junto com o
componente "AppxPackage" do MSBuild do Visual Studio. Por não ser encontrada, a build do
`QuickStacks.UI.csproj` definia `<EnableCoreMrtTooling>false</EnableCoreMrtTooling>` e pulava
a etapa inteira.

> **CORREÇÃO (fase de fechamento).** A afirmação que estava aqui — "esta máquina só tem o
> dotnet SDK puro, sem Visual Studio instalado, confirmado por busca no disco" — **era falsa**.
> O Visual Studio 2022 Community está instalado, com o componente AppxPackage, e a DLL sempre
> esteve em
> `C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Microsoft\VisualStudio\v17.0\AppxPackage\`.
>
> O que acontecia de verdade: `MrtCore.PriGen.targets` procura a DLL em
> `$(AppxMSBuildToolsPath)`, cujo padrão é
> `$(MSBuildExtensionsPath)\Microsoft\VisualStudio\v$(VisualStudioVersion)\AppxPackage\`. Sob
> `dotnet build`, `MSBuildExtensionsPath` aponta para dentro do **SDK do dotnet**, e
> `VisualStudioVersion` resolve para `v18.0` — dois motivos para nunca achar a instalação real.
> O diagnóstico original leu "o MSBuild não acha" como "não existe na máquina", e essa
> conclusão errada custou quatro contornos ao longo das fases seguintes (ver 6.25).
>
> Hoje o `QuickStacks.UI.csproj` localiza a instalação real e liga o MRT de verdade. Quando
> nenhuma instalação é encontrada, a build **avisa** (`warning`) e cai no modo degradado, em vez
> de degradar em silêncio.

Independente disso, a **decisão da Fase 5 continua válida**: a internacionalização é feita com
JSON puro embutido como recurso do assembly (`QuickStacks.Localization`), sem depender de
`.resw`/MRT — ver seção 6.7. Ter o MRT funcionando não a torna obsoleta; só remove a
fragilidade que existia por baixo.

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
cada fase; ver seção 8 para o total atual). **Correção**: ao contrário do que fases
anteriores deste documento afirmavam, esta máquina **tem** sessão gráfica interativa — o app
chegou a ser executado de verdade (seção 6.10) e um bug real de inicialização foi encontrado e
corrigido lá. O que continua não verificado é só a parte fina da interação (arraste visual
para o Explorer, redimensionamento) — ver seção 6.10 para o que já foi confirmado rodando.

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

## 6.8 Fase 6 — Importação de atalhos `.lnk` (RF08)

`ILnkResolver`/`LnkResolver` (`QuickStacks.Infrastructure`): resolve um `.lnk` via COM
(`IShellLinkW` + `IPersistFile.Load`) — a mesma API que o próprio Explorer usa para ler um
atalho, sem nenhum pacote NuGet (o interop COM clássico já funciona direto em
`net8.0-windows`). Extrai alvo, argumentos, diretório de trabalho e localização do ícone
reais — o EasyWinMenu **nunca fez isso**: ele só classificava `.lnk` pela extensão e guardava
o próprio arquivo `.lnk` como `Target`, sem nunca abri-lo (confirmado na investigação da
Fase 1).

- `ILnkImportService`/`LnkImportService`: recebe uma lista de caminhos `.lnk`, resolve cada
  um e cria um `MenuItem` (`MenuItemType.Executable` — o alvo resolvido é sempre lançado via
  `ShellExecute`, igual a qualquer outro item desde a Fase 1) dentro do nó selecionado no
  editor (ou na raiz, se nada estiver selecionado).
- **Alvo de importação segue a mesma regra "como o Explorer" do restante do editor**: se o nó
  selecionado for uma pasta, os atalhos entram dentro dela; se for um item-folha (que não pode
  ter filhos), entram ao lado dele, no mesmo pai — essa regra (`ResolveTargetParentId`) foi
  extraída e também passou a valer para "Nova pasta"/"Novo item" da Fase 2, que antes
  aninhavam incorretamente sob um item-folha selecionado (bug latente da Fase 2, corrigido
  aqui de passagem).
- Botão "Importar atalhos (.lnk)..." no editor, com seleção múltipla de arquivos
  (`FileOpenPicker.PickMultipleFilesAsync`) e diálogo de confirmação mostrando quantos atalhos
  e para onde vão.
- **Verificação real, não só "compila"**: os testes de integração criam um `.lnk` de verdade
  via COM (`IPersistFile.Save`, um caminho de interop escrito separadamente do `LnkResolver`
  em teste, para não testar contra as próprias suposições do código) e conferem que o
  `LnkResolver` lê de volta o alvo/argumentos/diretório corretos — essa é a parte
  tecnicamente arriscada desta fase (interop COM), e passou.

## 6.9 Fase 7 — Distribuição (manual de implantação e atualização)

Duas rotas foram consideradas para a "distribuição corporativa" pedida no spec original
(seção "Distribuição Corporativa": gratuita, uso comercial, sem licenças restritivas, com
documentação de implantação/atualização). Só uma delas foi de fato testada nesta máquina.

### Rota verificada: publicação autocontida ("xcopy deployment")

`quickstacks/publish.ps1` roda:

```
dotnet publish src/QuickStacks.UI -c Release -p:Platform=x64 -r win-x64 --self-contained true -o publish/win-x64
```

e produz uma pasta autocontida (~160 MB — inclui o runtime inteiro do Windows App SDK, já
que é self-contained; RNF02 fala de **memória em repouso**, não de tamanho em disco, então
isso não viola o requisito) com `QuickStacks.UI.exe` pronto pra rodar. **Testado nesta
máquina de ponta a ponta**: o script roda, o publish termina sem erro e o executável e todas
as dependências (incluindo `Assets/quickstacks-placeholder.ico` e os 4 JSONs de idioma
embutidos dentro de `QuickStacks.Localization.dll`) aparecem na pasta de saída.

**Como implantar**: copiar a pasta `publish/win-x64` inteira para a máquina de destino (rede,
pendrive, compartilhamento) e rodar `QuickStacks.UI.exe` — sem instalador, sem precisar ser
administrador (RNF06), sem exigir o Windows App SDK pré-instalado na máquina de destino (por
ser self-contained).

**Como atualizar**: **não existe (ainda) um mecanismo de atualização automática** — atualizar
hoje significa gerar uma nova publicação e substituir a pasta inteira na máquina de destino.
O banco SQLite (`%LocalAppData%\QuickStacks\quickstacks.db`) fica fora da pasta do app, então
substituir os arquivos do programa não apaga a configuração do usuário.

### Rota documentada, mas não verificada: pacote MSIX

O spec original também cita MSIX implicitamente (Store, atualização automática, superfície de
confiança do Windows). Isto **não foi tentado nesta máquina** porque o empacotamento MSIX
depende da mesma ferramenta de PRI/MRT (`Microsoft.Build.Packaging.Pri.Tasks.dll`) cuja
ausência já obrigou a desligar `EnableCoreMrtTooling` desde a Fase 1 (seção 4) — sem Visual
Studio instalado, `WindowsPackageType=Packaged`/`MSIX` provavelmente falharia do mesmo jeito
que a build normal falhava antes daquele ajuste. Para tentar essa rota:

1. Numa máquina com Visual Studio 2022 + workload "Desenvolvimento para plataforma
   universal do Windows" (ou "Windows App SDK"), reverter `EnableCoreMrtTooling=false` no
   `QuickStacks.UI.csproj` (ou condicioná-lo à plataforma de build).
2. Adicionar um `Package.appxmanifest` e mudar `WindowsPackageType` de `None` para `MSIX`.
3. `dotnet publish -p:Platform=x64 -p:WindowsPackageType=MSIX -p:GenerateAppxPackageOnBuild=true`.
4. Assinar o pacote (certificado próprio para distribuição interna, ou Partner Center para a
   Store).

## 6.10 Primeira execução real: crash na inicialização e correção

Depois de publicado (seção 6.9), o QuickStacks foi executado de verdade pela primeira vez
nesta máquina — todas as fases anteriores só tinham `dotnet build`/`dotnet test` como
verificação. **Crashava instantaneamente ao abrir**, com o código de saída `-1073741189`
(`0xC0000409`, uma falha nativa/fast-fail do Windows, não uma exceção .NET capturável — não
gerava nenhum dump nem entrada no Log de Eventos, sinal de que acontecia cedo demais até para
os próprios ganchos de diagnóstico do runtime entrarem em ação).

**Investigação** (por isolamento binário, não por suposição):
1. Um app WinUI 3 mínimo (só uma `Window` vazia, mesmas configurações de projeto) rodou sem
   problema — descartou o build da máquina, a versão do Windows (uma build canary/Insider,
   `10.0.26340`) e a ausência de MRT/PRI como causas.
2. Logs manuais inseridos temporariamente no `App`/`TrayIconWindow` isolaram o travamento
   exatamente dentro do `InitializeComponent()` do `TrayIconWindow` — ou seja, na construção
   da árvore XAML do `TaskbarIcon`, antes de qualquer código próprio rodar.
3. Adicionar o mesmo `TaskbarIcon` com o mesmo menu de contexto a um app mínimo reproduziu o
   problema; removendo peça por peça, isolou-se exatamente o `RadioMenuFlyoutItem` (usado nos
   submenus Tema e Idioma) como o componente que trava/derruba o processo nesta máquina.
   `MenuFlyoutSubItem` sozinho, e `MenuFlyoutItem` comuns, funcionam normalmente — o problema
   é especificamente o `RadioMenuFlyoutItem` dentro de um `TaskbarIcon.ContextFlyout`.
   Trocar `Microsoft.WindowsAppSDK` (1.6 → 1.8) e `H.NotifyIcon.WinUI` (2.1.3 → 2.3.2) não
   mudou nada — não é um bug de versão de pacote, é o controle em si nesta combinação de
   ambiente.

**Correção**: os dois submenus passaram a usar `ToggleMenuFlyoutItem` em vez de
`RadioMenuFlyoutItem`. Como `ToggleMenuFlyoutItem` não tem `GroupName`/exclusividade nativa,
o code-behind (`TrayIconWindow.SetCheckedExclusive`) desmarca manualmente os outros itens do
mesmo grupo sempre que um é marcado — mesmo efeito visual (só uma opção marcada por vez),
sem o componente que causava o problema.

**Resultado**: o app agora abre e permanece rodando e respondendo (confirmado via
`Get-Process ... | Select Responding` alguns segundos depois de aberto, sem diálogo de erro,
sem crash). **Ainda não testado interativamente**: clicar no ícone da bandeja de verdade,
abrir o popup, navegar pelas pastas, arrastar itens — a verificação nesta rodada confirmou
"o processo inicializa e fica de pé", não o funcionamento fim-a-fim de cada recurso. Isso
continua como a pendência mais importante do projeto.

## 6.11 Fase 8 — Feature flag Lite/Full + fundação do tema completo

Pedido novo: um único produto/código com uma flag **Lite** (o QuickStacks como sempre foi) e
**Full** (tudo o que o EasyWinMenu tem — inclusive os grupos soltos na área de trabalho,
confirmado com o usuário que não é só "capacidades soltas", é também esse segundo paradigma
de janela coexistindo com o popup). Um agente catalogou **130 itens distintos** do EasyWinMenu
(lendo `docs/PROMPT.md` inteiro, `docs/OVERVIEW.md` inteiro e ~30 arquivos de código) — o
roteiro completo, fase a fase, com a referência exata de qual arquivo do EasyWinMenu serve de
modelo pra cada item, está registrado no plano local (`deep-twirling-ocean.md`), não
duplicado aqui para não ter duas fontes de verdade desatualizando uma da outra.

- **`FeatureTier`** (`QuickStacks.UI`, mesmo padrão estático de `ThemeService`/
  `LocalizationService`): `Lite`/`Full` em `Settings` (`"product.tier"`), trocável pelo
  submenu "Modo" na bandeja — `ToggleMenuFlyoutItem`, nunca `RadioMenuFlyoutItem` (mesma regra
  da seção 6.10: trava/derruba o processo nesta máquina).
- **Decisão de arquitetura**: um grupo de área de trabalho (fases futuras) não é uma entidade
  paralela — é o mesmo `MenuItem` do tipo `Folder`, só com `IsDesktopGroup=true` e uma
  geometria própria numa tabela nova (`DesktopGroupPlacement`, ainda não criada — entra
  quando a fase dos grupos começar). Mesma lógica que o `MenuCategory.IsDesktopGroup` do
  EasyWinMenu já validava.
- **`FolderTheme`** (`QuickStacks.Domain`): registro com todos os campos do `MenuTheme` do
  EasyWinMenu (cor de fundo/borda/texto/destaque, raio de canto, sombra completa, fontes,
  espaçamento, tamanho de ícone, duração de animação) — cada campo `null` = "usa o tema
  global". A cor de fundo simples da Fase 4 continua na sua própria coluna
  (`FolderAppearance.BackgroundColorHex`, agora aceitando `NULL`); o resto do tema vai
  serializado em `FolderAppearance.ThemeJson` — evita 17 colunas soltas pra um recurso que só
  o modo Full usa. `IMenuRepository.Get/SetFolderThemeAsync`.
- **Editor visual do tema completo ainda não existe** — só o armazenamento. A UI (equivalente
  ao `ThemeEditWindow` do EasyWinMenu) fica pra quando a Fase 9 (grupos soltos) tiver um
  cabeçalho de grupo de verdade pra pendurar esse comando, já que é lá que o EasyWinMenu
  também oferece "Editar tema".

Testes: 24/24 de integração (+ 4 novos cobrindo o `FolderTheme` completo, incluindo o caso de
limpar só a cor de fundo sem perder o resto do tema, e o caso de a linha sumir quando tudo
volta a ficar vazio). App confirmado rodando de pé depois da mudança (mesmo critério da seção
6.10).

## 6.12 Fase 9 — Grupos soltos na área de trabalho (modo Panel) + duas causas-raiz de crash

`DesktopGroupWindow` (nova janela WinUI3): canvas livre com ícones arrastáveis por pasta com
`IsDesktopGroup=true`, geometria persistida em `DesktopGroupPlacement` (1:1 com `MenuItems.Id`,
mesma tabela desenhada na seção 6.11), reflow em cascata pra ícone sem posição salva. Ligar o
modo Full abre uma janela por grupo existente (`TrayIconWindow.OpenAllDesktopGroupsAsync`);
desligar só fecha as janelas — os dados sobrevivem, igual ao EasyWinMenu nunca apagar config do
usuário ao trocar de flag. Arrastar-e-soltar do Explorer pra dentro do grupo via
`StandardDataFormats.StorageItems`. Entrar numa subpasta abre o `PopupWindow` já existente
(Fase 1) ali dentro (`NavigateToFolderAsync`) em vez de duplicar navegação em trilha numa
segunda janela.

Esta fase expôs **dois bugs de inicialização pré-existentes**, nenhum dos dois causado pelo
código novo — só nunca detectados porque `DesktopGroupWindow` foi a primeira janela nova desde
a Fase 1, e o app nunca tinha sido testado de verdade em modo publicado (`dotnet publish`)
depois da Fase 7:

1. **`{ThemeResource LayerFillColorDefaultBrush}` nunca resolvia, desde a Fase 1** —
   `XamlParseException: Cannot find a Resource with the Name/Key ...`. Causa: `App.xaml` nunca
   teve os dicionários padrão do Fluent mergeados (`XamlControlsResources`, o jeito "normal" do
   WinUI 3) porque mergeá-los trava/derruba o processo nesta máquina com o mesmo tipo de falha
   nativa (`0xC0000409`) do `RadioMenuFlyoutItem` da seção 6.10 — reproduzido isolado. Sem essa
   maquiagem, nenhum `{ThemeResource ...}` em nenhuma janela (incluindo `PopupWindow` e
   `EditorWindow`, existentes desde a Fase 1) jamais teria resolvido pra um usuário real — só
   não crashava porque, até a Fase 9, nenhuma dessas janelas tinha sido construída de verdade
   fora do ambiente sintético dos testes de integração (que não sobem UI). Correção definitiva:
   eliminar todo uso de `{ThemeResource ...}` no projeto — `ThemeService.Register(Panel root)`
   agora pinta o fundo de cada janela direto por código, escolhendo a cor a partir de
   `FrameworkElement.ActualTheme` (`CreateDefaultBackgroundBrush`), sem depender de nenhum
   dicionário de recursos do framework.
2. **`dotnet publish` não copiava os `.xbf` (XAML compilado) pro output** —
   `XamlParseException: "XAML parsing failed."` já na primeira janela (`TrayIconWindow`),
   determinístico em toda publicação, mas ausente no `dotnet build` (Debug). Causa:
   `EnableCoreMrtTooling=false` (necessário nesta máquina por não ter Visual Studio — seção 4)
   também desliga, como efeito colateral, o passo que levaria os `.xbf` gerados em `obj/` até o
   diretório publicado; eles ficavam presos em `obj/x64/Release/.../win-x64/*.xbf`, nunca
   chegando à pasta que `publish.ps1` distribui. **Esta é a causa mais provável de o usuário ter
   reportado "parece que estou com o exe errado"** — o instalador/publicação mais recente da
   Fase 7 já rodava sobre uma base sem os dois primeiros vícios corrigidos, mas o efeito prático
   seria o mesmo tipo de sintoma (app "não faz o que deveria"). Correção: um target de MSBuild
   em `QuickStacks.UI.csproj` (`IncludeXbfInPublishOutput`) que inclui os `.xbf` gerados em
   `@(None)` com `CopyToPublishDirectory` antes de `ComputeFilesToPublish`. Verificado depois da
   correção: publicação limpa (`Remove-Item` + `publish.ps1`) com os 5 `.xbf` presentes, app
   publicado sobe sem exceção (`Application.UnhandledException` instrumentado
   temporariamente para confirmar — nenhuma disparada), e com o modo Full ligado e uma pasta
   real marcada `IsDesktopGroup=1` via mutação direta do banco, a janela do grupo abre de
   verdade (`WinUI Desktop`, `Rect=80;80;260;220`, confirmado por UI Automation).

Testes: 29/29 de integração, 22/22 unitários, sem alteração de contagem nesta fase (a fase foi
majoritariamente UI, não testável por automação de banco). Verificação de execução: processo
sobe e fica de pé tanto no `dotnet build` (Debug) quanto no `dotnet publish` (Release,
autocontido) — o critério da seção 6.10 agora cobre explicitamente as duas formas de rodar, não
só uma.

**Ainda não verificado interativamente** (mesma ressalva honesta da seção 6.10/do
`DragGhostWindow` do EasyWinMenu): arrastar um ícone dentro do canvas com o mouse de verdade,
soltar um arquivo vindo do Explorer, redimensionar a janela do grupo, múltiplos grupos abertos
ao mesmo tempo, e o botão "marcar como grupo de área de trabalho" no `EditorWindow` — validados
por leitura de código e pela construção real da janela (via UI Automation), não por interação
de mouse simulada.

## 6.13 Fase 10 — App Folder (ladrilho fechado) + sheet de navegação

`DesktopGroupWindow` ganha um segundo modo de exibição (`DesktopGroupPlacement.DisplayMode`,
campo que já existia desde a Fase 9 mas não tinha UI própria): **Panel** (canvas aberto, como
até aqui) e **AppFolder** (ladrilho fechado — mosaico 3×3 dos primeiros ícones + selo de
contagem + nome, modelo `AppFolderTile.cs` do EasyWinMenu). A mesma janela troca de conteúdo em
tempo real (`ApplyDisplayModeChrome`/`ReloadAsync`) sem recriar nada; alternância pelo menu de
contexto do clique direito (`ToggleDisplayModeItem`) — ainda não o "menu de cabeçalho universal"
completo da Fase 9 original (renomear, organizar, etc. ficam para quando essas ações forem
implementadas), só o toggle Panel/AppFolder que é o escopo desta fase.

Dar duplo-toque no ladrilho fechado abre o **sheet de navegação**: o mesmo `PopupWindow` da
Fase 1, escopado na pasta do grupo (`NavigateToFolderAsync`, já existente desde a Fase 9) — mas
`ActivateCentered()` (novo método, análogo a `ActivateNearCursor`) em vez de perto do cursor,
já que um ladrilho fixo na área de trabalho não tem "onde o usuário clicou" como ponto de
partida natural. Reaproveita o `PopupWindow`/`FolderNavigationViewModel` inteiros — nenhuma
navegação em trilha nova foi escrita.

**Limitação cosmética encontrada e não resolvida nesta fase**: o tamanho pedido para a janela
em modo AppFolder (110×140) não é respeitado à risca — `AppWindow.MoveAndResize` aceitou a
altura (140) mas a largura ficou em ~198px, confirmado via UI Automation
(`Rect=80;80;198;140`). Causa provável: o `OverlappedPresenter` padrão do WinUI 3 aplica um
tamanho mínimo de janela vinculado à moldura/título do sistema, mesmo quando o conteúdo visível
não usa nenhum dos dois — corrigir exigiria trocar o presenter (`IsResizable=false` ou uma
configuração de borda própria), não tentado aqui para não arriscar quebrar a janela também em
modo Panel. O ladrilho em si (mosaico + selo) é desenhado corretamente dentro do espaço
disponível, só a moldura da janela fica mais larga que o ladrilho.

Testes: 29/29 de integração, 22/22 unitários (sem mudança de contagem — fase de UI, não de
dados). Verificação de execução: publicação limpa, os 5 `.xbf` presentes automaticamente (o
fix da seção 6.12 se mantém sem intervenção manual), app publicado sobe sem exceção nos dois
modos (Panel confirmado em `Rect=80;80;260;220`, AppFolder em `Rect=80;80;198;140`, ambos via
UI Automation).

**Ainda não verificado interativamente**: duplo-toque real no ladrilho (só a construção da
janela em modo AppFolder foi confirmada, não o clique), o toggle do menu de contexto
(`ToggleDisplayModeItem`) acionado por um clique direito de verdade, e a persistência do modo
ao fechar/reabrir com múltiplos grupos.

## 6.14 Fase 11 — Organização automática (Grid/ByName/ByType) "viva"

`DesktopGroupPlacement` ganha `Arrangement` (`DesktopIconArrangement`: `None`/`Grid`/`ByName`/
`ByType`, coluna nova `Arrangement TEXT NOT NULL DEFAULT 'None'`, migrada via `EnsureColumn`
para bancos existentes). Com um modo diferente de `None` ativo, `DesktopGroupWindow.ReloadAsync`
ignora `DesktopIconPosition` (a posição livre salva) e recalcula a grade em cascata sempre que
recarrega — `ByName`/`ByType` ordenam antes de colocar, `Grid` usa a mesma ordem de
`SortOrder` de sempre, só forçando a grade em vez de respeitar posições soltas. Arrastar um
ícone manualmente fica desligado (`AttachTileBehavior(..., allowManualDrag: false)`) quando um
arranjo automático está ativo, porque a próxima recarga desfaria o arrasto de qualquer jeito —
menos confuso que deixar arrastar e ver o ícone "voltar" sozinho. Alternável pelo mesmo menu de
contexto do clique direito da Fase 10, num submenu novo ("Organizar por").

Testes: 30/30 de integração (+1 cobrindo persistência de `Arrangement` em
`SetDesktopGroupPlacementAsync`), 22/22 unitários. Verificação de execução: publicação limpa,
app publicado sobe sem exceção com o modo Full ligado (confirmado via UI Automation, mesma
janela do grupo abrindo normalmente).

**Ainda não verificado interativamente**: a reordenação de fato ao trocar de "Livre" para
"Nome"/"Tipo" com múltiplos itens (só a leitura de código e a persistência em banco foram
confirmadas, não o resultado visual do reflow).

## 6.15 Fase 12 — Multi-monitor para grupos soltos + um bug de corrupção de estado corrigido

`MonitorPlacement` (`QuickStacks.Domain`, porta direta do `MonitorPlacement.cs` do
EasyWinMenu, geometria pura sobre `MonitorRect` em vez de `System.Windows.Rect` já que
`QuickStacks.Domain` é `net8.0` puro, sem WPF/WinUI): `IsReachable` (o grupo ainda é alcançável
com o mouse?), `IndexOfOwner` (a que monitor um grupo pertence), `AdjacentIndex` (vizinho na
direção Win+Shift+seta, com wrap-around), `MapBetween` (leva um grupo de um monitor pro outro
preservando a posição relativa), `ClampInto`/`AvoidStacking`. Testado sem hardware real - 10
testes unitários novos sobre retângulos sintéticos (dois monitores lado a lado, grupo fora de
tela, etc.), igual ao módulo original do EasyWinMenu.

`DesktopGroupWindow` usa isso em dois pontos: **resgate ao abrir** (`RescueIfUnreachableAsync`
— se o monitor onde o grupo foi salvo não existe mais, ele volta pra dentro de uma área de
trabalho de verdade em vez de ficar preso fora da tela) e **Win+Shift+seta** movendo o grupo
focado pro monitor vizinho (`RootGrid_KeyDown`, checando os modificadores via
`InputKeyboardSource.GetKeyStateForCurrentThread` — só funciona com a janela em foco, já que
não há hook global de teclado; a mesma decisão de nunca usar `WH_KEYBOARD_LL`/`WH_MOUSE_LL`
que o EasyWinMenu já tinha tomado, item 111 do inventário, não foi reaberta aqui). **Não
implementado nesta fase**: reagir a `DisplaySettingsChanged` em tempo real (plugar/desplugar
monitor com o app já aberto) — só a checagem na abertura de cada janela.

**Dois defeitos reais encontrados e corrigidos durante a verificação, nenhum deles no código
novo desta fase**:

1. `Microsoft.UI.Windowing.DisplayArea.FindAll()` (a API WinRT "certa" para enumerar
   monitores) lança `InvalidCastException: No such interface supported` ao enumerar a lista
   retornada, **nesta máquina** (mesma classe de defeito específico deste ambiente já
   documentada para `RadioMenuFlyoutItem`/`XamlControlsResources` — seções 6.10/6.12).
   Descoberto porque a exceção, não tratada dentro do `InitializeAsync` (Task
   fire-and-forget chamado do construtor), abortava a inicialização da janela silenciosamente
   *depois* que `Activate()` já tinha sido chamado pelo `TrayIconWindow` — o resultado visível
   era a janela abrir do tamanho padrão que o Windows dá a uma janela nova sem
   `MoveAndResize` bem-sucedido (perto do tamanho da tela inteira), nunca do tamanho pedido.
   Corrigido trocando `DisplayArea.FindAll()` por `EnumDisplayMonitors`/`GetMonitorInfo` via
   P/Invoke clássico (`DisplayInventory.cs`) — a mesma API que o `DisplayInventory.cs` do
   EasyWinMenu já usa, sem nenhum tipo WinRT envolvido.
2. `DesktopGroupWindow.AppWindow_Changed` (existente desde a Fase 9) gravava
   `DisplayMode = Panel` e `Arrangement = None` **fixos** toda vez que a janela se movia ou
   redimensionava — apagando silenciosamente a escolha de App Folder (Fase 10) ou organização
   automática (Fase 11) do usuário a cada `MoveAndResize` programático (inclusive os desta
   própria fase: resgate de monitor, troca de modo). Corrigido preservando o `_placement`
   atual e só atualizando `Width`/`Height` quando o modo é `Panel` (em `AppFolder` o tamanho
   da janela é sempre o do ladrilho, gravar por cima destruiria a geometria do Panel guardada
   para quando o usuário voltar a ele).

Testes: 32/32 unitários (+10 de `MonitorPlacement`), 30/30 de integração (sem mudança — os
dois bugs corrigidos são de UI/runtime, não de schema). Verificação de execução: publicação
limpa, os 5 `.xbf` presentes, app publicado sobe sem exceção e a janela do grupo abre com a
geometria correta nos dois modos depois da correção (`Rect=80;80;198;140` em App Folder,
confirmado via UI Automation — antes da correção do item 1, a mesma situação abria a janela em
`Rect≈380;452;2880;1541`, essencialmente do tamanho da tela).

## 6.16 Fase 13 — Atalho global + iniciar com o Windows + restaurar grupos, e um crash fatal corrigido

`GlobalHotkeyService` (`QuickStacks.UI`, modelo `GlobalHotkeyService.cs` do EasyWinMenu):
Ctrl+Alt+Q abre o popup; Win+Ctrl+Alt+D restaura os grupos soltos (só registrado quando o modo
Full está ligado). WinUI 3 não tem `HwndSource` (WPF) para encaixar um `WndProc` customizado —
aqui o HWND real por baixo do `TrayIconWindow` (sempre viva, mesmo escondida) é obtido via
`WindowNative.GetWindowHandle` e "subclassado" com `SetWindowSubclass` (comctl32, compõe com
outros subclasses em vez de substituir o WNDPROC inteiro) só para interceptar `WM_HOTKEY`.
`StartupRegistration` (porta direta do arquivo homônimo do EasyWinMenu): liga/desliga
`HKCU\...\Run` sem precisar de administrador. Ambos alternáveis por `ToggleMenuFlyoutItem` no
menu da bandeja, persistidos em `Settings`.

**Crash fatal encontrado e corrigido durante a verificação**: uma exceção gerenciada lançada
dentro do callback `WM_HOTKEY` (por exemplo, se `ShowPopup`/`RestoreDesktopGroups` lançasse
algo) atravessava a fronteira do callback nativo do `SetWindowSubclass` sem conseguir ser
desenrolada normalmente, resultando em `STATUS_STOWED_EXCEPTION` (`0xC000027B`) — o processo
inteiro derrubado, sem log nenhum, exatamente o mesmo código de falha nativa já visto (e não
relacionado) na investigação da seção 6.12. Reproduzido com uma instrumentação temporária.
Corrigido envolvendo o corpo do callback (`SubclassProc`) num `try/catch` que nunca deixa nada
atravessar de volta para o código nativo do Windows — regra geral para qualquer callback nativo
chamado via P/Invoke neste projeto, não só este.

Testes: 32/32 unitários, 30/30 de integração (sem mudança — funcionalidade de runtime, não de
dados). Verificação de execução: publicação limpa, app publicado sobe sem exceção em modo
Lite (onde o hotkey já está ativo por padrão) e `RegisterHotKey` confirmado retornando sucesso
(`registrado=True`, `GetLastWin32Error=0`) via instrumentação temporária.

**Ainda não verificado interativamente**: a entrega de fato da combinação de teclas pelo
sistema operacional até o callback — `SendKeys` (usado para simular o atalho automaticamente)
não conseguiu disparar o hotkey de forma confiável neste ambiente (sem crash, sem exceção, mas
também sem o popup abrir), uma limitação de simulação de teclado já conhecida deste ambiente,
não do código; `RegisterHotKey` bem-sucedido é a evidência disponível de que o atalho está
corretamente registrado no sistema.

## 6.17 Fase 14 — Instância única, e um bug crítico de Fase 1 nunca antes detectado

`SingleInstanceCoordinator` (`QuickStacks.UI`, porta direta do arquivo homônimo do
EasyWinMenu): um `Mutex` nomeado (`QuickStacks.SingleInstance`) decide se este é o primeiro
lançamento; um segundo lançamento repassa um comando ("open") pra instância já viva via named
pipe (`QuickStacks.DesktopAction`) e sai imediatamente, em vez de abrir um segundo ícone na
bandeja. A primeira instância escuta o pipe num loop em background e despacha o comando pro
`DispatcherQueue` (thread de UI) via `TryEnqueue`.

**Bug crítico encontrado durante a verificação, não relacionado ao código desta fase**: o
teste de ponta a ponta desta fase foi a **primeira vez neste projeto inteiro (desde a Fase 1)
que o `PopupWindow` foi construído de verdade num build publicado** — toda verificação
anterior de UI, em todas as fases anteriores, sempre exercitou `DesktopGroupWindow` ou
`TrayIconWindow`, nunca o popup principal. O resultado: `new PopupWindow(...)` derrubava o
processo inteiro com uma falha nativa irrecuperável (`STATUS_STOWED_EXCEPTION`,
`0xC000027B`) sem nenhuma exceção gerenciada capturável — nem um `try/catch` ao redor da
chamada pegava nada, e `printexception` num dump de crash confirmou "no current managed
exception". Causa raiz isolada removendo elementos do XAML um a um e reproduzindo: o
`BreadcrumbBar` nativo (a trilha de navegação do requisito 1) — um controle Fluent
comparativamente novo e mais complexo que `MenuFlyout`/`ToggleMenuFlyoutItem` (que já
funcionam nesta máquina) — não consegue ser construído sem os dicionários de recursos padrão
do Fluent (`XamlControlsResources`), que **nunca estiveram mergeados em `App.xaml`** desde a
Fase 1 (a mesma causa-raiz da seção 6.12, mas ali o sintoma era uma `XamlParseException`
capturável; aqui é uma falha nativa sem exceção gerenciada nenhuma — provavelmente porque o
`BreadcrumbBar` referencia o recurso ausente de dentro do próprio código nativo/COM do
controle, não de uma property WinUI comum que o runtime gerenciado intercepta).

Isso significa que **a experiência mais básica do produto — clicar no ícone da bandeja pra
abrir o popup — provavelmente nunca funcionou de verdade nesta máquina em nenhuma fase
anterior**, e não foi detectado porque os testes de integração não sobem UI e nenhuma
verificação interativa anterior chegou a construir o `PopupWindow`. Ferramenta usada para
diagnosticar: `dotnet-dump analyze` sobre o `.dmp` que o Windows Error Reporting já salva
automaticamente em `%LOCALAPPDATA%\CrashDumps\` (não precisou de nenhuma instrumentação do
processo em si) — `clrstack -f` mostrou a pilha gerenciada completa até o ponto exato da
falha nativa (`PopupWindow.InitializeComponent` → `LoadComponent` → `IApplicationStaticsMethods.LoadComponent`),
confirmando com precisão qual construtor de janela e qual controle estavam envolvidos.

**Correção**: `BreadcrumbBar` removido e substituído por uma trilha construída à mão
(`StackPanel` horizontal com `Button`/`TextBlock` simples, reconstruída inteira a cada mudança
de `ViewModel.Breadcrumb` via `CollectionChanged`) — o mesmo padrão já usado no
`DesktopGroupWindow` (ladrilhos construídos em código com controles primitivos em vez de
controles "prontos" com dependências de tema obscuras). Nenhuma funcionalidade perdida: clique
em qualquer nível anterior ainda chama `NavigateToBreadcrumbCommand`, o nível atual aparece em
negrito, níveis intermediários são separados por ">".

Testes: 32/32 unitários, 30/30 de integração (sem mudança — UI pura). Verificação de execução:
publicação limpa, duas instâncias lançadas em sequência resultam em **um único processo**
(confirmado via `Get-Process`), e o popup abre de verdade (`Rect=0;1144;420;340`, confirmado
via UI Automation) tanto por auto-teste direto quanto pelo comando repassado de uma segunda
instância via named pipe — a primeira confirmação real, nesta máquina, de que o popup principal
do QuickStacks abre sem crashar.

## 6.18 Fase 15 — Menu de contexto real da área de trabalho, e a internacionalização inteira nunca ter funcionado

`DesktopContextMenuRegistration` (`QuickStacks.UI`, porta direta do arquivo homônimo do
EasyWinMenu): registra um submenu em cascata no próprio menu de contexto real da área de
trabalho do Windows (`HKCU\Software\Classes\DesktopBackground\Shell\QuickStacks`) — o truque
clássico de verbo por registro, sem nenhuma DLL de shell extension nem hospedagem COM. Quatro
verbos: Novo grupo, Todos → App Folder, Todos → Panel, Editar estrutura. Cada verbo relança o
próprio exe com `--desktop-action=<ação>`; o `SingleInstanceCoordinator` (Fase 14) garante que
isso ou abre o app do zero (se estava fechado — `App.OnLaunched` processa a ação direto) ou
repassa a ação pra instância já rodando via named pipe. Registrado/desregistrado junto com o
toggle Lite/Full da bandeja (só faz sentido em Full).

**Segundo bug crítico de Fase 1 nunca antes detectado, encontrado durante a verificação**: os
rótulos dos verbos apareceram como as chaves de tradução cruas (`"desktop.newGroup"` em vez de
`"New group"`) em vez do texto de verdade. Investigação com `dotnet build -v:detailed` revelou
a causa raiz: o `<EmbeddedResource Include="Strings.*.json" />` em
`QuickStacks.Localization.csproj` **nunca embutiu nada no assembly principal** — o MSBuild
reconhece "de-DE"/"en-US"/"es-ES"/"pt-BR" no nome do arquivo como um segmento de **cultura**
(a mesma convenção que `.resx` usa para satellite assemblies) e trata os 4 arquivos como
recursos satélite: os 4 ganham o **mesmo** nome de manifesto (`Strings.json`, sem a cultura) e
vão parar em 4 DLLs satélite separadas — nenhuma delas no assembly principal, onde
`LocalizationService.LoadTable` procura (`assembly.GetManifestResourceStream(...)`, sem
suporte a satellite assemblies). `assembly.GetManifestResourceNames()` no `.dll` publicado
confirmou **zero** recursos embutidos.

Isso significa que **`LocalizationService.Get`/`GetForLanguage` provavelmente nunca retornou
uma tradução de verdade em nenhuma fase deste projeto** (RF15, "internacionalização completa",
implementada na Fase 5) — toda chave sempre caiu no fallback de "chave não encontrada" (que
devolve a própria chave), silenciosamente, sem crash, sem exceção, só texto errado na UI. Não
detectado porque os 3 testes existentes de `LocalizationServiceTests` só comparam as 4 tabelas
**entre si** (`EveryLanguage_HasEveryKeyThatDefaultLanguageHas`) ou verificam o comportamento
do fallback usando uma chave **inexistente de propósito** — nenhum deles jamais checou que uma
chave **conhecida** resolve pra uma tradução real, então quatro tabelas igualmente vazias
"batiam" nos testes de paridade e o fallback "nunca lança" continuava verdadeiro mesmo com
tudo vazio.

**Correção**: `WithCulture="false"` no item `EmbeddedResource` do `.csproj` — desliga a
inferência automática de cultura, cada arquivo passa a virar um recurso comum do assembly
principal com nome distinto (confirmado via `GetManifestResourceNames()`:
`QuickStacks.Localization.Strings.{de-DE,en-US,es-ES,pt-BR}.json`, um por idioma, no assembly
certo). Teste de regressão novo (`GetForLanguage_KnownKey_ReturnsRealTranslation_NotTheKeyItself`)
verifica que uma chave conhecida (`"tray.open"`) retorna a tradução de verdade ("Open"/"Abrir"),
não a chave — o tipo de asserção que faltava e teria pego isso desde a Fase 5.

Testes: 34/34 unitários (+2 do novo teste de regressão), 30/30 de integração. Verificação de
execução: publicação limpa, os verbos aparecem no registro com rótulos corretos (confirmado via
`Get-ChildItem` no `HKCU`), o verbo "Novo grupo" testado de ponta a ponta via
`Start-Process ... -ArgumentList "--desktop-action=new-group"` — resultou num único processo
(instância repassada corretamente), uma nova pasta criada e marcada como grupo, e a janela do
grupo novo abrindo de verdade ao lado da já existente (dois `WinUI Desktop` confirmados via UI
Automation, o novo na posição em cascata `+28px`).

## 6.19 Fase 16 — Área de transferência do Windows (Ctrl+C, Ctrl+X, Ctrl+V com recorte diferido)

Suporte completo a comandos de teclado da área de transferência tanto no popup (`PopupWindow`) quanto nas janelas de grupos de área de trabalho (`DesktopGroupWindow`):

- **Arquitetura desacoplada e Clean Architecture**: `PendingCutCoordinator` (`QuickStacks.Domain`, net8.0 puro, sem dependências de UI) gerencia os itens em estado de recorte pendente (`PendingCutItem`), o consumo por caminhos colados e a detecção de itens que deixaram de existir no disco. 6 testes unitários dedicados em `QuickStacks.UnitTests` validam todo o ciclo de vida.
- **Serviço de UI**: `ClipboardService` (`QuickStacks.UI`) centraliza o interop com a área de transferência do Windows via `StandardDataFormats.StorageItems` (`DataPackageOperation.Copy` para Ctrl+C e `Move` para Ctrl+X) e coordena os proprietários visuais (`ICutVisualOwner`).
- **Recorte diferido (Pending Cut) com feedback visual**: ao pressionar Ctrl+X em um item, o arquivo é colocado na área de transferência e o ladrilho tem sua opacidade reduzida para 50% (`Opacity = 0.5`), exatamente como no Windows Explorer. O item de origem **nunca é apagado antes de confirmar a colagem**.
- **Colagem e consumo de corte**: pressionar Ctrl+V lê os arquivos da área de transferência, adiciona à pasta de destino no SQLite e consome os cortes correspondentes, deletando os itens da origem no repositório e recarregando automaticamente os proprietários afetados.
- **Watcher em disco (Explorer)**: timer a cada 2 segundos (`DispatcherQueueTimer`) verifica se arquivos cortados foram movidos fisicamente no disco por aplicativos externos (`!File.Exists && !Directory.Exists`), finalizando a deleção no banco e desligando o timer quando a fila fica ociosa.

Testes: 40/40 unitários (+6 do `PendingCutCoordinatorTests`), 30/30 de integração. Publicação limpa (`publish.ps1`) verificada com sucesso.

## 6.20 Fase 17 — Extração Real de Ícones e Cache de PNG em Disco

Extração de ícones de alta resolução e cache persistente em PNG para arquivos, executáveis e atalhos:

- **Extração com Fallback multinível**: `IconCacheService` (`QuickStacks.Infrastructure`) utiliza a API de Shell do Windows (`SHGetImageList` com `SHIL_JUMBO` 256x256 e `SHIL_EXTRALARGE` 48x48) via interop COM, com fallback para `Icon.ExtractAssociatedIcon` e ícones de pastas do sistema.
- **Cache Persistente em Disco**: os ícones extraídos são convertidos e salvos como arquivos PNG no diretório `%LocalAppData%\QuickStacks\IconCache\`, indexados por hash SHA-256 do caminho do arquivo e data de modificação (`LastWriteTimeUtc`). Chamadas subsequentes são resolvidas diretamente do disco em milissegundos sem chamadas COM adicionais.
- **Interface e Domínio desacoplados**: `IIconCacheService` (`QuickStacks.Domain`) define o contrato de serviço (`GetIconPath(targetPath)`), permitindo injeção limpa no `MenuEntryViewModel` e `FolderNavigationViewModel`.
- **Renderização WinUI 3**: nos ladrilhos do `PopupWindow` e `DesktopGroupWindow` (inclusive mosaicos de pastas), quando um ícone personalizado/extraído estiver em cache (`entry.HasCustomIcon && entry.IconPath != null`), o componente renderiza uma tag `Image` com `BitmapImage` da imagem PNG; caso contrário, mantém fallback imediato para o `FontIcon` de glifo Segoe Fluent Icons.
- **Testes**: 3 testes de integração em `IconCacheServiceTests` validando extração real de executáveis (`cmd.exe`), cabeçalho PNG (`89 50 4E 47`) e reutilização de cache. Total de 33/33 testes de integração passando.

## 6.21 Fase 18 — Execução de Itens: Paridade Completa com EasyWinMenu

Paridade abrangente de execução de itens, parâmetros de processo, comandos de shell e resiliência:

- **Modos de Execução (`ExecutionMode`)**: adicionado enum `ExecutionMode` (`Normal`, `Minimized`, `Maximized`, `Administrator`) persistido no SQLite na tabela `MenuItems` com coluna migrada automaticamente via `SqliteSchema.EnsureColumn`.
- **Tipo Comando (`MenuItemType.Command`)**: suporte nativo a comandos arbitrários empacotados via `cmd.exe /c "<target>" [arguments]`.
- **Planejamento Puro de Execução (`LaunchPlanner` & `LaunchPlan`)**: componente do domínio puro (`QuickStacks.Domain`) com expansão completa de variáveis de ambiente (`Environment.ExpandEnvironmentVariables`) nos caminhos, argumentos e diretório de trabalho; resolução de nomes de executáveis isolados via diretórios do `%PATH%`; detecção automática de diretório de trabalho quando omitido para arquivos e pastas físicos; e elevação com `Verb = "runas"`.
- **Resiliência de Execução**: `LaunchService` captura e trata graciosamente recusas de prompt do UAC (`Win32Exception` com `NativeErrorCode == 1223`), arquivos inexistentes e falhas de processo sem desestabilizar a interface.
- **Ações de Shell e Menu de Contexto**: tanto nos itens do `PopupWindow` quanto nos ladrilhos de `DesktopGroupWindow`, o menu de contexto oferece:
  - "Executar como administrador"
  - "Abrir local do arquivo" (`LaunchService.RevealInExplorer` com seleção do arquivo)
  - "Copiar caminho" (para a área de transferência do Windows)
  - "Propriedades" (janela de propriedades nativa do Windows via `ShellHelper.ShowProperties` / `ShellExecuteEx` com `SEE_MASK_INVOKEIDLIST`)
  - Chaves localizadas em 4 idiomas (`pt-BR`, `en-US`, `es-ES`, `de-DE`).
- **Editor de Itens (`EditorWindow`)**: adição de seletor de modo de execução e suporte a itens do tipo `Command`.
- **Testes**: 9 novos testes unitários em `LaunchPlannerTests` cobrindo todas as variações de modos, expansões e resolução de PATH; 1 novo teste de integração em `SqliteMenuRepositoryTests` validando round-trip de persistência de `ExecutionMode`. Total de 49 testes unitários e 34 testes de integração passando (83 no total).

## 6.22 Fase 19 — Menu de Contexto Nativo do Shell / Explorer

Hospedagem COM completa do menu de contexto genuíno do Windows Explorer:

- **Hospedagem COM (`IContextMenu`, `IContextMenu2`, `IContextMenu3`)**: `ShellContextMenuService` (`QuickStacks.Infrastructure`) consome a Shell API via `SHParseDisplayName` e `SHBindToParent`, obtendo a interface `IShellFolder` e consultando `IContextMenu` para o item real apontado.
- **Subclassing de Janela (`SetWindowSubclass`)**: intercepta e encaminha dinamicamente mensagens Win32 (`WM_INITMENUPOPUP`, `WM_DRAWITEM`, `WM_MEASUREITEM`, `WM_MENUCHAR`) para `HandleMenuMsg` e `HandleMenuMsg2`, garantindo que extensões de terceiros com owner-drawn e submenus (ex: 7-Zip, Git, WinRAR, VS Code, Tortoise, ferramentas de antivírus) renderizem e processem eventos perfeitamente.
- **Invocação e Desacoplamento de COM**: a seleção é executada via `IContextMenu.InvokeCommand` com liberação determinística de ponteiros COM e PIDL no bloco `finally`. A interface pura `IShellContextMenuService` reside em `QuickStacks.Domain`.
- **Acesso na Interface**: opção "Menu padrão do Windows" nos menus de contexto de itens do `PopupWindow` e ladrilhos do `DesktopGroupWindow`, permitindo acessar todas as extensões do Explorer sem perder o tema e agilidade do QuickStacks.
- **Testes**: 4 testes de integração em `ShellContextMenuServiceTests` validando conformidade com a interface, parâmetros vazios, handles nulos e caminhos inexistentes sem disparar exceções não tratadas. Total de 87 testes (49 unitários + 38 de integração) passando com sucesso.

## 6.23 Fase 20 — Identidade Visual, Help Integrado e Distribuição

Consolidação visual, central de ajuda nativa e pacote de distribuição do QuickStacks:

- **Identidade Visual e Ícones Oficiais**: criação e vinculação do ícone multi-camadas nativo `quickstacks.ico` no `QuickStacks.UI.csproj` como `<ApplicationIcon>` e asset persistido, configurado dinamicamente para o `TrayIcon` e janelas via `AppWindow.SetIcon`.
- **Sistema de Ajuda Integrado (`HelpWindow`)**: janela de apoio ao usuário estruturada e moderna em WinUI 3 com design Fluent, organizada em 4 seções temáticas:
  1. *Visão Geral*: proposta do QuickStacks, comparação detalhada entre Modo Lite e Modo Full, e orientações de personalização.
  2. *Atalhos de Teclado*: matriz de atalhos globais e de navegação (`F1`, `Ctrl+Alt+Q`, `Win+Ctrl+Alt+D`, `Win+Shift+Setas`, `Ctrl+C/X/V`, `Esc`, `Backspace`, etc.).
  3. *Área de Trabalho*: guia completo de uso de grupos soltos (Painel e App Folder), drag & drop do Explorer e menus de contexto no desktop.
  4. *Sobre*: metadados da aplicação (v1.1.0), arquitetura em camadas e diagnóstico ao vivo do ambiente (Modo, Idioma, Tema).
- **Atalho F1 e Acesso Rápido**: acionamento do Help via tecla `F1` em qualquer janela (`PopupWindow`, `DesktopGroupWindow`), além de item dedicado no menu de contexto da bandeja do sistema (`tray.help`).
- **Localização Multilíngue**: paridade completa das novas chaves em português (`pt-BR`), inglês (`en-US`), espanhol (`es-ES`) e alemão (`de-DE`).
- **Script de Distribuição Inno Setup (`tools/QuickStacks.iss`)**: automação do instalador Windows x64 completo com suporte aos 4 idiomas, inicialização opcional com o Windows, ícones na área de trabalho e no menu iniciar, e desinstalação limpa.
- **Bateria de Testes**: 49 testes unitários e 38 testes de integração passando (87 no total).

## 7. O que ainda não existe (roteiro, em ordem)

Todas as fases do roteiro original (Fase 1 a Fase 7) foram implementadas, e o crash de
inicialização foi corrigido (6.10). A partir daqui o roteiro é o programa Lite/Full (Fase 8
em diante, ver 6.11) — fases 9 a 20 cobrem grupos soltos na área de trabalho, tema completo
com editor, organização automática viva, multi-monitor, atalhos globais, iniciar com o
Windows, instância única, menu real da área de trabalho, área de transferência, extração real
de ícone, paridade de execução, menu real do Explorer, e identidade/instalador do modo Full —
o detalhe de cada uma está no plano local, não duplicado aqui.

Trabalho menor, sem fase própria ainda:

- ~~Reordenar itens dentro do mesmo nível por arraste~~ — **feito** (ver 6.24).
- Empacotamento MSIX de verdade (seção 6.9) — precisa de uma máquina com Visual Studio.
- Verificação interativa completa (seção 6.10 confirmou que o app abre e fica de pé, mas não
  cada recurso individualmente): clicar no ícone da bandeja, o popup abrindo de verdade,
  navegar pelas pastas, o drag-and-drop para o Explorer, redimensionamento visual, os
  submenus (agora todos `ToggleMenuFlyoutItem`) marcando/desmarcando corretamente.
- Ícone próprio: `Assets/quickstacks.ico` é byte a byte igual ao
  `quickstacks-placeholder.ico`. A Fase 20 ligou o ícone em todos os lugares certos
  (`<ApplicationIcon>`, `TrayIcon`, `AppWindow.SetIcon`), mas a arte definitiva nunca
  substituiu o marcador — trocar o arquivo basta, nenhum código muda.
- Reverter (ou não) os contornos que a correção de 6.25 tornou desnecessários:
  `RadioMenuFlyoutItem` → `ToggleMenuFlyoutItem` e `BreadcrumbBar` → `StackPanel`.

## 6.25 A `EditorWindow` derrubava o app ao abrir — causa-raiz e correção

Encontrado na verificação interativa (o item que a seção 7 listava como nunca feito). Abrir o
editor — pela bandeja ou por `QuickStacks.UI.exe --desktop-action=open-editor` — mata o
processo inteiro com `0xC000027B` (`STATUS_STOWED_EXCEPTION`) em `Microsoft.UI.Xaml.dll`.
Reproduzido de forma determinística, e **confirmado como anterior a qualquer mudança desta
fase de fechamento** (o mesmo teste derruba o código da Fase 20 sem nenhuma alteração).

Ou seja: a Fase 2 / RF14 está documentada como concluída e aprovada, mas o editor nunca foi
aberto de verdade — os testes que existem cobrem o `EditorViewModel`, nunca a janela.

**Cadeia de causa, confirmada por experimento:**

1. Esta máquina não tem Visual Studio, logo não tem
   `Microsoft.Build.Packaging.Pri.Tasks.dll` (fornecida pelo componente "AppxPackage" do
   MSBuild; `MrtCore.PriGen.targets` a procura em `$(AppxMSBuildToolsPath)`). Por isso a
   seção 4 fixou `<EnableCoreMrtTooling>false</EnableCoreMrtTooling>`.
2. Sem essa etapa, a app não gera `resources.pri` próprio e nenhum `ms-appx:///` resolve —
   mesmo com `Microsoft.UI.Xaml.Controls.pri` presente na pasta de publicação.
3. Sem `ms-appx:///`, o `XamlControlsResources` (dicionário Fluent) não pode ser mergeado no
   `App.xaml`. Tentar mergear troca o sintoma de lugar: o app deixa de abrir, com
   `XamlParseException: Cannot locate resource from
   'ms-appx:///Microsoft.UI.Xaml/Themes/themeresources.xaml'` (testado e revertido).
4. Sem o dicionário Fluent, todo controle cujo estilo padrão mora nele derruba o processo ao
   ser aplicado. É a mesma causa dos contornos anteriores, que foram tratados um a um como
   bugs isolados: `RadioMenuFlyoutItem` → `ToggleMenuFlyoutItem` (seção 5.2 da consolidação) e
   `BreadcrumbBar` → `StackPanel` à mão (Fase 14). A `TreeView` do editor é o caso que nunca
   foi contornado.

**Correção aplicada: a raiz, não o sintoma.** A ferramenta de PRI sempre esteve nesta máquina
— o que faltava era o MSBuild encontrá-la (ver a correção na seção 4). Com isso resolvido, a
cadeia inteira se desfaz de cima para baixo:

1. `QuickStacks.UI.csproj` localiza o `AppxPackage` do Visual Studio numa cadeia explícita de
   candidatos e liga `EnableCoreMrtTooling`. A busca é por propriedade (`Exists(...)`), não por
   item: `MrtCore.PriGen.targets` lê `AppxMSBuildToolsPath` fora de qualquer `Target` e já o usa
   no `UsingTask`, então definir isso dentro de um `Target` seria tarde demais. Quem estiver
   fora do layout padrão passa `-p:AppxMSBuildToolsPath=...`.
2. A build passa a gerar `QuickStacks.UI.pri`. Como `dotnet publish` filtra a saída por
   `@(ResolvedFileToPublish)`, o arquivo entra nessa lista à mão — e também como
   `resources.pri`, que é o nome que o MRT Core procura ao lado do executável no modo
   desempacotado.
3. Com o `.pri` no lugar, `ms-appx:///` volta a resolver e o `App.xaml` finalmente mergeia o
   `XamlControlsResources`.
4. A `TreeView` nativa do WinUI volta a ser usada no editor, no padrão oficial de dados
   hierárquicos. Os alvos de soltar continuam num `Border` dentro do `TreeViewItem`, pela razão
   geométrica descrita acima, então a reordenação de 6.24 vale igual.

**Ganhos que o contorno manual não dava** — verificados por UI Automation no app real: os itens
voltam a expor o papel `TreeItem` (eram `ListItem`) e o padrão `ExpandCollapse`, que leitores de
tela e automação usam para navegar a árvore; o retângulo de foco de teclado volta a aparecer; e
expandir/recolher, virtualização e navegação por setas passam a ser da plataforma, não código
nosso.

**O contorno dos `.xbf` foi removido do caminho normal.** Ele existia pela mesma causa-raiz, e
com o MRT ligado passa a atrapalhar: o compilador XAML emite uma segunda cópia em
`obj\...\embed\`, e o glob antigo pegava as duas, colidindo no mesmo caminho relativo
(`NETSDK1152`). O alvo ficou condicionado ao modo degradado.

**Contornos que podem ser revisitados agora:** `RadioMenuFlyoutItem` → `ToggleMenuFlyoutItem`
(seção 5.2 da consolidação) e `BreadcrumbBar` → `StackPanel` à mão (Fase 14) foram adotados por
esta mesma causa, que não existe mais. Nenhum dos dois foi revertido nesta fase — ambos
funcionam e reverter é risco sem ganho imediato —, mas deixam de ser obrigatórios.

## 6.24 Fechamento — reordenar por arraste, WAL real e auditoria plano × código

Varredura do que a consolidação afirmava contra o que o código fazia, e fechamento das
divergências encontradas:

- **Reordenar irmãos por arraste (pendência da Fase 2)**: a `TreeView` do editor só
  reparentava (soltar sobre uma pasta). Agora a linha inteira é alvo de soltar e a posição
  vertical do ponteiro decide a ação, na convenção do Explorer: 30% de cima insere antes, 30%
  de baixo insere depois, e o miolo continua reparentando quando o alvo é pasta (num item que
  não é pasta, o miolo vira "inserir depois"). `EditorViewModel.TryReorderAsync` regrava o
  `SortOrder` do nível inteiro por `IMenuRepository.ReorderChildrenAsync`, reparentando antes
  quando o item arrastado vem de outro nível. O alvo do `Drop` é a linha (um `Border`), não o
  `TreeViewItem`: a altura do `TreeViewItem` inclui a subárvore inteira quando expandida, o
  que tornaria a divisão em zonas sem sentido. Uma legenda localizada no cursor
  (`editor.drop.*`, nos 4 idiomas) diz qual das três ações vai acontecer, já que as zonas são
  invisíveis. Coberto por `EditorReorderTests` (6 testes).
- **Modo WAL — afirmado desde a Fase 2, nunca ligado**: o banco rodava em
  `journal_mode=delete` e sem `busy_timeout`. Ver seção 5.5 da consolidação para a correção e
  a razão; `SqliteConcurrencyStressTests` cobre a regressão.
- **Documentação corrigida**: o esquema SQL da consolidação (§4.1) descrevia tabelas e colunas
  que nunca existiram com aqueles nomes/tipos, e o diagrama de classes usava membros
  inexistentes (`UseCount`, `CreateLink`, `RecordUsageAsync`). Ambos passaram a refletir o
  código.

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
  usados, cor de fundo por pasta (incluindo rejeição de hex mal formado), importação de
  `.lnk` reais criados via COM no próprio teste (`LnkResolver`/`LnkImportService`, incluindo
  ordenação e não sobrescrita de irmãos existentes) e o tema completo por pasta (Fase 8,
  round-trip de todos os campos, limpar só a cor de fundo sem perder o resto, linha some
  quando tudo volta a ficar vazio). 24/24 passando.

Nenhum teste de UI/WinUI 3 ainda (a interação de drag-and-drop e o comportamento do ícone de
bandeja só têm a cobertura de "compila e o tipo confere", não de comportamento real — ver
ressalva da seção 4 sobre não haver sessão gráfica disponível neste ambiente).
