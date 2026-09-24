# Regras e Preferências do Projeto EasyWinMenu (Claude)

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

4. **Disciplina de Verificação (Mandatória)**:
   - Toda feature com interface precisa ser testada e aberta de verdade na aplicação em execução.
   - Proibido declarar ausência sem busca exaustiva.
   - Proibido aplicar contorno pontual a sintoma repetido sem investigar a causa raiz.

5. **Preferências Visuais**:
   - Nunca usar vermelho em ícones, seleções, logos ou artes geradas para o Alexandre.
