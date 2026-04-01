using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;
using System;

public class DungeonGenerator : MonoBehaviour
{
    [Header("Map Size")]
    [SerializeField] private int mapWidth = 60;
    [SerializeField] private int mapHeight = 40;

    [Header("BSP Settings")]
    [SerializeField] private int minRoomSize = 6;
    [SerializeField] private int maxDepth = 4;

    [Header("Seed")]
    [SerializeField] private int seed = 1234;
    [SerializeField] private bool randomizeSeedOnPlay = false;

    [Header("Tilemaps")]
    [SerializeField] private Tilemap groundTilemap;
    [SerializeField] private Tilemap contentTilemap;

    [Header("Tiles")]
    [SerializeField] private TileBase floorTile;

    private System.Random rng;
    private bool[,] floorMap;

    private void Start()
    {
        Generate();
    }

    [ContextMenu("Generate")]
    private void Generate()
    {
        if (randomizeSeedOnPlay)
        {
            seed = UnityEngine.Random.Range(0, int.MaxValue);
        }

        rng = new System.Random(seed);
        floorMap = new bool[mapWidth, mapHeight];

        clearTilemaps();

        // temp test 1 big room
        CarveRect(2, 2, mapWidth - 4, mapHeight - 4);

        DrawFloorMap();
        Debug.Log($"Dungeon generated with seed: {seed}");
    }

    private void clearTilemaps()
    {
        if (groundTilemap != null)
        {
            groundTilemap.ClearAllTiles();
        }
        if (contentTilemap != null)
        {
            contentTilemap.ClearAllTiles();
        }
    }
    
    private void CarveRect(int x, int y, int width, int height)
    {
        for (int ix = x; ix < x + width; ix++)
        {
            for (int iy = y; iy < y + height; iy++)
            {
                if (InBounds(ix, iy))
                {
                    floorMap[ix, iy] = true;
                }
            }

        }
    }
    
    private bool InBounds(int x, int y)
    {
        return x >= 0 && y >= 0 && x< mapWidth && y < mapHeight;
    }
    private void DrawFloorMap()
    {
        if (groundTilemap == null || floorTile == null)
        {
            return;
        }
        
        for ( int x = 0; x < mapWidth; x++)
        {
            for ( int y = 0; y < mapHeight; y++)
            {
                if (floorMap[x, y])
                {
                    groundTilemap.SetTile(new Vector3Int(x, y, 0), floorTile);
                }
            }
        }
    }
}
