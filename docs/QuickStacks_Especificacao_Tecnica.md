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
            QuickStacks.UI/               net8.0-windows10.0.19041.0 — app WinUI 3
        tests/
            QuickStacks.UnitTests/         xUnit — regras do Domain
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

para pular essa etapa inteira. **Isto precisa ser revisto na Fase 5 (i18n com `.resw`)** — se
nesse ponto o build ainda rodar só nesta máquina (sem Visual Studio), a geração de recursos
localizados pode exigir outra abordagem (ex.: build numa máquina/CI com Visual Studio, ou um
mecanismo de recursos que não dependa do MRT).

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

Todos os seis projetos foram compilados com sucesso nesta máquina nessas condições; os dois
projetos de teste rodaram (4/4 e 4/4 testes passando). **O que não foi verificado**: execução
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

## 7. O que ainda não existe (roteiro, em ordem)

- **Fase 2 — Editor visual** (RF14/UC04/UC05): árvore de estrutura com CRUD completo,
  reordenar por drag dentro do mesmo nível (com persistência de `SortOrder`), importar/
  exportar configuração. Referência funcional: o `SettingsWindow` do EasyWinMenu já resolve
  esse conjunto de problemas em WPF — a lógica (não o código) é o que vale portar.
- **Fase 3 — Busca, favoritos, recentes** (RF08–RF13): busca em duas camadas (type-ahead +
  find-com-lista), inspirada no par que já existe no EasyWinMenu, mas com busca global (não
  só no nível atual). `IsFavorite`, `LaunchCount`, `LastUsedUtc` já existem no schema desde a
  Fase 1 — falta só a UI.
- **Fase 4 — Temas** (seção "Temas" do spec original): claro/escuro/seguir Windows via
  `ElementTheme` nativo + Mica/Acrylic; depois cores customizáveis por pasta
  (`ItemThemeOverrides`, tabela nova).
- **Fase 5 — i18n completo**: `.resw` para pt-BR/en-US/es-ES/de-DE, troca dinâmica sem
  reiniciar — **atenção à ressalva da seção 4** sobre a ferramenta MRT/PRI antes de começar.
- **Fase 6 — Importação de `.lnk`** (RF08): resolver `.lnk` via `IShellLinkW` (COM) para
  extrair alvo/argumentos/diretório/ícone reais. O EasyWinMenu nunca fez isso (confirmado –
  hoje ele só classifica `.lnk` pela extensão, sem nunca abrir o arquivo).
- **Fase 7 — Distribuição corporativa**: empacotamento MSIX (precisa da máquina com Visual
  Studio por causa da seção 4), documentação de implantação/atualização.

## 8. Testes automatizados

Diferente do EasyWinMenu (que documenta explicitamente não ter nenhum teste automatizado —
só verificação manual), o QuickStacks já nasce com:

- `QuickStacks.UnitTests`: regras do `MenuItem` (fábricas, `RegisterLaunch`).
- `QuickStacks.IntegrationTests`: `SqliteMenuRepository` contra um arquivo SQLite real e
  descartável por teste — inclui os dois casos de proteção contra ciclo (mover uma pasta para
  dentro de si mesma, e para dentro de um descendente).

Nenhum teste de UI/WinUI 3 ainda (a interação de drag-and-drop e o comportamento do ícone de
bandeja só têm a cobertura de "compila e o tipo confere", não de comportamento real — ver
ressalva da seção 4 sobre não haver sessão gráfica disponível neste ambiente).
