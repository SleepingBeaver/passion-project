# A Taste of Home

Protótipo 2D isométrico de farming, cozinha e atendimento ativo feito em Unity pela **Sleeping Beaver**. O estado atual inclui exploração, coleta, inventário/hotbar, plantio de tomates, crafting, caixotes posicionáveis, pedidos, preparo e entrega de pratos, ciclo de dia, clima, economia, reputação, iluminação e menus modais.

## Abrindo o projeto

- Unity: `6000.5.8f1`.
- Projeto Unity: esta pasta, não a pasta `Game` acima dela.
- Cena principal: `Assets/Scenes/SampleScene.unity`.
- Pipeline: Universal Render Pipeline 2D; pixel art padronizada em 64 PPU.

Abra a pasta pelo Unity Hub e aguarde a importação antes de carregar a cena. `Library`, `Temp`, `Logs`, arquivos de IDE e builds são locais e não pertencem ao Git.

## Controles atuais

| Ação | Controle |
| --- | --- |
| Movimento / corrida | WASD / Shift esquerdo |
| Interagir ou segurar interação | E |
| Interagir com farming pelo mouse | Clique esquerdo |
| Inventário | Tab |
| Crafting | C |
| Pausa / fechar modal | Esc |
| Selecionar hotbar | Teclas numéricas |
| Trocar linha da hotbar | Setas para cima/baixo |
| Espelhar caixote antes de posicionar | R |

Atalhos de geração de itens e avanço de dia em `InventoryDebugInput` só ficam ativos no Editor ou em Development Builds. Para testar rapidamente a cozinha, `Y` adiciona cinco tomates.

## Sistemas principais

- `Assets/Scripts/Core`: ciclo de dia e iluminação.
- `Assets/Scripts/Farming`: solo, culturas, crescimento e colheita.
- `Assets/Scripts/Items`: itens no mundo, drops, árvores, caixotes e ocupação da grade.
- `Assets/Scripts/Player`: movimento e roteamento extensível de interações.
- `Assets/Scripts/Restaurant`: pratos, pedidos, cozinha, entrega, reputação e HUD de atendimento.
- `Assets/Scripts/UI`: inventário, crafting, armazenamento, HUD e menus.
- `Assets/Scripts/SaveSystem`: catálogo estável de conteúdo e save/load versionado.
- `Assets/Editor`: validadores, ferramentas de recuperação, profiling e testes de Edit Mode.
- `ArtSource`: fontes de arte preservadas fora de `Assets`; exportações prontas para o jogo ficam em `Assets/Art`.

## Persistência

O jogo carrega automaticamente um save válido e salva ao concluir um novo dia, ao suspender o aplicativo e ao sair. O formato usa versão, checksum, arquivo temporário e backup. São persistidos:

- posição e direção do jogador;
- calendário, horário, clima e dinheiro;
- slots e seleção do inventário;
- plots arados/regados, cultura e estágio de crescimento;
- caixotes colocados, orientação e conteúdo;
- pedido ativo, prazo, reputação e estatísticas do restaurante;
- preparo em andamento e pratos aguardando coleta na cozinha.

Itens, culturas e pratos persistentes precisam estar em `Assets/Resources/GameContentCatalog.asset` e possuir `itemId`, `cropId` ou `dishId` únicos. O arquivo final fica em `Application.persistentDataPath/savegame.json`.

## Primeiro atendimento ativo

O restaurante abre às 08:00. O HUD no canto superior esquerdo mostra cliente, pedido, prazo e recompensa. O primeiro prato é a sopa de tomate caseira:

1. Obtenha dois tomates pelo farming ou use `Y` em ambiente de desenvolvimento.
2. Interaja com a estação laranja próxima ao ponto inicial para começar o preparo.
3. Após cinco segundos, interaja novamente para recolher a sopa.
4. Leve-a ao balcão verde e pressione `E` para servir.
5. Uma entrega concede 80 moedas e 5 pontos de reputação.

## Validação

No Unity, execute:

1. `Tools > Project Recovery > Validate Development Scene`.
2. `Tools > Day Cycle > Validate Feature`.
3. `Tools > Farming > Validate Feature`.
4. `Window > General > Test Runner` e rode os testes de Edit Mode.
5. Em Play Mode, valide o ciclo coletar → plantar → colher → cozinhar → servir → craftar → armazenar → dormir → reabrir o jogo.

A revisão de arquitetura e as invariantes estão em [`CODE_REVIEW.md`](CODE_REVIEW.md). A sequência recomendada de produto está em [`ROADMAP.md`](ROADMAP.md).
