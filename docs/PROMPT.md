# O prompt único

> **Regra de manutenção:** este arquivo tem que continuar sendo "o prompt que,
> dado a um agente contra um checkout limpo do commit anterior a todo este
> trabalho, produziria o estado atual do repositório". Toda mudança de
> comportamento precisa de uma frase nova aqui (ou uma frase existente
> corrigida), no mesmo commit que muda o código. Atualize também
> [`OVERVIEW.md`](OVERVIEW.md).
>
> Está em português e no tom em que o pedido real foi feito, de propósito:
> é o registro do que foi pedido, não uma tradução de especificação técnica.

---

Tenho um organizador de área de trabalho para Windows em WPF (.NET, projetos
`EasyWinMenu.Core` sem UI + `EasyWinMenu.App`). Ele já tem: grupos de
atalhos como janelas soltas na área de trabalho, com ícones arrastáveis,
subpastas ao estilo Explorer, temas por grupo, localização em 8 idiomas, um
menu na bandeja com hotkey global, e um hook de mouse de baixo nível
(`WH_MOUSE_LL`) que intercepta o clique-direito vazio da área de trabalho
para mostrar um menu próprio.

**Primeiro, uma correção urgente:** esse hook está travando o Windows
inteiro esporadicamente. Ele faz uma chamada `SendMessage` bloqueante para o
Explorer *dentro do próprio callback do hook* — um callback de hook de baixo
nível tem um prazo rígido do sistema (~300ms) antes do Windows simplesmente
tirar o hook da cadeia, e uma chamada síncrona entre processos ali prende o
mouse inteiro até o Explorer responder. Remova esse hook por completo. As
duas ações que ele oferecia (recolher todos os grupos, reunir todos no
centro) devem continuar existindo, só que no menu da bandeja.

## 1. Visual: uma repaginada completa

Quero que este app e o **IGCParam** (outro projeto meu, WPF também) pareçam
da mesma família. Porte o sistema visual do IGCParam para cá:

- Mesma paleta clara e escura (fundo, superfície, superfície-2/3, bordas,
  texto principal/secundário/apagado, cor de destaque e sua variante de
  hover, tom suave da cor de destaque, cor de perigo).
- Mesmas métricas de controle: comandos com 28px de altura, campos com 27px,
  cantos de 4px (6px em painéis maiores), anel de foco de 2px na cor de
  destaque suave.
- Troque todo ícone do `Segoe MDL2 Assets` por um conjunto próprio de ícones
  de linha, desenhados numa grade 24×24, traço de 1.7, pontas e junções
  arredondadas — o mesmo peso visual do IGCParam. Cubra pelo menos: novo,
  duplicar, renomear, excluir, pasta, arquivo, abrir externamente,
  cima/baixo/voltar, chevrons, marca de seleção, cadeado, grade, ordenar,
  tamanho de ícone, mover, reunir, cor, imagem, opacidade, selo, os dois
  modos de exibição de grupo, mais/reticências, pasta aberta, escudo
  (administrador), copiar, engrenagem, energia, seta de início.
- Siga as convenções de menu do Windows: toda linha reserva a coluna do
  ícone mesmo quando não tem um (para os textos ficarem alinhados verticalmente);
  um comando que liga/desliga algo mostra uma marca de seleção na coluna do
  ícone quando está ligado, nunca um caractere dentro do próprio texto;
  reticências (`...`) só em comandos que abrem uma caixa pedindo mais
  informação antes de agir — nunca em ações diretas, nem em ações que já
  pedem confirmação por conta própria.
- O diálogo simples de "digite um nome" tinha uma altura fixa que cortava os
  botões; troque para `SizeToContent="Height"` e remova a margem duplicada
  do conteúdo (o shell da janela já tem a dele).
- Rode um menu de contexto (o de um item, o de um grupo, o da bandeja, o da
  área de trabalho vazia) e confira: submenus abrem com fundo sólido — se
  aparecerem transparentes, é porque o item que abre o submenu não recebeu
  os mesmos brushes de fundo/borda que o menu principal (o `Popup` do
  submenu herda do dono, não do tema global).
- Um `ComboBox` cuja lista usa objetos anônimos com uma propriedade `Label`
  precisa de um `ItemTemplate` explícito — `DisplayMemberPath` sozinho não
  alimenta a caixa de seleção fechada quando o controle já tem um template
  próprio.

## 2. Grupos com dois jeitos de aparecer

Cada grupo escolhe entre dois modos:

- **Painel** (o que já existe): canvas livre, ícones soltos, redimensionável.
- **Pasta de app**, à moda Android: um ladrilho fechado — placa arredondada
  com gradiente, mosaico 3×3 dos primeiros ícones do grupo, um selo vermelho
  com a contagem total de itens no canto, e o nome do grupo embaixo. Um toque
  nele abre uma **folha centralizada e translúcida** (não em tela cheia —
  isto não é um celular): tamanho = `mínimo(2× a largura/altura do painel,
  60% da largura útil do monitor, 50% da altura útil)`, sempre centralizada
  na área de trabalho (descontando a barra de tarefas). Dentro da folha,
  entrar numa subpasta navega no lugar, sem abrir uma janela nova; Esc sobe
  um nível, e só fecha de verdade quando já está na raiz. Clicar fora da
  folha, ou ela perder o foco, fecha.
- As duas aparências vivem na mesma janela (alternando por visibilidade, não
  reconstruindo), e trocar de uma para a outra **preserva** a geometria
  anterior: voltar de pasta-de-app para painel devolve exatamente a
  largura/altura/posição de antes; voltar de painel para pasta-de-app põe o
  ladrilho no canto superior esquerdo de onde o painel estava.
- Arrastar o ladrilho move o grupo; um clique que mal se moveu conta como
  toque (abre a folha) em vez de arrasto.

## 3. Ícones nítidos

Os ícones extraídos ficavam com halo preto ou branco ao redor. Troque a
extração para vir da lista de imagens do shell (`SHGetImageList`, tamanho
jumbo/256px) em vez de `Icon.ExtractAssociatedIcon` (que só dá 32px), com
esse método como reserva. E troque o cache em disco de `.ico` para **PNG** —
salvar como `.ico` e reler estava achatando o canal alfa, e essa era a causa
real do halo, não a resolução.

## 4. Margem, contraste, sombra

- Ícones dentro de um grupo (painel) mantêm no mínimo **5% de margem** (ou
  8px, o que for maior) dos quatro lados, aplicado ao posicionamento padrão,
  ao "organizar automaticamente", às ordenações e ao limite do arrasto.
- A cor do texto de um ícone é **derivada** da cor de fundo efetiva do grupo
  (contraste calculado por luminância), nunca fixa no tema — um grupo com
  imagem de fundo ou translúcido usa texto branco com uma sombra por baixo
  para continuar legível sobre qualquer coisa.
- Sombra deixa de ser só liga/desliga: adicione raio de desfoque, distância,
  direção (em graus) e opacidade como campos do tema, editáveis na tela de
  edição de tema, aplicados ao painel, ao ladrilho e à folha.

## 5. Convenções de janela que estavam erradas

- O cursor de "mover" (seta de quatro pontas) só deve aparecer **durante** o
  arraste do título, nunca ao simplesmente passar o mouse por cima.
- Só um grupo (ou subpasta) **vazio** pode ser removido — pelo menu e pela
  tecla Delete (numa seleção múltipla, remove o que pode e avisa que o que
  tinha conteúdo foi preservado).
- O título de um grupo precisa do botão padrão do Windows 11 de "ver mais"
  (um "..." que abre o menu do grupo), além do que já existe.
- O clique direito num ícone dentro de um grupo deve oferecer os mesmos
  comandos que o Explorer oferece num arquivo: executar como administrador,
  abrir local do arquivo, copiar como caminho, propriedades — e só quando o
  alvo for de fato um arquivo ou pasta no disco (não um comando nem uma URL).
- Crie também uma opção **"Menu padrão"** no menu de um ícone, que abre o
  `IContextMenu` de verdade do Windows para aquele arquivo — com extensões de
  terceiros, submenus (7-Zip etc.) funcionando de verdade. Isso é
  necessariamente desenhado pelo Windows, não pelo tema do app; as duas
  coisas convivem, o menu padrão não substitui o temático.

## 6. Comandos de grupo para grupo

No menu de um grupo, adicione:

- **Duplicar grupo** — cria uma cópia com "(cópia)" no nome, deslocada
  ~28px para não cair exatamente em cima da original.
- **Aplicar esta aparência a todos os grupos** — copia tema, opacidades,
  imagem de fundo e selo do grupo atual para todos os outros.
- **Usar esta aparência como padrão** — vira o tema que grupos novos usam a
  partir de agora.
- **Usar papel de parede do Windows como fundo**, com duas variantes: só
  este grupo, ou todos. Leia o papel de parede atual do Windows
  (`SPI_GETDESKWALLPAPER`, com reserva no `TranscodedWallpaper` para quando
  o Windows está em modo apresentação/slideshow).

## 7. Múltiplos monitores

- Quando um monitor desaparece, todo grupo que estava nele precisa ser levado
  para o monitor que sobrou, **mantendo o lugar relativo** (canto superior
  direito vira canto superior direito no outro). Essa posição de resgate não
  deve ser salva como definitiva — o grupo guarda a posição original como
  "casa" e volta sozinho quando o monitor volta a existir; um arrasto manual
  durante o resgate vira a nova casa normalmente.
- **Win+Shift+seta esquerda/direita** move o grupo em foco para o monitor
  vizinho, dando a volta nas pontas — a mesma convenção que o próprio Windows
  usa para janelas comuns.
- Escreva a geometria (achar o dono de um retângulo, mapear proporcionalmente
  entre dois retângulos, evitar empilhar duas coisas resgatadas no mesmo
  ponto) como funções puras, sem depender de WPF, para dar para testar sem
  precisar de dois monitores físicos.

## 8. Identidade

- Preciso de um ícone bonito para o app que lembre as palavras "Easy Win".
  Gere-o em código (não é uma imagem editada à mão): quatro ladrilhos numa
  grade 2×2 (janelas + os grupos que o app organiza), um deles destacado com
  uma estrelinha. Desenhe cada resolução de 16 a 256px **separadamente**, não
  reduza de uma maior — reduzir vira uma mancha cinza no tamanho de bandeja,
  que é exatamente onde o ícone mais aparece. Aplique-o ao executável, à
  bandeja e à barra de tarefas.
- Em nenhuma janela, diálogo ou caixa de mensagem deve aparecer o nome
  "EasyWinMenu" — o nome visível é **"Easy Windows & Menus"**, em todos os
  idiomas suportados. A pasta de dados do usuário e o nome interno do
  processo continuam como estão, para não invalidar configurações já
  salvas.

## 9. Atalho para trazer os grupos de volta

"Mostrar área de trabalho" (Win+D) e Win+M escondem os grupos e eles não
voltam sozinhos. **Antes de implementar, meça o que realmente acontece** —
não assuma que é uma minimização: uma janela sem botão na barra de tarefas
(o que os grupos são, de propósito) não é de fato minimizada por essas
teclas; o Windows só traz a janela da própria área de trabalho para o topo
da ordem Z e deixa os grupos enterrados atrás dela, ainda visíveis e no
lugar certo. A correção certa não checa o estado de minimizado: alterne a
propriedade `Topmost` da janela (true e depois false) para forçá-la de volta
ao topo da sua faixa de ordem Z, sem roubar o foco. Registre um **segundo**
atalho global (independente do que já existe para abrir o menu), padrão
**Ctrl+Alt+D**, configurável na tela de Configurações do mesmo jeito que o
atalho existente, para acionar essa correção manualmente.

## 10. A pergunta que decide a arquitetura: ancorar os grupos na área de trabalho?

Antes de qualquer coisa: **investigue, não implemente às cegas.** A ideia
óbvia para os grupos nunca ficarem atrás de outra janela nem sumirem no
Win+D é reparentá-los como filhos da janela real da área de trabalho
(`SHELLDLL_DefView`, sob o `Progman`) — a mesma técnica que ferramentas afins
usam. Construa uma sonda em C# puro (não em PowerShell — a marshalling do
PowerShell não converte `$null` para `NULL` num parâmetro de string em
P/Invoke, o que já vai gerar diagnóstico errado) para descobrir, sem supor:
qual janela realmente é a superfície da área de trabalho nesta versão do
Windows, se dá para reparentar uma janela ali, se ela pinta, se recebe
clique, e se sobrevive ao Win+D. Depois, com uma janela WPF real (não só
Win32 puro), confirme se a transparência por pixel (`AllowsTransparency`)
sobrevive ao mesmo reparentamento — comparando lado a lado com a mesma
janela sem reparentar.

Se a transparência não sobreviver (o que de fato aconteceu: uma janela WPF
reparentada vira opaca, mesmo mantendo a flag interna de janela em camadas),
**não ancore os grupos**. O produto usa transparência em várias partes —
opacidade por grupo, papel de parede visto através da área, a folha
translúcida da pasta de app — e vale mais manter isso do que ganhar a
imunidade ao "Mostrar área de trabalho", que já foi resolvida de outro jeito
no item 9. Documente a decisão e o porquê, para não ser retestada às cegas
de novo.

## 11. Documentação e versionamento

Mantenha dois documentos sempre sincronizados com o código, atualizados no
mesmo commit de qualquer mudança:

- Uma visão geral do produto, da arquitetura e das decisões tomadas (por que
  os grupos não são ancorados, por que o menu do shell convive com o
  temático, etc.), incluindo o que ainda não foi verificado de verdade
  (ex.: a lógica de múltiplos monitores só foi testada por simulação nesta
  máquina, que só tem um monitor).
- Este prompt único — o que você está lendo — mantido como o pedido
  equivalente a tudo isso, para que alguém possa reconstruir o produto do
  zero a partir dele.

Adote um único arquivo `VERSION` na raiz do repositório (formato `a.b.c.d`,
a mesma convenção já usada nos projetos Delphi), lido por um
`Directory.Build.props` que aplica a versão a todo projeto do produto sem
tocar em cada `.csproj` individualmente. Ferramentas de desenvolvimento
soltas (geradores de ícone, sondas de diagnóstico) ficam de fora do
versionamento — não fazem parte do que é entregue.
