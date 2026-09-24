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

## 11. Atalhos de arquivo e redimensionar por qualquer borda

Dentro de um grupo (painel), os atalhos padrão do Windows para copiar,
recortar, colar e selecionar tudo não funcionavam — só o clique com Ctrl
alternava a seleção de um ícone por vez, e não havia nenhum jeito por
teclado de agir sobre a seleção. Implemente:

- **Ctrl+A** seleciona todos os ícones do grupo (subpastas e itens
  fixados).
- **Ctrl+C** e **Ctrl+X** colocam os arquivos por trás dos ícones
  selecionados na área de transferência de verdade do Windows (não uma área
  de transferência interna do app), para colar em qualquer lugar — inclusive
  no Explorer. Recortar também marca o `Preferred DropEffect` como "mover"
  (a mesma convenção que o Explorer usa para desenhar o ícone apagado) e
  desafixa os itens copiados deste grupo. Subpastas do grupo são ignoradas:
  são um agrupamento do próprio app, sem uma pasta real em disco por trás
  para copiar.
- **Ctrl+V** aceita qualquer arquivo que esteja na área de transferência
  (copiado do Explorer ou de outro grupo) e o fixa como um novo ícone, pelo
  mesmo caminho que soltar um arquivo arrastado já usava.

Também só dava para redimensionar o grupo puxando um único ícone no canto
inferior direito. Troque por oito zonas de arraste invisíveis cobrindo os
quatro lados e os quatro cantos do painel — puxar qualquer uma redimensiona
a partir dali, mantendo a borda oposta fixa no lugar, do jeito que qualquer
janela do Windows já se comporta.

## 12. Um lote de acabamentos e recursos do painel

- **Organizar ícones automaticamente** deixa de ser um comando de "fazer uma vez"
  (e, como um grupo só guarda atalhos, **não há mais "ordenar por nome/tipo"**: ligado,
  a ordem é sempre por nome do atalho): a escolha fica salva por grupo
  (`MenuCategory.IconArrangement`) e é **reaplicada sozinha** toda vez que o
  conjunto de ícones muda — soltar um arquivo, colar, apagar, renomear —
  não só no instante em que o item de menu foi clicado. Um grupo em posição
  livre (o padrão de sempre) continua livre; nada disso se aplica a ele.
- Troque o atalho global padrão de restaurar grupos de **Ctrl+Alt+D** para
  **Win+Ctrl+Alt+D** (o usuário já usa Ctrl+Alt+D para outra coisa).
- No Ctrl+X de um ícone dentro de um grupo: **não remova na hora.** O ícone
  fica meio-opaco, do jeito que o Explorer marca um recorte pendente, e só
  sai do grupo quando algo de fato consome o "colar" — um Ctrl+V em outro
  grupo deste mesmo app finaliza a remoção ali mesmo. Para um paste externo
  de verdade (arrastar para o Explorer, por exemplo), o Windows não avisa
  ninguém quando isso termina; a única saída é conferir periodicamente se o
  arquivo original ainda existe no caminho de onde foi recortado, e remover o
  ícone quando ele não existir mais — aceite o atraso de alguns segundos como
  o preço de não precisar de um hook do shell.
- A borda de um grupo deixa de vir do tema: é **derivada da cor de fundo
  efetiva**, 10% mais clara — ou 10% mais escura quando o fundo já é branco
  ou perto disso, onde clarear não mudaria nada visível.
- Passar o mouse sobre um ícone dentro do painel dá um retorno visual "como
  a web dá": um leve aumento de escala e um tingimento do fundo, animados,
  em vez de mudar de estado sem transição nenhuma. Não compete com o
  destaque, mais forte, de um ícone selecionado.
- O ladrilho fechado (estilo App Folder) nasce **40% maior** quando é o que
  fica na área de trabalho — do jeito que ele já existe hoje é pequeno demais
  perto de ícones reais do Windows ao lado.
- **Ctrl+F**, só no modo painel (o modo App Folder não tem o que buscar):
  abre uma barra de busca sob o título do grupo. Enquanto digita: destaca o
  trecho digitado dentro do nome de cada ícone que bate, mostra um contador
  de quantos bateram, e alimenta uma lista suspensa dos resultados navegável
  com seta para cima/para baixo. Enter num resultado da lista tem exatamente
  o mesmo efeito que dar duplo clique nele, e fecha a busca. Esc também
  fecha. O texto buscado só é lembrado para a próxima vez que abrir com
  Ctrl+F nesses dois momentos — Enter num resultado, ou Esc — nunca por
  simplesmente clicar fora ou perder o foco.
- No menu de contexto **real do Windows** (clique direito na área de
  trabalho vazia, fora do app), adicione um submenu com: novo grupo,
  colapsar/expandir todos os grupos, trocar todos para o estilo App Folder,
  trocar todos para o estilo painel. Isso não é o menu temático do app — o
  app não tem como interceptar o clique na área de trabalho real (ver a
  decisão de não ancorar os grupos, seção 10) — é um registro de verdade sob
  `HKCU\...\DesktopBackground\Shell`, o mesmo truque que outras ferramentas
  usam para colocar um verbo estático no menu do Explorer, sem shell
  extension e sem hook. Cada verbo apenas relança o próprio executável com
  um argumento; isso precisa que o app saiba se já existe uma instância rodando
  — sem esse controle, cada clique nesses itens abriria um `EasyWinMenu.App.exe`
  novo, com um segundo ícone de bandeja e os grupos duplicados por cima dos
  já abertos. A instância nova, ao perceber que já existe uma rodando, manda
  o comando para ela (um named pipe é suficiente) e sai imediatamente sem
  desenhar nada; instâncias abertas fora desse fluxo — um duplo clique comum
  no `.exe`, por exemplo — não devem ficar presas nessa checagem além do
  necessário para fechar sozinhas.

## 13. Instalador e um link de download que funcione fora daqui

Preciso de duas coisas: um instalador de verdade (assistente, atalho no menu
iniciar, entrada em Adicionar/Remover Programas, desinstalador — não só o
`.exe` solto) e um jeito de baixar isso por URL de fora da minha rede.

- Gere o instalador com **Inno Setup**: publique o app **self-contained,
  single-file, win-x64** (não framework-dependent — o instalador precisa
  funcionar numa máquina sem o runtime do .NET instalado, senão não faz
  sentido ele rodar fora daqui) e depois compile um script `.iss` que
  instala por usuário (sem pedir elevação), com atalho no menu iniciar,
  ícone de área de trabalho opcional, e um desinstalador que **não** apaga a
  configuração do usuário em `%AppData%\EasyWinMenu`.
- Isso vira o comportamento de `mkfile package` para este projeto — coloque
  um `tools\build_installer.ps1` ao lado do `.csproj`, do mesmo jeito que o
  lado Flutter do `mkfile` já usa `tools\build_apk.py`: quando esse script
  existe, ele é a fonte da verdade para "empacotar", não `dotnet publish` cru.
  Edite o `mkfile.ps1` para reconhecer essa convenção do lado .NET também,
  em vez de reimplementar o empacotamento como outro script solto sem relação
  com o `mkfile`.
- O link de download é uma **release do GitHub** neste repositório (já é
  público), com o instalador anexado à tag da versão — estável, sem custo,
  funciona de fora desta rede sem eu precisar manter servidor nenhum.

## 14. A organização dos ícones precisa reagir, não só "rodar uma vez"

Os ícones de um grupo em modo grade/nome/tipo paravam de bater com o layout
depois de duas ações que deveriam ter mexido neles: redimensionar o grupo
(a grade continuava com o número antigo de colunas) e mudar o tamanho dos
ícones pelo menu ou pelo Ctrl+roda do mouse (a grade não recalculava quantos
cabem por linha, então em zoom alto os últimos ícones da linha saíam do
painel). O grupo inteiro precisa ficar responsivo: qualquer coisa que mude a
ordenação, o tamanho do grupo ou o tamanho dos ícones tem que refletir na
hora, não só as ações que já disparavam isso (soltar, colar, apagar,
renomear).

Também: marcar vários ícones e mandar remover pelo menu de contexto de um
deles só removia aquele um — o menu não sabia da seleção múltipla. A tecla
Delete já fazia isso certo; o menu de contexto (tanto de um item quanto de
uma pasta) precisa se comportar do mesmo jeito que o Explorer: clicar em
"remover" com vários itens marcados age sobre todos eles, não só o que está
debaixo do mouse.

## 15. O ladrilho de App Folder parou de responder a clique

O ladrilho fechado (estilo App Folder) na área de trabalho ficou 40% maior
numa sessão anterior usando um `LayoutTransform` no controle que o hospeda.
Isso quebrou a interação: dava para ver o ladrilho, mas clicar nele não
abria mais a pasta. Troque a abordagem — em vez de desenhar no tamanho
normal e esticar depois, faça o `AppFolderTile` nascer já nas medidas
finais (um parâmetro de escala que multiplica cada medida antes de montar o
visual). Mais seguro por construção: não existe uma camada de transform
separada para o hit-test poder discordar do que foi desenhado.

## 16. Busca por digitação e clique único quebrando a seleção

Implemente a busca por digitação de dentro de um grupo do jeito que o
Windows Explorer já faz dentro de qualquer pasta: sem apertar atalho
nenhum, só começar a digitar já pula a seleção para o primeiro ícone cujo
nome comece com o que foi digitado, acumulando letras dentro de cerca de 1
segundo uma da outra e recomeçando depois de uma pausa. Isso é diferente do
Ctrl+F que já existe (aquele abre uma caixa com contador e lista) — vale
tanto no painel livre quanto dentro da folha do estilo App Folder.

No estilo App Folder, um clique único num ícone não pode mais abri-lo na
hora — isso tira a chance de só selecionar um ícone para fazer outra coisa
com ele (por exemplo, escolher antes de decidir a ação). Clique único
seleciona; abrir é duplo clique ou Enter, do mesmo jeito que qualquer
pasta de ícones do Windows já se comporta.

## 17. Mover objetos entre grupos, e paridade completa do App Folder

Mover um ícone de um grupo para outro não funcionava, e uma pasta movida
assim acabava sendo interpretada como um grupo novo e independente da área
de trabalho, em vez de uma subpasta pertencente ao grupo de destino.
Implemente arrastar de verdade: soltar um ícone (arquivo **ou** subpasta)
fora do próprio grupo, sobre outro grupo aberto, precisa **mover** o objeto
— tirar da lista do grupo de origem e colocar na lista do grupo de destino
(itens fixados ou subpastas, conforme o tipo), nunca na configuração de
nível superior. Uma subpasta movida assim continua subpasta.

Outros acertos no mesmo lote:

- **Configurações globais a partir de qualquer grupo**: o menu de qualquer
  grupo (painel ou App Folder) precisa ter uma entrada que abra a tela de
  Configurações do app, não só a bandeja.
- **Paridade total entre painel e App Folder**: tudo que dá pra fazer no
  painel (organizar automaticamente, ordenar, novo arquivo/pasta, tamanho
  do ícone, etc.) precisa estar disponível também no estilo App Folder —
  seja no menu do próprio ladrilho fechado, seja dentro da folha que abre
  ao clicar nele (onde vale também seleção múltipla, Delete, F2, Ctrl+A e
  menu de contexto por ícone, os mesmos que o painel já tinha).
- **Estado marcado nos menus de ordenação**: seguindo o padrão do Windows,
  a opção de organização atualmente ativa (grade automática, por nome, por
  tipo) precisa aparecer com visto/marcada no menu, não só executar a ação.
  Como o menu é reconstruído a cada clique direito, isso nunca fica
  desatualizado.
- **Legenda do grupo com um resumo das configurações**: o título de um
  grupo ganha uma dica (tooltip) resumindo o essencial — estilo atual,
  organização, tamanho do ícone, opacidade, selo — sem precisar abrir o
  menu para saber o que está configurado.
- **Idioma dos itens no menu de contexto real do Windows**: os rótulos que
  aparecem no clique direito de verdade da área de trabalho (o registro em
  `DesktopBackground\Shell`) seguem o **idioma do Windows**, não o idioma
  configurado dentro do app — esse menu é lido pelo Explorer mesmo com o
  app fechado, então o idioma escolhido dentro do app não tem influência
  nenhuma sobre ele. Dentro do app (bandeja, menus dos grupos), continua
  valendo o idioma configurado nas Configurações. Esse menu real ganha
  também uma entrada "Configurações..." (mesmo destino do item 3 acima).

## 18. O ladrilho de App Folder "sumia" ao arrastar

Arrastar o ladrilho fechado do estilo App Folder às vezes fazia a janela do
grupo desaparecer para sempre — só voltava trocando **todos** os grupos de
volta para o estilo painel pelo menu de contexto real do Windows (o que
força uma reconstrução completa da janela, com `Width`/`Height` explícitos
de novo). Não bastava olhar o código e desconfiar: a suspeita óbvia (as
zonas de redimensionar, ou o TileTransform da sessão anterior) não batia
com o sintoma. Só reproduziu na prática montando um teste automatizado com
UI Automation (`System.Windows.Automation`, não `GetWindowRect` — em uma
máquina com DPI por monitor, `GetWindowRect` chamado de um processo sem
DPI-awareness devolve coordenadas "virtualizadas" que não bate com a tela
real, e isso por pouco não desviou a investigação inteira) simulando um
arrasto de verdade e comparando a posição da janela antes/depois. A janela
apareceu exatamente em `Int16.MinValue` num dos eixos — a pista que
resolveu: era um problema de acúmulo de posição via duas chamadas de
`PointToScreen` no mesmo evento de `MouseMove`, a segunda já depois da
primeira ter alterado `Left`/`Top` da própria janela — em DPI por monitor,
a segunda leitura podia vir escalada por um fator diferente da primeira. A
correção trocou a lógica à mão por `DragMove()` (a mesma primitiva do WPF
que já movia a janela pelo cabeçalho, robusta a monitores/DPI por
delegar o arrasto de verdade para o Windows). Ao investigar um bug de
posição/tamanho de janela que só aparece "às vezes" ou "em algum lugar
esquisito", desconfie primeiro de contas de coordenada feitas à mão
(`PointToScreen`/`Left +=`) em vez de uma primitiva nativa (`DragMove`,
`DragResize`) — e meça com uma ferramenta que respeita DPI por monitor.

## 19. Documentação e versionamento

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

## 20. Arrastar entre grupos: o ícone sumia e soltava em lugar errado

A experiência de arrastar um ícone de um grupo para outro estava ruim: o
objeto sumia durante o arrasto, e ao soltar parecia não ter feito nada mas
na verdade movia — só que para um lugar diferente de onde foi largado.

Duas causas distintas, achadas por leitura de código (comparando com o
caminho que já funcionava corretamente — arrastar *dentro* do mesmo
grupo), não por reprodução visual:

1. **Sumia**: o arrasto movia o ícone só dentro do `Canvas` da própria
   janela do grupo de origem (`Canvas.SetLeft/Top`). Nada renderiza fora
   dos limites de uma janela WPF — então, assim que o ícone cruzava a
   borda da janela (o que acontece o tempo todo ao arrastar para OUTRO
   grupo, que é uma janela diferente), ele desaparecia, mesmo a captura do
   mouse continuando ativa e o resto do gesto funcionando por trás dos
   panos.
2. **Soltava em lugar errado**: a posição final usada era o cursor cru
   (`PointToScreen(e.GetPosition(this))`), que descarta o deslocamento
   entre onde o usuário pegou o ícone e o canto dele — deslocamento que o
   arrasto dentro do mesmo grupo preservava (`tileStart + delta`), mas que
   o caminho entre grupos ignorava, recalculando a posição a partir do zero
   a cada solta.

Corrigido com uma janela-fantasma real (`DragGhostWindow`, um novo arquivo
em `Desktop/`) — um `VisualBrush` do próprio tile, sem clonar a árvore
visual — posicionada em coordenadas de tela, que acompanha o cursor e
atravessa livremente os limites de qualquer janela, do mesmo jeito que o
Explorer mostra um ícone "flutuando" durante um arrasto. A posição *dela*
(não a do cursor) virou a fonte de verdade tanto para achar qual grupo está
embaixo quanto para a posição final no destino, o que resolve as duas
causas de uma vez.

Repetindo a lição do item 18 (o ladrilho de App Folder que sumia por causa
de duas chamadas de `PointToScreen` misturando DPI de monitores
diferentes): `PointToScreen` só é chamado sobre a janela de origem, que
nunca se move durante o gesto — nunca lido de volta a partir da própria
janela-fantasma, que é quem se move. Ao introduzir uma coordenada de tela
nova numa correção de arrasto, desconfie de qualquer leitura de posição
feita a partir de algo que você acabou de mover.

**Não verificado interativamente.** Uma tentativa de montar um teste
automatizado (lançar o app com uma configuração de dois grupos conhecidos,
localizar os tiles via `System.Windows.Automation` e simular o arrasto com
`SetCursorPos`/`mouse_event`) não conseguiu clicar nos ícones de forma
confiável: a janela usa `AllowsTransparency=true`, que faz o Windows testar
cliques pixel a pixel — um clique num pixel transparente atravessa para a
janela debaixo em vez de acertar o tile — e os elementos internos
(`StackPanel`/`Canvas` construídos à mão, sem `AutomationPeer` customizado)
não aparecem de forma utilizável nem na árvore "Raw" da UI Automation. A
correção ficou validada por leitura de código e por compilação, não por
reprodução visual — diferente do item 18, onde a reprodução automatizada
foi possível. Fica pendente a confirmação manual de alguém rodando o app de
verdade.

## 21. Seleção estilo Explorer, abrir/fechar grupos, ordem das janelas e organização só por nome

Depois de tudo acima, um lote de pedidos deixou o painel com o comportamento do Explorer
e simplificou a organização. (A versão do zero, em um passo, está em
[`PROMPT_GERACAO_UNICA.md`](PROMPT_GERACAO_UNICA.md).)

- **Um grupo só guarda atalhos**, então **não faz sentido ordenar por nome ou por tipo**.
  Removi o submenu "Ordenar por" e a ordenação por tipo. O menu do grupo tem um único item
  marcável, **"Organizar ícones automaticamente"**; ligado, a ordem é **sempre por nome do
  atalho** (sem diferenciar maiúsculas de minúsculas), em grade, reaplicada sozinha ao
  soltar, colar, apagar, renomear, redimensionar e mudar o zoom. Configurações salvas com os
  valores antigos `Grid` ou `ByType` continuam abrindo e passam a valer como "por nome".
- **Seleção como no Explorer**: clique, Ctrl+clique, Shift+clique (intervalo), retângulo de
  seleção arrastando no fundo, Ctrl+A. **Setas, Home e End** navegam em 2D pela grade (Shift
  estende); **Enter** abre; **F2** renomeia; **Delete** remove; **F5** reaplica a organização;
  **Esc** limpa a seleção. Ctrl+C, Ctrl+X e Ctrl+V usam a área de transferência real.
- **Arrastar vários itens selecionados** de uma vez, também para outro grupo, com a
  janela-fantasma e uma animação de batimento no ícone pego. Ao soltar num grupo com a
  organização ligada, ele reordena por nome.
- **Novo atalho…** no menu do grupo, que também abre pela janela externa (fora do painel).
- **Fechar grupo** no menu do grupo (`MenuCategory.IsClosed`), e no menu da bandeja e no
  clique direito da área de trabalho: **Abrir todos os grupos**, **Fechar todos os grupos** e
  um item marcável por grupo.
- **Ordem das janelas** (submenu no grupo): trazer todos para frente, enviar os outros para
  trás, enviar todos para trás — para escolher quem cobre quem na área de trabalho.
- **Win+Shift+←/→** leva o grupo ao monitor vizinho.
- **Atalho de grupo na barra de tarefas**: item **"Criar atalho na barra de tarefas"** no
  menu do grupo. Cada grupo ganha um `Id` estável (GUID, gravado na config; duplicar gera outro).
  O item cria um `.lnk` em `%AppData%\EasyWinMenu\GroupShortcuts` que abre o próprio app com
  `--desktop-action=focus-group:<id>` e mostra o arquivo no Explorer para o usuário arrastar à
  barra. Clicar no atalho abre o grupo se estiver fechado, expande se estiver recolhido, traz
  para frente, ativa e faz um pulso curto de opacidade; com o app fechado, sobe e faz o mesmo;
  com um id que não existe mais, mostra o balão "Grupo não encontrado". Uma pasta
  `...\User Pinned\TaskBar` sozinha **não** fixa (o Explorer usa o valor `Favorites` da chave
  `Taskband`), então o fixar-sozinho ficou para depois — ver
  [`VIABILIDADE_ATALHO_TASKBAR_GRUPO.md`](VIABILIDADE_ATALHO_TASKBAR_GRUPO.md).
- Versão **1.1.1.0**.

## 22. Higienização: o EasyWinMenu passa a ser só os grupos

O repositório carregava três coisas que não são a função de grupos. Separe cada uma **sem
mudar nada no comportamento dos grupos**, e prove que não mudou.

- **QuickStacks** (app WinUI 3 inteiro, 5 projetos) vira repositório próprio, com o histórico
  que tocava a pasta preservado por `git subtree split`. Antes de remover daqui, ele tem que
  compilar sozinho lá.
- **O menu lançador da bandeja** — a lista hierárquica de categorias e atalhos do usuário —
  vira um app próprio chamado **QuickMenu**, com `%AppData%\QuickMenu` separado. Aqui fica
  só o menu como painel de controle dos grupos (novo grupo, abrir/fechar, recolher, reunir,
  iniciar com Windows, configurações, sair).
- **Código morto e ferramentas de scratch**: `DesktopContextMenuBuilder`, `DeskProbe`,
  `DragDropTester`, capturas soltas e seis scripts `.ps1` de auditoria por *grep* de
  código-fonte — que a sonda nova substitui por medição real.

**A prova exigida, nesta ordem:**

1. Um **arsenal de verificação primeiro**, com a master ainda intacta: 32 asserções xUnit e
   uma sonda em C# que sobe o app de verdade e grava geometria, captura PNG por grupo e o
   menu de contexto inteiro lido por UI Automation.
2. **Validar o oráculo antes de confiar nele**: rodar a sonda duas vezes contra o *mesmo*
   build e exigir relatórios idênticos. A primeira versão oscilava entre 283 e 316 campos
   porque esperava os submenus por `sleep` fixo — troque por esperar o estado que o menu
   reporta. Medição que muda sozinha não distingue refatoração de ruído.
3. Comparar cada etapa contra a linha de base e **exigir zero divergência** nos 316 campos.
