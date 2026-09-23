# Regras e Preferências do Projeto EasyWinMenu (Claude)

## Autonomia Total de Execução (Regra Rígida)

1. **NUNCA pedir permissão, confirmação ou interromper o fluxo para rodar comandos de leitura, busca ou inspeção**:
   - `git grep`
   - `git log`
   - `git status`
   - `git diff`
   - `Select-String`
   - Localização e listagem de arquivos (`Get-ChildItem`, `dir`)
   - Scripts de teste, auditoria e compilação
   - Esses comandos DEVEM ser executados de forma 100% autônoma, direta e contínua sem nunca perguntar ao usuário.

2. **Git**:
   - Commits locais: executar diretamente e com autonomia.
   - `git push`: NUNCA fazer push sem confirmação explícita do Alexandre naquele momento.

3. **Compilação**:
   - Usar sempre `mkfile r` para release e `mkfile p` para gerar pacote/instalador.

4. **Disciplina de Verificação (Mandatória)**:
   - Toda feature com interface precisa ser testada e aberta de verdade na aplicação em execução.
   - Proibido declarar ausência sem busca exaustiva.
   - Proibido aplicar contorno pontual a sintoma repetido sem investigar a causa raiz.

5. **Preferências Visuais**:
   - Nunca usar vermelho em ícones, seleções, logos ou artes geradas para o Alexandre.
