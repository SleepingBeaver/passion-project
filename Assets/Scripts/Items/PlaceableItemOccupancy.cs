using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlaceableItemOccupancy : MonoBehaviour
{
    private static readonly Dictionary<Grid, Dictionary<Vector3Int, PlaceableItemOccupancy>> OccupantsByGrid = new();

    private Grid targetGrid;
    private Vector3Int anchorCell;
    private Vector2Int footprintSize = Vector2Int.one;
    private bool isRegistered;

    public Grid TargetGrid => targetGrid;
    public Vector3Int AnchorCell => anchorCell;
    public Vector2Int FootprintSize => footprintSize;
    public bool IsRegistered => isRegistered;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry()
    {
        OccupantsByGrid.Clear();
    }

    public void Initialize(Grid grid, Vector3Int cell, Vector2Int size)
    {
        Unregister();

        targetGrid = grid;
        anchorCell = cell;
        footprintSize = ClampSize(size);

        Register();
    }

    public static bool CanOccupy(
        Grid grid,
        Vector3Int anchor,
        Vector2Int size,
        PlaceableItemOccupancy ignoredOccupant = null)
    {
        if (grid == null || !OccupantsByGrid.TryGetValue(grid, out Dictionary<Vector3Int, PlaceableItemOccupancy> occupants))
            return true;

        Vector2Int clampedSize = ClampSize(size);

        for (int y = 0; y < clampedSize.y; y++)
        {
            for (int x = 0; x < clampedSize.x; x++)
            {
                Vector3Int cell = anchor + new Vector3Int(x, y, 0);
                if (!occupants.TryGetValue(cell, out PlaceableItemOccupancy occupant))
                    continue;

                if (occupant == null)
                {
                    occupants.Remove(cell);
                    continue;
                }

                if (occupant != ignoredOccupant)
                    return false;
            }
        }

        return true;
    }

    public bool OccupiesCell(Grid grid, Vector3Int cell)
    {
        if (!isRegistered || grid != targetGrid)
            return false;

        Vector3Int offset = cell - anchorCell;
        return offset.x >= 0 && offset.x < footprintSize.x &&
               offset.y >= 0 && offset.y < footprintSize.y;
    }

    private void OnEnable()
    {
        Register();
    }

    private void OnDisable()
    {
        Unregister();
    }

    private void OnDestroy()
    {
        Unregister();
    }

    private void Register()
    {
        if (isRegistered || !isActiveAndEnabled || targetGrid == null)
            return;

        if (!CanOccupy(targetGrid, anchorCell, footprintSize, this))
        {
            Debug.LogError($"Nao foi possivel registrar o footprint de {name}: uma das celulas ja esta ocupada.", this);
            return;
        }

        if (!OccupantsByGrid.TryGetValue(targetGrid, out Dictionary<Vector3Int, PlaceableItemOccupancy> occupants))
        {
            occupants = new Dictionary<Vector3Int, PlaceableItemOccupancy>();
            OccupantsByGrid.Add(targetGrid, occupants);
        }

        for (int y = 0; y < footprintSize.y; y++)
        {
            for (int x = 0; x < footprintSize.x; x++)
                occupants[anchorCell + new Vector3Int(x, y, 0)] = this;
        }

        isRegistered = true;
    }

    private void Unregister()
    {
        if (!isRegistered)
            return;

        if (targetGrid != null && OccupantsByGrid.TryGetValue(targetGrid, out Dictionary<Vector3Int, PlaceableItemOccupancy> occupants))
        {
            for (int y = 0; y < footprintSize.y; y++)
            {
                for (int x = 0; x < footprintSize.x; x++)
                {
                    Vector3Int cell = anchorCell + new Vector3Int(x, y, 0);
                    if (occupants.TryGetValue(cell, out PlaceableItemOccupancy occupant) && occupant == this)
                        occupants.Remove(cell);
                }
            }

            if (occupants.Count == 0)
                OccupantsByGrid.Remove(targetGrid);
        }

        isRegistered = false;
    }

    private static Vector2Int ClampSize(Vector2Int size)
    {
        return new Vector2Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y));
    }
}
