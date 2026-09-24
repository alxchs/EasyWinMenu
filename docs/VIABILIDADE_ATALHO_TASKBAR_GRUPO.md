# Viabilidade: atalho de grupo na barra de tarefas

> Pedido: um item no menu de contexto (clique direito no grupo ou no título)
> que crie um atalho na barra de tarefas para **abrir / visualizar / trazer
> para frente** aquele grupo.
>
> Estado: **A (atalho `.lnk` com argumento) está implementada na versão 1.1.1.0**;
> **A+ (fixar sozinho) e B (janela-proxy) continuam só analisadas.** Onde o texto abaixo diz
> "criar", leia como "foi criado" para o que é de A. Medições reais de A: um grupo na posição 36
> da ordem-z foi para a 3 em ~0,6 s; com todos os grupos fechados, o atalho reabriu só o dele; um
> id inexistente não derrubou o app. Toda afirmação sobre o código foi lida do código atual.
> Os custos são **estimativas**, não medições — ver "Como os custos foram estimados".

## Veredito

**Viável.** Há duas formas de entregar, com custos muito diferentes:

| | A. Atalho `.lnk` com argumento | B. Botão de barra de tarefas por grupo (janela-proxy) |
|---|---|---|
| Como o usuário fixa | Arrasta o `.lnk` gerado para a barra (o app abre a pasta com o arquivo selecionado); ou "A+", abaixo, que fixa sozinho | Clica com o direito no botão que apareceu e escolhe "Fixar na barra de tarefas" |
| Botão na barra com o app rodando | Não (o app hoje é só bandeja) | Sim, um por grupo escolhido, com indicador de "aberto" |
| Clique | Inicia um processo curto que repassa o comando ao app já aberto e sai | Ativa a janela-proxy, que traz o grupo para frente |
| Miniatura / Aero Peek | Não | Possível, mas é a parte mais cara |
| Risco | Baixo | Médio-alto |
| Tempo estimado | **1,5 a 2,5 h** | **7 a 11 h**, em 2 a 3 sessões |
| Tokens estimados (saída / totais processados) | **35–50 mil / 0,6–1 milhão** | **120–200 mil / 3–5 milhões** |
| Recomendação | **Fazer primeiro** | Só se o usuário quiser o indicador de "aberto" ou a miniatura |

Pré-requisito comum às duas (custo já incluído acima): dar identidade estável ao
grupo (ver "Identidade do grupo").

## O que o código já tem e barateia a solução

- **Instância única com canal de comandos.** `SingleInstanceCoordinator`
  (`Services/SingleInstanceCoordinator.cs`) segura o mutex `EasyWinMenu.SingleInstance`
  e escuta o pipe nomeado `EasyWinMenu.DesktopAction`. Um segundo processo que abre
  chama `TrySendToRunningInstance(action)` e sai; o primeiro recebe a string em
  `HandleDesktopAction` (`App.xaml.cs`).
- **Argumento de linha de comando já vira ação.** `DesktopContextMenuRegistration.ParseAction`
  lê argumentos com o prefixo `ArgumentPrefix` e devolve a ação. Se o app ainda **não**
  estiver rodando, `OnStartup` executa a mesma ação depois de carregar
  (`startupAction`). O comportamento "abrir o app e já fazer X" existe.
- **Trazer janela para frente já existe** para o conjunto: `DesktopGroupWindow`
  mantém `_allGroupWindows` e tem `BringAllGroupsToFront`, `SendOthersToBack` e
  `SendAllToBack`, todos por `SetWindowPos` com `HWND_TOP`/`HWND_BOTTOM`.
- **Um grupo pode estar fechado.** `MenuCategory.IsClosed` e
  `DesktopOrganizerService.ToggleGroup` existem; "abrir o grupo" e "trazer para frente"
  são dois casos da mesma ação.

## O que **não** existe e precisa ser criado

1. **Identidade estável do grupo.** `MenuCategory` só tem `Name`
   (`EasyWinMenu.Core/Models/MenuCategory.cs`). Renomear um grupo ou ter dois com o
   mesmo nome quebraria um atalho que aponte para o nome. Definição: acrescentar
   `Id` (GUID) em `MenuCategory`, gerado ao carregar quando ausente e gravado na
   próxima persistência. Configs antigas continuam abrindo (o campo é opcional no JSON).
2. **Comando "focar grupo".** Nova ação `focus-group:<id>` tratada em
   `HandleDesktopAction` (hoje o `switch` compara a string inteira; passa a haver um
   ramo por prefixo). Comportamento definido:
   - grupo fechado → abrir;
   - grupo recolhido (`AppFolder`) → abrir a folha; grupo em painel → só trazer;
   - janela minimizada não se aplica (janelas de grupo ficam na camada da área de
     trabalho), mas o grupo deve ficar **acima das demais janelas** por um instante
     e receber um destaque visual curto (pulso na borda), depois volta à camada normal
     — para não quebrar a ordem que o usuário escolheu em "Ordem das janelas".
3. **Item de menu** "Criar atalho na barra de tarefas" no menu do grupo (clique direito
   no fundo e no título), com o texto nos 8 idiomas em `Localization/Strings.*.json`.
4. **Gerar o `.lnk`.** Não há criação de atalhos `.lnk` no código; entra um wrapper
   `IShellLink`/`IPersistFile` (~60 linhas de interop COM), com destino
   `EasyWinMenu.App.exe`, argumento da ação, ícone do grupo e nome do grupo.

## Fixar na barra de tarefas: o que foi testado (e uma correção minha)

A primeira versão deste documento dizia que o Windows 11 "não deixa fixar programaticamente".
Isso foi dito de forma absoluta demais, e o Alexandre apontou o caminho certo: os atalhos
fixados ficam como arquivos `.lnk` em
`%AppData%\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar` (existe nesta
máquina, com 26 atalhos), e a pasta é gravável pelo usuário. O que **foi medido** aqui:

- Criar um `.lnk` de teste nessa pasta **sozinho não fixa nada**: o valor `Favorites` de
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband` continuou com os mesmos
  22.865 bytes, esperando 8 s.
- **Reiniciar o Explorer também não fixa**: depois do reinício o `Favorites` continuou com
  22.865 bytes e sem o nome do arquivo de teste.
- Conclusão: a lista que o Explorer usa para desenhar os botões é o valor binário
  `Favorites` (mais `FavoritesResolve`, `FavoritesVersion` e `FavoritesChanges`), e a pasta é
  só o depósito dos `.lnk` a que ele se refere. Para fixar **por código**, é preciso escrever
  o `.lnk` na pasta **e** acrescentar a entrada no `Favorites` (formato binário não
  documentado, com identificadores de item do shell) e reiniciar o Explorer. Esse último
  passo **não foi testado**; só foi medido que a pasta sozinha não basta.

Consequência para o desenho: existe uma **Implementação A+** (fixar de verdade, sem o usuário
arrastar), com custo e risco maiores que A simples:

| | A (arrastar o `.lnk`) | A+ (pasta + `Favorites` + reiniciar Explorer) |
|---|---|---|
| Usuário faz algo depois | Arrasta o arquivo | Nada (o botão aparece) |
| Depende de formato não documentado | Não | **Sim** (`Favorites`), pode quebrar numa atualização do Windows |
| Reinicia o Explorer | Não | **Sim** (a barra pisca; as janelas abertas continuam) |
| Outro usuário logado | Não se aplica | Precisa do perfil (`HKU\<SID>`) e da pasta dele, só com o hive carregado |
| Tempo adicional sobre A | 0 | +2 a 3 h (engenharia reversa do blob e testes de reversão) |
| Tokens adicionais | 0 | +40 a 70 mil de saída / +1 a 2 milhões totais (estimativa) |

Recomendação revisada: entregar A primeiro, com **A+ como opção do menu** ("Fixar na barra
de tarefas agora") que faz backup da chave `Taskband` antes de mexer e desfaz se algo falhar.

## Implementação A — atalho `.lnk` com argumento

### Fluxo
1. Usuário: clique direito no grupo → **Criar atalho na barra de tarefas**.
2. App grava `%AppData%\EasyWinMenu\GroupShortcuts\<Nome do grupo>.lnk`
   (destino `EasyWinMenu.App.exe`, argumentos `<prefixo>focus-group:<id>`,
   ícone do grupo, `AppUserModelID` próprio por grupo).
3. App abre o Explorer com o arquivo selecionado e mostra uma dica de uma linha:
   "Arraste o atalho para a barra de tarefas".
4. Depois de fixado, cada clique inicia `EasyWinMenu.App.exe` com o argumento. O
   processo novo não consegue o mutex, envia o comando pelo pipe e sai (o caminho já
   existente). Se o app não estiver rodando, ele sobe e executa a ação inicial.

### Definições de comportamento
- Clique com o app rodando: o grupo abre, vem para frente e pulsa. Em menos de meio
  segundo, sem janela extra.
- Grupo apagado depois: o atalho continua existindo; ao clicar, o app mostra uma
  bandeja de aviso "Grupo não encontrado" e não faz mais nada.
- Grupo renomeado: o atalho continua funcionando (é pelo `Id`); o nome do arquivo
  `.lnk` fica desatualizado — o app o renomeia ao renomear o grupo, se o arquivo existir.
- Duplo clique / vários cliques rápidos: o pipe serializa; o segundo comando vira
  "trazer para frente" de novo, sem efeito colateral.
- Sem indicador de "aberto" e sem miniatura: o ícone fixado é estático.

### Estimativa (A)

| Etapa | Tempo |
|---|---|
| `Id` no `MenuCategory` + migração | 0,3 h |
| Ação `focus-group` + abrir/trazer/pulso | 0,5 h |
| Gerador de `.lnk` (COM) + item de menu + 8 idiomas | 0,5 h |
| Verificação com a aplicação aberta (clique no `.lnk`, com app aberto e fechado, grupo fechado, renomeado) | 0,4 h |
| Documentação (`OVERVIEW.md`, `PROMPT.md`) e novo instalador | 0,2 h |
| **Total** | **~1,5–2,5 h** |

Tokens estimados: **35–50 mil de saída**, **0,6–1 milhão processados** (leitura de
`DesktopGroupWindow.cs`, com 3.700 linhas, e do ciclo compilar/abrir/capturar tela).

## Implementação B — botão de barra de tarefas por grupo (janela-proxy)

### Ideia
Cada grupo escolhido ganha uma janela-proxy minúscula e transparente com
`ShowInTaskbar = true`, e um `AppUserModelID` próprio gravado pela API de propriedades
da janela (`SHGetPropertyStoreForWindow`) junto de `RelaunchCommand`,
`RelaunchDisplayNameResource` e `RelaunchIconResource`. Com isso o Windows trata o
botão como um aplicativo à parte:

- o usuário fixa pelo menu nativo do botão (sem arrastar `.lnk`);
- o botão fixado **sobrevive** ao app fechado e, ao clicar, relança o app com o
  argumento certo;
- com o app rodando, clicar ativa a proxy, e a proxy traz o grupo para frente.

### Por que custa muito mais
- **Alt+Tab e barra poluídos:** uma proxy por grupo aparece no Alt+Tab. Esconder a
  proxy do Alt+Tab (`WS_EX_TOOLWINDOW`) **também** tira o botão da barra — os dois
  objetivos entram em conflito e precisa de uma solução por janela em camadas.
- **Ciclo de vida:** abrir/fechar grupo, recolher, mover de monitor, renomear e apagar
  precisam criar, esconder e destruir a proxy corretamente.
- **Miniatura:** mostrar uma imagem do grupo no botão exige
  `DwmSetIconicThumbnail`/`DwmSetIconicLivePreviewBitmap` com captura do grupo
  (o projeto já tem captura de janela em `tools/`, mas não dentro do produto).
- **Histórico do projeto:** o mesmo tipo de janela auxiliar já deu regressões de
  DPI, multi-monitor e ordem-z (ver `OVERVIEW.md`, "Multi-monitor" e as correções do
  arraste). É onde está o risco.
- **Escolha por grupo:** com 4 grupos no config atual, um botão para cada um seria
  ruído. Precisa de um marcador por grupo ("Mostrar na barra de tarefas") — mais um
  campo no modelo e mais um item de menu.

### Estimativa (B)

| Etapa | Tempo |
|---|---|
| Tudo da Implementação A que é reaproveitado (Id, ação, pulso) | 0,8 h |
| Janela-proxy + `AppUserModelID` + propriedades de relançamento | 2 h |
| Ciclo de vida (abrir/fechar/recolher/renomear/apagar/multi-monitor) | 2 h |
| Solução Alt+Tab × botão de barra | 1,5 h |
| Miniatura do grupo (opcional) | 2–3 h |
| Verificação real em cada cenário + regressão do arraste | 1,2 h |
| Documentação e instalador | 0,3 h |
| **Total** | **~7–11 h** |

Tokens estimados: **120–200 mil de saída**, **3–5 milhões processados**.

## Recomendação e ordem

1. Implementar **A** agora. Entrega o pedido (abrir/visualizar/trazer para frente por um
   ícone fixado) com risco baixo e sem tocar na camada de janelas já frágil.
2. Só considerar **B** depois de usar A, e apenas se faltar o indicador de "aberto" ou a
   miniatura. Se sim, B reaproveita a ação e o `Id` de A: nada do trabalho de A se perde.

## Critérios de aceite (valem para A e B)

- Com o app rodando, clicar no ícone fixado abre/traz o grupo certo em menos de 1 s.
- Com o app fechado, o mesmo clique sobe o app e mostra o grupo certo.
- Grupo fechado abre; grupo recolhido abre a folha; grupo em painel só vem para frente.
- Renomear o grupo não quebra o atalho; apagar o grupo mostra um aviso, sem erro.
- A "Ordem das janelas" escolhida pelo usuário volta ao normal depois do pulso.
- Config antiga (sem `Id`) abre sem erro e ganha os `Id` na próxima gravação.
- Verificado **abrindo o app de verdade** (regra do projeto), não só por compilar.

## Como os custos foram estimados

Comparação com o trabalho já feito neste repositório: uma mudança de escopo médio em
`DesktopGroupWindow.cs` com compilar, abrir o app e capturar tela costuma ficar entre
1 e 2 horas; uma mudança que cria janela nova com regressão de ordem-z/DPI ficou entre
6 e 10. Os tokens seguem o mesmo raciocínio: a saída é o código e a documentação
escritos; o total processado é dominado por reler arquivos grandes a cada ciclo. São
faixas para planejamento, e o primeiro ciclo real de A serve para recalibrar B.
