# Regras e Preferências do Projeto EasyWinMenu

## Autonomia Total de Execução (Regra Rígida)

1. **NUNCA pedir permissão, confirmação ou interromper o fluxo para comandos de busca, listagem ou leitura**:
   - `Get-ChildItem`, `dir`, `ls`
   - `Select-String`
   - `git grep`, `git log`, `git status`, `git diff`, `git branch`, `git show`
   - Scripts de teste, auditoria e compilação
   - Esses comandos DEVEM ser executados de forma 100% autônoma, direta e contínua sem nunca perguntar ao Alexandre.

2. **Git**:
   - NUNCA perguntar nada sobre comandos git (leitura, checkout, branch, add, commits locais de trabalho). Executar diretamente com autonomia total.
   - A única restrição é `git push`: NUNCA fazer push sem confirmação explícita do Alexandre naquele momento.

3. **Compilação**:
   - Usar sempre `mkfile r` para release e `mkfile p` para gerar pacote/instalador.

4. **Disciplina de Verificação**:
   - Toda feature com interface precisa ser testada e aberta de verdade.
   - Proibido declarar ausência sem busca exaustiva.
   - Proibido aplicar contorno pontual a sintoma repetido sem investigar a causa raiz.

## 6. Verificação: o que vale como prova neste repositório

Existem duas camadas, e as duas rodam antes de qualquer publicação:

```
dotnet test tests/EasyWinMenu.Tests          # 32 assercoes, ~1 s
dotnet build -c Release tests/GroupProbe     # sonda de caracterizacao
```

A sonda sobe o app de verdade e grava um relatório JSON com a geometria das janelas, o
SHA-256 da captura de cada grupo e o menu de contexto inteiro lido por UI Automation. Uso:

```
tests/GroupProbe/bin/Release/net10.0-windows/GroupProbe.exe ^
  --exe <EasyWinMenu.App.exe> --fixture tests/GroupProbe/fixtures/probe-config.json ^
  --out <pasta> --label <nome>
python tests/compare_reports.py tests/baseline/report-master.json <pasta>/report-<nome>.json
```

**Regras que valem para qualquer agente que mexer aqui, inclusive a agy:**

1. **Toque em comportamento de grupo ⇒ rode a sonda e cole a saída do comparador.** "Compila"
   não é prova. Divergência inesperada nos 316 campos é reprovação, não detalhe.
2. **Valide o oráculo antes de usá-lo.** Antes de comparar dois builds, rode a sonda duas
   vezes contra o *mesmo* build e confirme `IDENTICO`. Um oráculo que oscila reprova ou aprova
   por sorte. Já aconteceu aqui: a primeira sonda dava 283 campos numa execução e 316 na
   seguinte, porque esperava submenu por `sleep` fixo em vez do estado reportado pelo menu.
3. **Nunca mexa no `%AppData%\EasyWinMenu\config.json` sem backup conferido por hash**, e
   confira a restauração no fim. A sonda já faz isso e **aborta antes de abrir qualquer coisa**
   se o backup não bater — copie esse padrão, não o improvise.
4. **Sonda de Win32 se escreve em C#, não em PowerShell.** O marshalling do PowerShell
   transforma `$null` em string vazia num parâmetro `string` de P/Invoke e já produziu duas
   conclusões erradas neste projeto.
5. **Auditoria por *grep* de código-fonte não conta como teste.** Os seis scripts que faziam
   isso foram removidos justamente porque passavam enquanto a feature estava quebrada. Meça o
   que o app faz, não a aparência do código que deveria fazer.
6. **Antes de declarar uma fase concluída, diga o que você mediu e com que comando.** Uma
   afirmação técnica sem o comando que a sustenta é tratada aqui como não verificada.
