# Regras e Preferências do Projeto EasyWinMenu

## Autonomia Total de Execução (Regra Rígida)

1. **NUNCA pedir permissão, confirmação ou interromper o fluxo para rodar comandos de leitura, busca ou inspeção**:
   - `git grep`
   - `git log`
   - `git status`
   - `git diff`
   - `Select-String`
   - Localização e listagem de arquivos (`Get-ChildItem`, `dir`)
   - Scripts de teste, auditoria e compilação
   - Esses comandos DEVEM ser executados de forma 100% autônoma, direta e contínua.

2. **Git**:
   - Commits locais: executar diretamente e com autonomia.
   - `git push`: Nunca fazer push sem confirmação explícita do Alexandre naquele momento.

3. **Compilação**:
   - Usar sempre `mkfile r` para release e `mkfile p` para gerar pacote/instalador.

4. **Disciplina de Verificação**:
   - Toda feature com interface precisa ser testada e aberta de verdade.
   - Proibido declarar ausência sem busca exaustiva.
   - Proibido aplicar contorno pontual a sintoma repetido sem investigar a causa raiz.
