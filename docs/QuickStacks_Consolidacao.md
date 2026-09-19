# QuickStacks — Documento de Consolidação do Desenvolvimento

> **Versão:** 1.1.0  
> **Data:** Setembro de 2026  
> **Autor:** Alexandre Chaves  
> **Repositório:** `alxchs/EasyWinMenu`  
> **Status:** Concluído e Consolidado (Fases 1 a 20 + CI/CD + Auto-Update)

---

## 1. Resumo Executivo e Visão Geral

O **QuickStacks** é a reconstrução e modernização completa do utilitário de produtividade *EasyWinMenu*, originalmente construído em Delphi e com protótipos em WPF. O projeto foi integralmente redesenhado para a plataforma **.NET 8** no ecossistema moderno do Windows 10/11, utilizando **WinUI 3** (Windows App SDK 1.6) com Fluent Design, banco de dados local **SQLite em modo WAL** com migração incremental automática, integração profunda com o Shell nativo do Windows (COM) e uma arquitetura estrita orientada a **Clean Architecture**, **Clean Code** e princípios **SOLID**.

O QuickStacks oferece aos usuários:
1. **Modo Lite**: Operação rápida, leve e minimalista, mantendo um único ícone na bandeja do sistema (*System Tray*) que abre uma janela popup de acesso imediato a arquivos, aplicativos, pastas e comandos de sistema, com suporte a busca rápida (*type-ahead*), histórico de recentes e favoritos.
2. **Modo Full**: Paridade funcional de 100% com o EasyWinMenu clássico, habilitando grupos visuais soltos na Área de Trabalho (*Desktop Groups* nos formatos Painel em grade ou *App Folder* empilhado), menus de contexto nativos do Windows Explorer via interfaces COM `IContextMenu2/3`, suporte a multi-monitores com atalhos de realocação, e integração com o menu de contexto do desktop do Windows.
3. **Central de Ajuda Integrada**: Janela nativa com guia rápido, tabela de atalhos e acionamento instantâneo via tecla **F1** em qualquer contexto da aplicação.
4. **Distribuição e Atualização Automática**: Instalador standalone compilado via Inno Setup 6 e pipeline de CI/CD no GitHub Actions integrado a domínio privado de atualização (`updates.alxchs.com`).

---

## 2. Arquitetura de Software (Clean Architecture & SOLID)

A solução foi estruturada em camadas rigorosamente desacopladas, com isolamento de responsabilidades e inversão de dependências:

```
                  ┌─────────────────────────────────────┐
                  │           QuickStacks.UI            │
                  │   (WinUI 3, XAML, P/Invoke, Hosts)  │
                  └──────┬──────────────┬───────────────┘
                         │              │
                         ▼              │
    ┌───────────────────────────┐       │
    │  QuickStacks.Application  │       │
    │  (ViewModels MVVM, DTOs,  │       │
    │   UseCases, Export/Import)│       │
    └─────────────┬─────────────┘       │
                  │                     │
                  ▼                     ▼
    ┌───────────────────────────┐ ┌───────────────────────────┐
    │    QuickStacks.Domain     │◄┤ QuickStacks.Infrastructure│
    │  (Modelos Puros, Enums,   │ │ (SQLite WAL, Shell COM,   │
    │   Contratos de Serviços)  │ │  IconCache, UpdateService)│
    └─────────────▲─────────────┘ └───────────────────────────┘
                  │
    ┌─────────────┴─────────────┐
    │  QuickStacks.Localization │
    │  (JSON Dictionaries I18n) │
    └───────────────────────────┘
```

### 2.1. Detalhamento das Camadas

#### `QuickStacks.Domain`
- **Responsabilidade**: O núcleo da aplicação. Não possui dependência de frameworks de interface gráfica (WPF/WinUI) nem de bibliotecas de acesso a dados.
- **Componentes Principais**:
  - `MenuItem`: Entidade central que representa itens do menu (Pastas, Links/Executáveis e Comandos).
  - `DesktopGroupPlacement`: Entidade imutável que registra coordenadas `(X, Y, Width, Height)`, monitor proprietário e modo de exibição (`Panel` vs `AppFolder`).
  - `LaunchPlanner` & `LaunchPlan`: Regras de negócio puras para planejamento e resolução de executáveis, expansão de variáveis `%PATH%` e elevação de privilégios.
  - `MonitorPlacement`: Cálculo determinístico puro de adjacência e pertencimento de retângulos a monitores físicos.
  - `UpdateInfo` & `UpdateCheckResult`: Modelos de dados para o ciclo de auto-atualização.
  - **Contratos (Interfaces)**: `IMenuRepository`, `IIconCacheService`, `IShellContextMenuService`, `IUpdateService`, `IConfigExportService`, `ILnkImportService`.

#### `QuickStacks.Application`
- **Responsabilidade**: Orquestração de casos de uso e gerenciamento de estado desacoplado da interface gráfica através do padrão MVVM (usando `CommunityToolkit.Mvvm`).
- **Componentes Principais**:
  - `FolderNavigationViewModel`: Gerencia pilha de navegação, breadcrumbs, modos de visualização (todas as pastas, favoritos, recentes, mais usados) e histórico de uso.
  - `EditorViewModel`: Árvore hierárquica do menu, operações completas de CRUD, importação de atalhos `.lnk` e exportação/importação de configurações.
  - `MenuEntryViewModel` & `MenuTreeNodeViewModel`: Modelos observáveis de apresentação.

#### `QuickStacks.Infrastructure`
- **Responsabilidade**: Acesso a recursos externos do sistema operacional, persistência local e interop Win32/COM.
- **Componentes Principais**:
  - `SqliteMenuRepository` & `SqliteSchema`: Persistência atômica em SQLite com modo WAL (*Write-Ahead Logging*), timeouts de concorrência e migração incremental de colunas (`EnsureColumn`).
  - `ShellContextMenuService`: Hospedagem COM de `IContextMenu`, `IContextMenu2` e `IContextMenu3` com gancho Win32 seguro via `SetWindowSubclass` (`comctl32.dll`).
  - `IconCacheService`: Extração de ícones nativos de alta fidelidade (Jumbo 256px / Extra Large 48px) via `SHGetImageList` COM com cache em disco em formato PNG.
  - `UpdateService`: Cliente HTTP assíncrono para consumo de manifesto JSON em domínio privado e comparação SemVer de versões.
  - `LnkResolver`: Resolução de atalhos do Windows via `IShellLinkW` e `IPersistFile`.
  - `SettingsStore`: Armazenamento de preferências do usuário baseadas em chave-valor em SQLite.

#### `QuickStacks.UI`
- **Responsabilidade**: Apresentação visual moderna no padrão Windows 11 Fluent Design em aplicação desempacotada (*unpackaged* WinUI 3).
- **Componentes Principais**:
  - `PopupWindow`: Janela popup rápida ancorada ao cursor ou bandeja, com reflow dinâmico e suporte a *drag-and-drop*.
  - `DesktopGroupWindow`: Janelas flutuantes que renderizam grupos na Área de Trabalho com suporte a movimentação livre, multi-monitores e efeito *cut/paste* translúcido.
  - `EditorWindow`: Editor gráfico de estrutura com visualização em árvore.
  - `HelpWindow`: Central de ajuda nativa com abas organizadas e verificação de atualizações.
  - `TrayIconWindow`: Janela invisível que hospeda o ícone da bandeja (`H.NotifyIcon`), menus de contexto e orquestração de instâncias.
  - `SingleInstanceCoordinator`: Controle de instância única via `Mutex` nomeado (`QuickStacks.SingleInstance`) e Named Pipe de comandos (`QuickStacks.DesktopAction`).
  - `GlobalHotkeyService`: Registro de atalhos globais de teclado no Windows (`RegisterHotKey`).
  - `ThemeService`: Aplicação determinística de temas Claro, Escuro ou Sistema sem bloqueios por falta de dicionários Fluent desnecessários.

#### `QuickStacks.Localization`
- **Responsabilidade**: Internacionalização (i18n) completa da aplicação através de arquivos JSON independentes de tooling de compilação MRT/RESW.
- **Idiomas Homologados**:
  - Português do Brasil (`pt-BR`)
  - Inglês dos EUA (`en-US`)
  - Espanhol da Espanha (`es-ES`)
  - Alemão da Alemanha (`de-DE`)
- **Garantia de Qualidade**: Suíte de testes automatizados (`LocalizationServiceTests`) que valida por reflexão que 100% das chaves existentes no idioma padrão existem com conteúdo válido em todos os outros idiomas.

---

## 3. Roteiro de Desenvolvimento: As 20 Fases Concluídas

| Fase | Descrição do Escopo | Branch Dedicada | Commit Principal | Testes Aprovados |
|:---:|:---|:---|:---:|:---:|
| **1** | Fundação Clean Architecture, Domínio Puro e Abstrações | `main` | `5c735d4` | 4 unitários |
| **2** | Repositório SQLite WAL, Migrações e CRUD de Itens | `main` | `f586940` | 8 unitários + 9 integração |
| **3** | Shell Win32, Resolução de Links .lnk e Execução | `main` | `5ec45ea` | 13 unitários + 12 integração |
| **4** | Internacionalização JSON (4 idiomas) e Suporte a Temas | `main` | `378ba69` | 22 unitários + 12 integração |
| **5** | ViewModels MVVM, Navegação por Trilhas e Busca | `main` | `b68379c` | 31 unitários + 12 integração |
| **6** | Interface WinUI 3, Bandeja de Sistema e Popup de Navegação | `main` | `c07a98c` | 31 unitários + 12 integração |
| **7** | Editor Visual de Estrutura, Reordenação e Exportação | `main` | `75317eb` | 31 unitários + 12 integração |
| **8** | Programa Lite vs Full, FeatureTier e Governança de Recursos | `refactory/fase8` | `ad76800` | 31 unitários + 12 integração |
| **9** | Janela de Grupo da Área de Trabalho (DesktopGroupWindow) | `refactory/fase9` | `295a049` | 31 unitários + 16 integração |
| **10** | Modos de Exibição de Grupo: Painel em Grade vs App Folder | `refactory/fase10` | `8c459fc` | 31 unitários + 19 integração |
| **11** | Persistência de Posição, Tamanho e Resgate Fora da Tela | `refactory/fase11` | `43fe5da` | 31 unitários + 24 integração |
| **12** | Suporte a Múltiplos Monitores e Atalhos Win+Shift+Setas | `refactory/fase12` | `7f53a47` | 40 unitários + 24 integração |
| **13** | Atalhos Globais de Teclado e Inicialização com Windows | `refactory/fase13` | `6b97669` | 40 unitários + 25 integração |
| **14** | Instância Única e Comunicação IPC via Named Pipes | `refactory/fase14` | `da7c08b` | 40 unitários + 28 integração |
| **15** | Integração com Menu de Contexto Real da Área de Trabalho | `refactory/fase15` | `e2a537f` | 40 unitários + 30 integração |
| **16** | Área de Transferência: Ctrl+C, Recortar Translúcido e Colar | `refactory/fase16` | `cfa1bb1` | 40 unitários + 33 integração |
| **17** | Extração Real de Ícones Jumbo 256px COM e Cache em PNG | `refactory/fase17` | `01e51af` | 40 unitários + 33 integração |
| **18** | Paridade Completa de Execução: ExecutionMode, Command, Shell | `refactory/fase18` | `3f16346` | 49 unitários + 34 integração |
| **19** | Menu de Contexto Nativo Shell COM (IContextMenu2/3) | `refactory/fase19` | `5b5e480` | 49 unitários + 38 integração |
| **20** | Identidade Visual, Central de Ajuda Integrada (F1) e Inno Setup | `refactory/fase20` | `426965d` | 49 unitários + 38 integração |
| **Final** | Auto-Atualização em Domínio Privado, CI/CD e Consolidação | `refactory/final` | `70a61c4` | 49 unitários + 48 integração (97 total) |
| **Fechamento** | Reordenar por arraste (pendência da Fase 2), WAL/`busy_timeout` reais e auditoria plano×código | `refactory/final` | *este commit* | **49 unitários + 60 integração (109 total)** |

---

## 4. Persistência de Dados e Esquema SQLite

O banco de dados SQLite local reside em `%LocalAppData%\QuickStacks\quickstacks.db`. O motor opera em modo **WAL (Write-Ahead Logging)** com concorrência segura e isolamento de locks.

### 4.1. Estrutura das Tabelas

```sql
-- Itens de menu, pastas e comandos executaveis.
-- Type e' gravado como TEXTO (o nome do enum MenuItemType: Folder, Shortcut, Url,
-- Executable, Command) - nao como inteiro.
CREATE TABLE IF NOT EXISTS MenuItems (
    Id               TEXT PRIMARY KEY,
    ParentId         TEXT NULL REFERENCES MenuItems(Id) ON DELETE CASCADE,
    Name             TEXT NOT NULL,
    Description      TEXT NULL,
    Type             TEXT NOT NULL,
    Path             TEXT NULL,
    Arguments        TEXT NULL,
    WorkingDirectory TEXT NULL,
    Icon             TEXT NULL,
    SortOrder        INTEGER NOT NULL DEFAULT 0,
    IsFavorite       INTEGER NOT NULL DEFAULT 0,
    LaunchCount      INTEGER NOT NULL DEFAULT 0,
    LastUsedUtc      TEXT NULL,
    CreatedAt        TEXT NOT NULL,
    UpdatedAt        TEXT NOT NULL,
    ExecutionMode    INTEGER NOT NULL DEFAULT 0  -- 0=Normal, 1=Minimized, 2=Maximized, 3=Administrator
);
-- IsDesktopGroup INTEGER NOT NULL DEFAULT 0 entra por EnsureColumn (ver 4.2), nao no CREATE.

CREATE INDEX IF NOT EXISTS IX_MenuItems_ParentId ON MenuItems(ParentId);

-- Configuracoes chave-valor da aplicacao
CREATE TABLE IF NOT EXISTS Settings (
    Key   TEXT PRIMARY KEY,
    Value TEXT NOT NULL
);

-- Tema por pasta (Fase 4/8): so' existe linha aqui quando a pasta foi customizada.
CREATE TABLE IF NOT EXISTS FolderAppearance (
    FolderId           TEXT PRIMARY KEY REFERENCES MenuItems(Id) ON DELETE CASCADE,
    BackgroundColorHex TEXT NULL,
    ThemeJson          TEXT NULL
);

-- Geometria da janela solta de um grupo (Fase 9). Coordenadas em REAL, nao INTEGER,
-- e DisplayMode/Arrangement gravados como texto do enum.
CREATE TABLE IF NOT EXISTS DesktopGroupPlacement (
    GroupId     TEXT PRIMARY KEY REFERENCES MenuItems(Id) ON DELETE CASCADE,
    X           REAL NOT NULL,
    Y           REAL NOT NULL,
    Width       REAL NOT NULL,
    Height      REAL NOT NULL,
    DisplayMode TEXT NOT NULL,          -- Panel | AppFolder
    IconScale   REAL NOT NULL,
    IsCollapsed INTEGER NOT NULL DEFAULT 0,
    Arrangement TEXT NOT NULL DEFAULT 'None'
);

-- Posicao livre de cada item pinado no canvas do grupo que o contem (Fase 9, modo Panel).
CREATE TABLE IF NOT EXISTS DesktopIconPosition (
    ItemId TEXT PRIMARY KEY REFERENCES MenuItems(Id) ON DELETE CASCADE,
    X      REAL NOT NULL,
    Y      REAL NOT NULL
);
```

### 4.2. Migração Contínua e Segura
A evolução do esquema em instâncias existentes é realizada pelo helper de infraestrutura `SqliteSchema.EnsureColumn()`. O mecanismo inspeciona `PRAGMA table_info` dinamicamente e executa `ALTER TABLE ... ADD COLUMN` sem exigir recriação do banco de dados ou perda de configurações prévias.

---

## 5. Decisões Técnicas Críticas & Lições Aprendidas

Durante as 20 fases de engenharia reversa e reescrita, diversas decisões de baixo nível foram tomadas para garantir estabilidade absoluta no Windows:

1. **WinUI 3 Desempacotado sem Visual Studio / MRT Tooling**:
   - Em máquinas de compilação sem as ferramentas legadas do Visual Studio, desabilitou-se o MRT Core (`<EnableCoreMrtTooling>false</EnableCoreMrtTooling>`).
   - Para evitar que o `dotnet publish` ignorasse o XAML compilado (`.xbf`), implementou-se o target MSBuild customizado `IncludeXbfInPublishOutput`, que mapeia deterministamente todos os binários XAML para a raiz do pacote de saída.
2. **Substituição de `RadioMenuFlyoutItem` por `ToggleMenuFlyoutItem`**:
   - Descobriu-se que o componente `RadioMenuFlyoutItem` do WinUI 3 provocava encerramento abrupto do processo em builds desempacotadas sem dicionários Fluent mergeados no `App.xaml`.
   - Adotou-se `ToggleMenuFlyoutItem` com exclusividade mútua gerenciada via código (`SetCheckedExclusive`), com comportamento equivalente.
   - **Atualização (fase de fechamento):** isto era um contorno, não a correção. A causa real era o `App.xaml` não conseguir mergear o dicionário Fluent por falta de `resources.pri` — hoje resolvida na raiz (seção 6.25 da especificação técnica). O contorno segue no código porque funciona, mas deixou de ser necessário.
3. **Subclassing com `SetWindowSubclass` para Menus de Contexto COM**:
   - Para hospedar menus de contexto nativos do Windows Explorer (`IContextMenu2` e `IContextMenu3`), mensagens Win32 fundamentais como `WM_INITMENUPOPUP`, `WM_DRAWITEM`, `WM_MEASUREITEM` e `WM_MENUCHAR` precisavam ser interceptadas antes do framework gráfico.
   - Utilizou-se a API `SetWindowSubclass` da biblioteca nativa `comctl32.dll` com ponteiros estáticos de callback, permitindo que extensões de terceiros (ex: 7-Zip, Git, antivírus, editores de código) desenhem seus itens proprietários e processem submenus dinâmicos com perfeição.
4. **Resiliência a Elevação UAC**:
   - Ao executar um aplicativo com solicitação de privilégios de administrador (`Verb = "runas"`), a recusa do usuário no diálogo UAC gera nativamente `Win32Exception` com código de erro `1223` (`ERROR_CANCELLED`). O `LaunchService` captura e trata essa exceção de forma transparente, mantendo a aplicação viva e sem popups de erro intrusivos.
5. **WAL e `busy_timeout` são por arquivo e por conexão — e precisam ser ligados explicitamente**:
   - O `Microsoft.Data.Sqlite` abre em `journal_mode=delete` por padrão, e o padrão de acesso
     real do modo Full (popup + editor + uma `DesktopGroupWindow` por grupo, cada um com seu
     próprio `SqliteMenuRepository` contra o mesmo arquivo) transformava isso em
     `SQLite Error 5: 'database is locked'` sob escrita concorrente.
   - `journal_mode=WAL` e `synchronous=NORMAL` ficam gravados no arquivo e são ligados uma vez
     em `SqliteSchema.EnsureCreated`. Já `foreign_keys` e `busy_timeout` **não** são
     persistidos: valem por conexão, e por isso toda abertura passa por
     `SqliteSchema.OpenConnection` — o ponto único que os reaplica.
   - Regressão coberta por `SqliteConcurrencyStressTests` (seção 7.2), que falhava com 17
     exceções de lock antes da correção.
6. **Prevenção de Falhas Críticas com Named Pipes e DispatcherQueue**:
   - Exceções gerenciadas não capturadas dentro de delegates do `DispatcherQueue.TryEnqueue` levam o runtime do Windows App SDK a encerrar o processo com `STATUS_STOWED_EXCEPTION`. A infraestrutura do `SingleInstanceCoordinator` isola todas as chamadas de dispatch em blocos protegidos com logging preventivo.

---

## 6. Controle de Auto-Atualização com Domínio Privado

Para permitir que a aplicação se mantenha atualizada de forma autônoma sem depender de lojas centralizadas ou ferramentas pesadas de terceiros, foi desenvolvida uma arquitetura de distribuição leve e desacoplada:

```
                          ┌──────────────────────────┐
                          │   Push Tag (ex: v1.1.0)  │
                          └─────────────┬────────────┘
                                        │
                                        ▼
                   ┌─────────────────────────────────────────┐
                   │          GitHub Actions CI/CD           │
                   │  1. Restaura e compila solução .NET 8   │
                   │  2. Executa 109 testes unit/integration │
                   │  3. Publica binário self-contained x64  │
                   │  4. Gera instalador Inno Setup (.exe)   │
                   │  5. Calcula SHA256 do instalador        │
                   │  6. Gera manifesto version.json         │
                   └─────────────┬─────────────┬─────────────┘
                                 │             │
                   ┌─────────────┘             └─────────────┐
                   ▼                                         ▼
    ┌───────────────────────────┐             ┌───────────────────────────┐
    │   GitHub Release Assets   │             │   Domínio Privado HTTPS   │
    │  (Backup público aberto)  │             │   (updates.alxchs.com)    │
    └───────────────────────────┘             └──────────────┬────────────┘
                                                             │
                                                             │ GET /version.json
                                                             ▼
                                              ┌───────────────────────────┐
                                              │   QuickStacks Cliente     │
                                              │   (UpdateService no app)  │
                                              │   • Compara SemVer        │
                                              │   • Notifica usuário      │
                                              │   • Baixa instalador .exe │
                                              └───────────────────────────┘
```

### 6.1. O Manifesto de Versão (`version.json`)
O servidor em domínio privado (`updates.alxchs.com`) serve o arquivo estático `version.json` via HTTPS:
```json
{
  "version": "1.1.0",
  "downloadUrl": "https://updates.alxchs.com/quickstacks/QuickStacks-Setup-v1.1.0-x64.exe",
  "changelog": "QuickStacks v1.1.0 - Atualização automática com Clean Architecture, WinUI 3 Fluent e integração completa com Windows Shell.",
  "sha256": "4a7b8c...d9e1",
  "isMandatory": false,
  "releaseDateUtc": "2026-09-18T00:00:00Z"
}
```

### 6.2. Comparação de Versões e Integração no Cliente
- `IUpdateService` no domínio expõe `CheckForUpdatesAsync()` e `CompareVersions()`.
- A comparação de versões é compatível com o padrão SemVer 2.0 (ex.: `1.2.0` > `1.1.0`, tratando prefixos `v` e sufixos de release).
- A interface de usuário disponibiliza a verificação na aba **Sobre** da central de ajuda (`HelpWindow`), informando o status em tempo real e exibindo o botão de download direto quando uma nova versão for detectada.

---

## 7. Instruções de Operação, Compilação e Testes

### 7.1. Compilação com `mkfile`
Conforme as diretrizes do ambiente do usuário, o script unificado `mkfile` gerencia o ciclo de compilação:

```powershell
# Compilação em modo de desenvolvimento (Debug)
mkfile d

# Compilação otimizada para distribuição (Release)
mkfile r

# Empacotamento
mkfile p
```

### 7.2. Execução da Suíte Completa de Testes
```powershell
# Executa os 49 testes unitários puros de Domínio e Localização
dotnet test quickstacks/tests/QuickStacks.UnitTests

# Executa os 60 testes de integração com SQLite, Shell COM, UpdateService,
# reordenação do editor e stress de concorrência
dotnet test quickstacks/tests/QuickStacks.IntegrationTests

# Só os testes de stress de concorrência (WAL, escritores/leitores simultâneos)
dotnet test quickstacks/tests/QuickStacks.IntegrationTests --filter "FullyQualifiedName~SqliteConcurrencyStressTests"
```
*Status:* **109 testes passando com 100% de sucesso.**

### 7.3. Publicação e Geração do Instalador
```powershell
# 1. Publicar binários self-contained para Windows x64
dotnet publish quickstacks/src/QuickStacks.UI/QuickStacks.UI.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -o quickstacks/publish/win-x64

# 2. Compilar instalador standalone com Inno Setup 6
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" tools/QuickStacks.iss /DMyAppVersion=1.1.0
```
O executável final do instalador é gerado em `dist/QuickStacks-Setup-v1.1.0-x64.exe`.

> **Estado real nesta máquina:** o passo 1 (publicação self-contained) roda e produz
> `quickstacks/publish/win-x64/QuickStacks.UI.exe`. O passo 2 **não** roda aqui: o Inno Setup 6
> não está instalado nesta estação, então `dist/` ainda não contém nenhum instalador do
> QuickStacks (o `EasyWinMenuSetup-1.1.0.1.exe` que está lá é do produto antigo). Quem gera o
> instalador hoje é o workflow do GitHub Actions, que instala o Inno Setup via `choco` antes de
> chamar o `ISCC.exe`. O `tools/build_installer.ps1` detecta a ausência e avisa, em vez de falhar.

---

## 8. Conclusão

O projeto **QuickStacks** concluiu todas as suas metas de arquitetura, qualidade de código e paridade funcional com êxito. O software está completamente desacoplado, com testes automatizados abrangentes, documentação técnica exaustiva, interface fluida em WinUI 3, empacotamento automatizado e infraestrutura de entrega contínua pronta para produção.

