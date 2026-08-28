# Roadmap

## Estado estabilizado

- Identidade de itens unificada por `itemId`, com catálogo validável.
- Inventário e caixotes com slots encapsulados e remoções atômicas.
- Placement e coleta de drops sem perda silenciosa de itens.
- Save/load versionado para o estado jogável atual.
- Farming alinhado ao Grid `IsometricZAsY` da cena.
- Testes de Edit Mode para identidade, catálogo e transações de inventário.
- Primeiro atendimento ativo: pedido com prazo, preparo, coleta, entrega, dinheiro e reputação.
- Persistência v2 do pedido e do estado da cozinha.

## Vertical slice ativo escolhido

O modelo escolhido é atendimento ativo. A primeira fatia funcional já cobre:

1. Produzir ingredientes: plantar, cuidar e colher.
2. Cozinhar a sopa de tomate em uma estação com tempo de preparo.
3. Servir um pedido com cliente, prazo e valor definidos.
4. Receber dinheiro e reputação.
5. Salvar pedido e cozinha junto do estado do jogo.

## Próximo marco de produto

1. Substituir as estações de fallback por prefabs e arte definitivos na cena.
2. Representar o cliente fisicamente, com chegada, espera, entrega e saída.
3. Adicionar dois novos pratos e desbloqueio de receitas.
4. Comprar sementes, capacidade e melhorias.
5. Mostrar um resumo ao encerrar o dia.

## Decisões de produto pendentes

- Papel dos NPCs: clientes descartáveis, moradores persistentes ou ambos.
- Condição de progressão: reputação, objetivos narrativos, lucro ou combinação.

Identidade definida: **Sleeping Beaver**, bundle `com.sleepingbeaver.atasteofhome`. CI permanece fora do escopo por enquanto.

## Sequência técnica dos próximos marcos

1. Evoluir `DishDefinition` e o catálogo com novos conteúdos.
2. Implementar o cliente visual sobre os eventos do serviço, sem acoplá-lo ao inventário.
3. Persistir receitas desbloqueadas, upgrades e estado dos NPCs.
4. Migrar atalhos de gameplay/UI restantes para action maps do Input System e adicionar gamepad.
5. Separar assemblies por domínio quando os contratos de conteúdo estabilizarem.
6. Criar CI somente quando o projeto decidir adotar uma licença Unity para automação.
7. Fazer profiling em build de Development, não apenas no Editor.
