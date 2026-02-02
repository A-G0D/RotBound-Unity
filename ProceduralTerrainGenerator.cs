using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.InputSystem;
using System.Linq; // Added for list manipulation

public class ProceduralTerrainGenerator : MonoBehaviour
{
    [Header("References")]
    public Transform player;
    public Tilemap tilemap;
    public Grid grid;

    [Header("Chunk Settings")]
    public int chunkSize = 16;
    public int chunkLoadRadius = 3;

    [Header("Noise Settings")]
    public float noiseScale = 0.1f;
    public int seed = 12345;
    public Vector2 noiseOffset = Vector2.zero;

    [Header("Tile Settings")]
    public float buildingThreshold = 0.40f;

    [Header("Player Movement")]
    public float moveSpeed = 5f;

    private Dictionary<Vector2Int, ChunkData> loadedChunks = new Dictionary<Vector2Int, ChunkData>();
    private Vector2Int currentPlayerChunk;
    private Dictionary<int, Tile> gradientTiles = new Dictionary<int, Tile>();
    private Tile buildingTile;
    private Rigidbody2D playerRigidbody;

    private class ChunkData
    {
        public Vector2Int chunkCoord;
        public TileData[,] tiles;
    }

    private class TileData
    {
        public Vector3Int position;
        public float noiseValue;
        public bool isBuilding;
    }

    void Start()
    {
        // Auto-setup if references are missing
        if (player == null) player = transform;

        SetupPlayer();
        if (tilemap == null) SetupTilemapComponents();
        CreateDefaultTiles();

        currentPlayerChunk = GetChunkCoordFromPosition(player.position);
        UpdateChunks();
    }

    void Update()
    {
        Vector2Int playerChunk = GetChunkCoordFromPosition(player.position);
        if (playerChunk != currentPlayerChunk)
        {
            currentPlayerChunk = playerChunk;
            UpdateChunks();
        }
        HandlePlayerMovement();
    }

    // ... [SetupPlayer, HandlePlayerMovement, SetupTilemapComponents, CreateDefaultTiles, CreateColorSprite Unchanged] ...
    // Note: Included abbreviated versions of unchanged methods to save space, 
    // but in your file keep the original implementations of the Setup/Create methods.

    void SetupPlayer()
    {
        playerRigidbody = player.GetComponent<Rigidbody2D>();
        if (playerRigidbody == null)
        {
            playerRigidbody = player.gameObject.AddComponent<Rigidbody2D>();
            playerRigidbody.gravityScale = 0;
            playerRigidbody.constraints = RigidbodyConstraints2D.FreezeRotation;
        }

        SpriteRenderer spriteRenderer = player.GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            spriteRenderer = player.gameObject.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = CreateColorSprite(Color.red);
            spriteRenderer.sortingOrder = 10;
        }
    }

    void HandlePlayerMovement()
    {
        Vector2 movement = Vector2.zero;
        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed) movement.y += 1;
            if (Keyboard.current.sKey.isPressed) movement.y -= 1;
            if (Keyboard.current.aKey.isPressed) movement.x -= 1;
            if (Keyboard.current.dKey.isPressed) movement.x += 1;
        }
        movement.Normalize();
        playerRigidbody.velocity = movement * moveSpeed;
    }

    void SetupTilemapComponents()
    {
        GameObject gridObj = new GameObject("Grid");
        grid = gridObj.AddComponent<Grid>();
        grid.cellSize = new Vector3(1, 1, 0);

        GameObject tilemapObj = new GameObject("Tilemap");
        tilemapObj.transform.SetParent(grid.transform);
        tilemap = tilemapObj.AddComponent<Tilemap>();
        tilemapObj.AddComponent<TilemapRenderer>().sortingOrder = 0;
        tilemapObj.AddComponent<TilemapCollider2D>();
    }

    void CreateDefaultTiles()
    {
        for (int i = 0; i < 256; i++)
        {
            float grayValue = i / 255f;
            Color tileColor = new Color(grayValue, grayValue, grayValue, 1f);
            Tile tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = CreateColorSprite(tileColor);
            gradientTiles[i] = tile;
        }
        buildingTile = ScriptableObject.CreateInstance<Tile>();
        buildingTile.sprite = CreateColorSprite(new Color(0.6f, 0.4f, 0.2f, 1f));
        buildingTile.colliderType = Tile.ColliderType.Grid;
    }

    Sprite CreateColorSprite(Color color)
    {
        Texture2D texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.filterMode = FilterMode.Point;
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
    }

    Vector2Int GetChunkCoordFromPosition(Vector3 worldPosition)
    {
        int chunkX = Mathf.FloorToInt(worldPosition.x / chunkSize);
        int chunkY = Mathf.FloorToInt(worldPosition.y / chunkSize);
        return new Vector2Int(chunkX, chunkY);
    }

    void UpdateChunks()
    {
        HashSet<Vector2Int> chunksToKeep = new HashSet<Vector2Int>();
        for (int x = -chunkLoadRadius; x <= chunkLoadRadius; x++)
        {
            for (int y = -chunkLoadRadius; y <= chunkLoadRadius; y++)
            {
                Vector2Int chunkCoord = new Vector2Int(currentPlayerChunk.x + x, currentPlayerChunk.y + y);
                chunksToKeep.Add(chunkCoord);
                if (!loadedChunks.ContainsKey(chunkCoord)) LoadChunk(chunkCoord);
            }
        }

        List<Vector2Int> chunksToUnload = new List<Vector2Int>();
        foreach (var chunkCoord in loadedChunks.Keys)
        {
            if (!chunksToKeep.Contains(chunkCoord)) chunksToUnload.Add(chunkCoord);
        }
        foreach (var chunkCoord in chunksToUnload) UnloadChunk(chunkCoord);
    }

    // ---------------------------------------------------------
    //  MODIFIED GENERATION LOGIC STARTS HERE
    // ---------------------------------------------------------

    void LoadChunk(Vector2Int chunkCoord)
    {
        ChunkData chunk = new ChunkData
        {
            chunkCoord = chunkCoord,
            tiles = new TileData[chunkSize, chunkSize]
        };

        Vector2Int worldOrigin = new Vector2Int(chunkCoord.x * chunkSize, chunkCoord.y * chunkSize);

        // 1. First Pass: Generate raw noise and identify potential building spots
        // We use a temporary boolean map for processing blobs
        bool[,] potentialBuildings = new bool[chunkSize, chunkSize];

        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                Vector2Int worldPos = worldOrigin + new Vector2Int(x, y);
                Vector3Int tilePos = new Vector3Int(worldPos.x, worldPos.y, 0);
                float noiseValue = GetDeterministicNoise(worldPos.x, worldPos.y);

                chunk.tiles[x, y] = new TileData
                {
                    position = tilePos,
                    noiseValue = noiseValue,
                    isBuilding = false // Default to false, will set true in Step 2 if valid
                };

                // Mark potential building based on threshold
                if (noiseValue > buildingThreshold)
                {
                    potentialBuildings[x, y] = true;
                }
            }
        }

        // 2. Second Pass: Find connected blobs and fit rectangles
        List<List<Vector2Int>> blobs = FindBlobs(potentialBuildings);

        foreach (var blob in blobs)
        {
            RectInt bestRect = FindLargestRectangle(blob, potentialBuildings);

            // Check if rectangle fits 5x3 or 3x5 criteria
            bool fitsHorizontal = bestRect.width >= 5 && bestRect.height >= 3;
            bool fitsVertical = bestRect.width >= 3 && bestRect.height >= 5;

            if (fitsHorizontal || fitsVertical)
            {
                // Mark ONLY the rectangle area as building
                for (int x = bestRect.x; x < bestRect.xMax; x++)
                {
                    for (int y = bestRect.y; y < bestRect.yMax; y++)
                    {
                        chunk.tiles[x, y].isBuilding = true;
                    }
                }
            }
            // If it doesn't fit, do nothing. The tiles remain isBuilding = false (normal noise color)
        }

        // 3. Final Pass: Apply tiles to tilemap
        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                TileData t = chunk.tiles[x, y];
                if (t.isBuilding)
                {
                    tilemap.SetTile(t.position, buildingTile);
                }
                else
                {
                    int tileIndex = Mathf.Clamp(Mathf.RoundToInt(t.noiseValue * 255), 0, 255);
                    tilemap.SetTile(t.position, gradientTiles[tileIndex]);
                }
            }
        }

        loadedChunks.Add(chunkCoord, chunk);
    }

    // Helper: Flood Fill to find connected "true" pixels
    List<List<Vector2Int>> FindBlobs(bool[,] map)
    {
        List<List<Vector2Int>> blobs = new List<List<Vector2Int>>();
        bool[,] visited = new bool[chunkSize, chunkSize];

        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                if (map[x, y] && !visited[x, y])
                {
                    List<Vector2Int> currentBlob = new List<Vector2Int>();
                    Queue<Vector2Int> queue = new Queue<Vector2Int>();
                    
                    queue.Enqueue(new Vector2Int(x, y));
                    visited[x, y] = true;

                    while (queue.Count > 0)
                    {
                        Vector2Int pos = queue.Dequeue();
                        currentBlob.Add(pos);

                        // Check neighbors
                        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
                        foreach (var dir in dirs)
                        {
                            Vector2Int neighbor = pos + dir;
                            
                            // Bounds check
                            if (neighbor.x >= 0 && neighbor.x < chunkSize && 
                                neighbor.y >= 0 && neighbor.y < chunkSize)
                            {
                                if (map[neighbor.x, neighbor.y] && !visited[neighbor.x, neighbor.y])
                                {
                                    visited[neighbor.x, neighbor.y] = true;
                                    queue.Enqueue(neighbor);
                                }
                            }
                        }
                    }
                    blobs.Add(currentBlob);
                }
            }
        }
        return blobs;
    }

    // Helper: Brute force largest rectangle inside the blob
    RectInt FindLargestRectangle(List<Vector2Int> blob, bool[,] map)
    {
        RectInt maxRect = new RectInt(0, 0, 0, 0);
        int maxArea = 0;

        // Optimization: only check pixels that are actually in the blob
        foreach (var startPos in blob)
        {
            // For every point, expand right and down to find valid rectangles
            // This is O(N^3) but N is small (chunkSize=16), so it's very fast.
            
            // Max possible width from this point
            int limitW = chunkSize - startPos.x;
            int limitH = chunkSize - startPos.y;

            for (int w = 1; w <= limitW; w++)
            {
                for (int h = 1; h <= limitH; h++)
                {
                    // If the area is already smaller than our best, don't bother checking validity
                    // unless we are searching for a specific shape, but here we want Max Area.
                    if (w * h <= maxArea) continue;

                    if (IsRectangleValid(startPos.x, startPos.y, w, h, map))
                    {
                        if (w * h > maxArea)
                        {
                            maxArea = w * h;
                            maxRect = new RectInt(startPos.x, startPos.y, w, h);
                        }
                    }
                    else
                    {
                        // If this height failed for this width, higher heights will also fail
                        break; 
                    }
                }
            }
        }
        return maxRect;
    }

    bool IsRectangleValid(int startX, int startY, int width, int height, bool[,] map)
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (!map[startX + x, startY + y]) return false;
            }
        }
        return true;
    }

    void UnloadChunk(Vector2Int chunkCoord)
    {
        if (!loadedChunks.ContainsKey(chunkCoord)) return;
        ChunkData chunk = loadedChunks[chunkCoord];
        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                TileData tileData = chunk.tiles[x, y];
                tilemap.SetTile(tileData.position, null);
            }
        }
        loadedChunks.Remove(chunkCoord);
    }

    float GetDeterministicNoise(int worldX, int worldY)
    {
        float seedOffsetX = seed * 0.1f;
        float seedOffsetY = seed * 0.1f;
        float sampleX = (worldX + noiseOffset.x + seedOffsetX) * noiseScale;
        float sampleY = (worldY + noiseOffset.y + seedOffsetY) * noiseScale;
        return Mathf.Min(Mathf.PerlinNoise(sampleX, sampleY)*1.2f, 1f); // Slightly boost max value for more building spots
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying || player == null) return;
        Gizmos.color = Color.yellow;
        foreach (var chunkCoord in loadedChunks.Keys)
        {
            Vector3 chunkWorldPos = new Vector3(chunkCoord.x * chunkSize, chunkCoord.y * chunkSize, 0);
            Gizmos.DrawWireCube(chunkWorldPos + new Vector3(chunkSize / 2f, chunkSize / 2f, 0), new Vector3(chunkSize, chunkSize, 0));
        }
    }
}