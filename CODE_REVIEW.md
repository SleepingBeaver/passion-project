# Revisao tecnica do codigo

Este documento registra a responsabilidade dos scripts autorais de **A Taste of Home**, os fluxos entre sistemas e as decisoes tomadas na revisao. O escopo cobre `Assets/Scripts` e `Assets/Editor`. Shaders do TextMesh Pro, arquivos `.meta`, cenas, prefabs, arte e configuracoes geradas pelo Unity nao sao codigo autoral e nao foram reescritos.

## Convencao de comentarios

Os scripts usam comentarios de **secao** para explicar intencao, invariantes e fronteiras entre responsabilidades. Comentarios que apenas repetem a sintaxe foram evitados, pois aumentam manutencao sem ajudar a equipe. Ao alterar um fluxo, atualize o comentario da secao quando a intencao ou a regra de negocio mudar.

## Fluxos principais

### Relogio, dia e iluminacao

`WorldInfoSystem` e a fonte autoritativa de calendario, horario, clima e dinheiro. Ele publica eventos em vez de exigir polling. `GameplayDayCycleController` decide sono voluntario e encerramento forcado do dia, suspende o relogio e aplica o salto de horario. `TimeOfDayLightingController` escuta os saltos/passos do relogio e interpola o Global Light 2D. `FarmingSystem` escuta `NewDayStarted` para avancar as culturas uma vez por dia de gameplay.

### Inventario, hotbar e modais

`InventorySystem` possui os slots e a selecao. Interfaces recebem a mesma lista somente para leitura e reagem a `InventoryChanged`/`SelectionChanged`. `SimpleInventoryUI` coordena a janela principal e o pause; `CraftingUI`, `CrateStorageUI` e `PauseMenuUI` usam esse bloqueio modal quando disponivel, restaurando o estado anterior do jogador ao fechar.

### Interacao e farming

`PlayerInteractor` mantem uma lista nao alocante de `WorldInteractable` proximos, escolhe o melhor alvo, controla interacoes por toque/hold e consulta o farming. `FarmingSystem` valida a celula antes de executar qualquer acao. Plantio consome a semente somente durante o commit e devolve o item se o estado mudar antes da conclusao.

### Crafting e caixotes

`CraftingSystem` simula remocao e insercao antes de alterar o inventario real. A transacao agrupa notificacoes e possui rollback. `WorldCratePlacementSystem` valida alcance e colisao antes de consumir o item. `CrateStorageInteractable` mantem o conteudo e `CrateStorageUI` transfere pilhas completas ou parciais entre os dois armazenamentos.

## Mapa dos scripts de runtime

### Core

| Arquivo | Responsabilidade | Dependencias principais |
| --- | --- | --- |
| `GameplayDayCycleController.cs` | Sono, fim forcado, fade e salto atomico para o novo dia. | `WorldInfoSystem`, `CanvasGroup` |
| `TimeOfDayLightingController.cs` | Curva diaria e suavizacao do Global Light 2D. | `WorldInfoSystem`, `Light2D` |

### Farming

| Arquivo | Responsabilidade | Dependencias principais |
| --- | --- | --- |
| `CropDefinition.cs` | Definicao de semente, colheita, estagios e sprites de uma cultura. | `ItemData` |
| `FarmingSystem.cs` | Cache de solo, targeting, arar, regar, plantar, crescer, colher e desenhar plots. | `Tilemap`, `InventorySystem`, ciclo do dia |

### Itens e mundo

| Arquivo | Responsabilidade | Dependencias principais |
| --- | --- | --- |
| `WorldInteractable.cs` | Contrato base de foco, toque, hold e bloqueio de interacoes. | `PlayerInteractor` |
| `PlantInteractable.cs` | Coleta parcial ou total de plantas simples no mundo. | Inventario |
| `TreeInteractable.cs` | Validacao do machado, animacao e quebra com hold. | `ResourceNodeDropper` |
| `ResourceNodeDropper.cs` | Divide uma quantidade em poucos drops visuais. | `DroppedItemVisual` |
| `DroppedItemVisual.cs` | Dispersao, magnetismo, coleta parcial e cache do jogador. | Inventario, alvo de pickup |
| `WorldCratePlacementSystem.cs` | Ghost, snap no Grid, colisao, espelhamento e criacao de caixotes. | Inventario, camera, Grid |
| `CrateStorageInteractable.cs` | Slots do caixote, abertura e quebra quando vazio. | `CrateStorageUI`, drops |

### Jogador

| Arquivo | Responsabilidade | Dependencias principais |
| --- | --- | --- |
| `IsoPlayerController2D.cs` | Movimento isometrico, sprint, direcao de idle e animator sem escritas redundantes. | Input System, `Rigidbody2D` |
| `PlayerInteractor.cs` | Deteccao de alvos, prompt, hold e roteamento de acoes de farming. | Inventario, farming, UI |

### Inventario e UI

| Arquivo | Responsabilidade | Dependencias principais |
| --- | --- | --- |
| `ItemData.cs` | ScriptableObject com identidade, icone, pilha e escalas visuais do item. | Unity assets |
| `InventoryItem.cs` | DTO legado simples de item e quantidade; nao participa do fluxo atual. | `ItemData` |
| `InventorySlotData.cs` | Estado mutavel e invariante de um slot. | `ItemData` |
| `InventorySystem.cs` | Fonte autoritativa dos slots, selecao, pilhas, lotes e eventos. | `InventoryUIController` |
| `InventoryUIController.cs` | Grade do inventario e drag-and-drop entre slots. | `InventorySlotVisual` |
| `InventorySlotVisual.cs` | Renderizacao adaptativa, selecao e eventos de ponteiro de um slot. | uGUI, TMP |
| `UIInventoryBar.cs` | Hotbar paginada, atalhos numericos e navegacao de linhas. | Inventario, Input System |
| `UIInventorySlot.cs` | Slot visual simples mantido para layouts legados. | uGUI, TMP |
| `SimpleInventoryUI.cs` | Abertura animada, fundo, pause, HUD e bloqueio de modais externos. | Jogador, CanvasGroup |
| `InventoryDebugInput.cs` | Atalhos habilitados apenas no Editor/Development Build. | Inventario, ciclo do dia |
| `CraftingRecipeDefinition.cs` | DTOs serializados e validacao de receitas/ingredientes. | `ItemData` |
| `CraftingSystem.cs` | Simulacao, transacao, rollback e bootstrap do crafting. | Inventario |
| `CraftingUI.cs` | Modal, lista de receitas, detalhes, feedback e layout de fallback. | Crafting, inventario |
| `CrateStorageUI.cs` | Duas grades, transferencias, drag preview e layout de fallback. | Inventario, caixote |
| `PauseMenuUI.cs` | Pause, opcoes persistidas, tres idiomas e layout editavel. | PlayerPrefs, UI, jogador |
| `CursorVisualController.cs` | Cursor customizado e conversao da posicao do ponteiro para o Canvas. | Input System, uGUI |
| `WorldInfoSystem.cs` | Calendario, horario, clima, economia e eventos autoritativos. | Nenhuma dependencia de gameplay |
| `UIInfoPanel.cs` | Montagem e atualizacao do painel de estacao, dia, hora, clima e dinheiro. | `WorldInfoSystem`, TMP |
| `PresentationLayoutController.cs` | Moldura 16:9, escala da UI e viewport da camera. | Canvas, camera |

## Ferramentas do Editor

| Arquivo | Responsabilidade |
| --- | --- |
| `DayCycleFeatureValidator.cs` | Valida configuracao, horarios de sono, fim forcado e curva de iluminacao. |
| `FarmingFeatureValidator.cs` | Valida pipeline 64 PPU, referencias e fluxo funcional completo de farming. |
| `PauseMenuUIEditor.cs` | Adiciona controles de preview ao Inspector do pause menu. |
| `ProjectPerformanceValidator.cs` | Mede uma carga controlada de UI/inventario/crafting no Play Mode. |
| `ProjectRecoveryValidator.cs` | Audita cena, assets, referencias e erros de runtime. |
| `RecoverEmptySceneOnLoad.cs` | Recupera a cena de desenvolvimento somente quando a sessao abre vazia e limpa. |

## Otimizacoes e correcoes desta revisao

- A iluminacao deixa de escrever no `Light2D` depois de atingir cor e intensidade alvo; o Lerp termina com um snap dentro de tolerancia.
- `InventorySystem.HasItem` encerra a busca assim que encontra a quantidade solicitada.
- O buffer de simulacao de crafting reserva a capacidade necessaria antes de copiar slots.
- Dinheiro e somado em 64 bits antes do clamp, evitando overflow de `int`.
- Atualizacoes de dinheiro e clima iguais ao estado atual nao emitem eventos redundantes.
- Drops saneiam duracoes, raios e velocidades e tratam dispersao com duracao zero sem divisao invalida.
- A divisao de drops garante ao menos uma peca visual, inclusive para assets antigos com valor serializado invalido.
- `InventorySlotData.SetItem` normaliza entradas nulas ou nao positivas para um slot realmente vazio.
- IDs de enxada e regador foram centralizados no farming para evitar divergencia entre teclado e mouse.

## Cuidados para proximas mudancas

- Nao renomeie campos `[SerializeField]` sem `FormerlySerializedAs`; cenas e prefabs dependem desses nomes.
- Mantenha `WorldInfoSystem` como fonte unica de tempo e calendario. Saltos devem passar por sua API publica para notificar luz e UI.
- Nao altere inventario durante a validacao de uma receita. Use a simulacao e mantenha o rollback do commit.
- Evite `FindAnyObjectByType`, `Resources.FindObjectsOfTypeAll` e criacao de listas dentro de `Update`. As buscas atuais sao de bootstrap ou possuem retry limitado.
- Ao adicionar um modal, integre-o ao bloqueio de `SimpleInventoryUI` para nao restaurar `Time.timeScale` ou controles pertencentes a outro menu.

## Validacao recomendada

1. Abrir `Assets/Scenes/SampleScene.unity` no Unity 6000.5.8f1.
2. Executar `Tools > Project Recovery > Validate Development Scene`.
3. Executar `Tools > Day Cycle > Validate Feature`.
4. Executar `Tools > Farming > Validate Feature`.
5. Em Play Mode, testar inventario, crafting, placement/armazenamento do caixote, farming por teclado e mouse, sono e pause menu.
