# Contribuindo

## Regras de conteúdo

- Todo `ItemData` persistente deve ter `itemId` único, estável, sem espaços nas extremidades e ser incluído em `GameContentCatalog`.
- Toda `CropDefinition` persistente deve seguir a mesma regra para `cropId`.
- Todo `DishDefinition` deve ter `dishId` único, item preparado catalogado e ingredientes válidos.
- Renomear o asset ou o texto exibido é seguro; alterar qualquer ID persistente exige uma migração de save.
- Fontes editáveis ficam em `ArtSource`; somente exportações usadas pelo Unity ficam em `Assets/Art`.

## Regras de código

- `WorldInfoSystem` é a fonte autoritativa de tempo, calendário, clima e dinheiro.
- Inventários só devem ser alterados pelas APIs de `InventorySystem` e `CrateStorageInteractable`.
- Operações com mais de uma etapa devem validar primeiro e possuir rollback.
- Modais devem preservar `Time.timeScale`, HUD e estado anterior do movimento.
- Não crie hierarquias ou assets em `OnValidate`; use ferramentas explícitas do Editor ou fallback em runtime.

## Antes de entregar uma mudança

1. Compile os assemblies runtime e Editor.
2. Rode os testes de Edit Mode.
3. Rode os três validadores descritos no README.
4. Confirme que não há `.meta` órfão ou ausente.
5. Verifique `git diff --check` e não inclua `Library`, `Temp`, `Logs`, `UserSettings` ou arquivos de IDE.
