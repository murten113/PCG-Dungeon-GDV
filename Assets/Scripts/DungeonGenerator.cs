using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class DungeonGenerator : MonoBehaviour
{
    [Header("Map Size")]
    [SerializeField] private int mapWidth = 60;
    [SerializeField] private int mapHeight = 40;

    [Header("BSP Settings")]
    [SerializeField] private int minPartitionSize = 12;
    [SerializeField] private int minRoomSize = 6;
    [SerializeField] private int maxDepth = 4;
    [SerializeField, Range(0, 2)] private int roomPadding = 1;

    [Header("Seed")]
    [SerializeField] private int seed = 12345;
    [SerializeField] private bool randomizeSeedOnPlay = false;

    [Header("Tilemaps")]
    [SerializeField] private Tilemap groundTilemap;
    [SerializeField] private Tilemap contentTilemap;

    [Header("Tiles")]
    [SerializeField] private TileBase floorTile;
    [SerializeField] private TileBase startTile;
    [SerializeField] private TileBase endTile;
    [SerializeField] private TileBase challengeATile;
    [SerializeField] private TileBase challengeBTile;
	[SerializeField] private TileBase rewardTile;

    [Header("Camera")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private float cameraPadding = 2f;

	[Header("Placement Constraints")]
	[SerializeField] private int minChallengeBDistanceFromStart = 12;
	[SerializeField] private int minChallengeBDistanceFromEnd = 12;
	[SerializeField] private int minRewardDistanceFromStart = 16;
	[SerializeField] private int minRewardDistanceFromEnd = 16;
    [SerializeField, Range(0f, 1f)] private float challengeATargetDistanceFactor = 0.5f; // mid-route
    [SerializeField] private int minChallengeADistanceFromStart = 8;
    [SerializeField] private int minChallengeADistanceFromEnd = 8;
	[SerializeField] private int minSeparationBetweenBAndReward = 8;


    // Analysis data
    private int[,] distanceMap;
    private List<Vector2Int> floorCells = new();
    private List<Vector2Int> deadEnds = new();
    private Vector2Int startPos;
    private Vector2Int farthestPos;
    private Vector2Int challengeAPos;
    private Vector2Int challengeBPos;
	private Vector2Int rewardPos;

    private System.Random rng;
    private bool[,] floorMap;
    private readonly List<RectInt> rooms = new();



    private class BSPNode
    {
        public RectInt Bounds;
        public BSPNode Left;
        public BSPNode Right;
        public RectInt? Room;
        public bool IsLeaf => Left == null && Right == null;

        public BSPNode(RectInt bounds)
        {
            Bounds = bounds;
        }
    }

    private void Start()
    {
        Generate();
    }

    [ContextMenu("Generate")]
    public void Generate()
    {
        if (randomizeSeedOnPlay)
            seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);

        rng = new System.Random(seed);
        floorMap = new bool[mapWidth, mapHeight];
        rooms.Clear();

        ClearTilemaps();

        RectInt rootRect = new RectInt(1, 1, mapWidth - 2, mapHeight - 2);
        BSPNode root = new BSPNode(rootRect);

        SplitRecursive(root, 0);
        CreateRoomsInLeaves(root);
        ConnectRooms(root);
        CarveAllRooms();

        AnalyzeLayout();
        PlaceChallenges();
		PlaceReward();

        DrawFloorMap();
        DrawContent();
        FitCameraToDungeon();

        Debug.Log($"Dungeon generated with seed: {seed}, rooms: {rooms.Count}, deadEnds: {deadEnds.Count}");
    }

    private void SplitRecursive(BSPNode node, int depth)
    {
        if (depth >= maxDepth) return;

        bool canSplitHoriz = node.Bounds.height >= minPartitionSize * 2;
        bool canSplitVert = node.Bounds.width >= minPartitionSize * 2;

        if (!canSplitHoriz && !canSplitVert) return;

        bool splitHoriz;
        if (canSplitHoriz && canSplitVert)
            splitHoriz = rng.NextDouble() < 0.5;
        else
            splitHoriz = canSplitHoriz;

        if (splitHoriz)
        {
            int min = node.Bounds.yMin + minPartitionSize;
            int max = node.Bounds.yMax - minPartitionSize;
            int splitY = rng.Next(min, max);

            RectInt a = new RectInt(node.Bounds.xMin, node.Bounds.yMin, node.Bounds.width, splitY - node.Bounds.yMin);
            RectInt b = new RectInt(node.Bounds.xMin, splitY, node.Bounds.width, node.Bounds.yMax - splitY);

            node.Left = new BSPNode(a);
            node.Right = new BSPNode(b);
        }
        else
        {
            int min = node.Bounds.xMin + minPartitionSize;
            int max = node.Bounds.xMax - minPartitionSize;
            int splitX = rng.Next(min, max);

            RectInt a = new RectInt(node.Bounds.xMin, node.Bounds.yMin, splitX - node.Bounds.xMin, node.Bounds.height);
            RectInt b = new RectInt(splitX, node.Bounds.yMin, node.Bounds.xMax - splitX, node.Bounds.height);

            node.Left = new BSPNode(a);
            node.Right = new BSPNode(b);
        }

        SplitRecursive(node.Left, depth + 1);
        SplitRecursive(node.Right, depth + 1);
    }

    private void CreateRoomsInLeaves(BSPNode node)
    {
        if (node == null) return;

        if (node.IsLeaf)
        {
            int maxRoomW = node.Bounds.width - roomPadding * 2;
            int maxRoomH = node.Bounds.height - roomPadding * 2;

            if (maxRoomW < minRoomSize || maxRoomH < minRoomSize) return;

            int roomW = rng.Next(minRoomSize, maxRoomW + 1);
            int roomH = rng.Next(minRoomSize, maxRoomH + 1);

            int roomX = rng.Next(node.Bounds.xMin + roomPadding, node.Bounds.xMax - roomPadding - roomW + 1);
            int roomY = rng.Next(node.Bounds.yMin + roomPadding, node.Bounds.yMax - roomPadding - roomH + 1);

            RectInt room = new RectInt(roomX, roomY, roomW, roomH);
            node.Room = room;
            rooms.Add(room);
            return;
        }

        CreateRoomsInLeaves(node.Left);
        CreateRoomsInLeaves(node.Right);
    }

    private void ConnectRooms(BSPNode node)
    {
        if (node == null || node.IsLeaf) return;

        ConnectRooms(node.Left);
        ConnectRooms(node.Right);

        RectInt? leftRoom = GetAnyRoom(node.Left);
        RectInt? rightRoom = GetAnyRoom(node.Right);

        if (leftRoom.HasValue && rightRoom.HasValue)
        {
            Vector2Int a = GetRoomCenter(leftRoom.Value);
            Vector2Int b = GetRoomCenter(rightRoom.Value);
            CarveCorridor(a, b);
        }
    }

    private RectInt? GetAnyRoom(BSPNode node)
    {
        if (node == null) return null;

        if (node.IsLeaf)
            return node.Room;

        bool tryLeftFirst = rng.NextDouble() < 0.5;

        if (tryLeftFirst)
        {
            RectInt? left = GetAnyRoom(node.Left);
            if (left.HasValue) return left;
            return GetAnyRoom(node.Right);
        }
        else
        {
            RectInt? right = GetAnyRoom(node.Right);
            if (right.HasValue) return right;
            return GetAnyRoom(node.Left);
        }
    }

    private Vector2Int GetRoomCenter(RectInt room)
    {
        int cx = room.xMin + room.width / 2;
        int cy = room.yMin + room.height / 2;
        return new Vector2Int(cx, cy);
    }

    private void CarveCorridor(Vector2Int from, Vector2Int to)
    {
        bool horizontalFirst = rng.NextDouble() < 0.5;

        if (horizontalFirst)
        {
            CarveHorizontalLine(from.x, to.x, from.y);
            CarveVerticalLine(from.y, to.y, to.x);
        }
        else
        {
            CarveVerticalLine(from.y, to.y, from.x);
            CarveHorizontalLine(from.x, to.x, to.y);
        }
    }

    private void CarveHorizontalLine(int x1, int x2, int y)
    {
        int min = Mathf.Min(x1, x2);
        int max = Mathf.Max(x1, x2);

        for (int x = min; x <= max; x++)
        {
            if (InBounds(x, y))
                floorMap[x, y] = true;
        }
    }

    private void CarveVerticalLine(int y1, int y2, int x)
    {
        int min = Mathf.Min(y1, y2);
        int max = Mathf.Max(y1, y2);

        for (int y = min; y <= max; y++)
        {
            if (InBounds(x, y))
                floorMap[x, y] = true;
        }
    }

    private void CarveAllRooms()
    {
        foreach (RectInt room in rooms)
            CarveRect(room.xMin, room.yMin, room.width, room.height);
    }

    private void ClearTilemaps()
    {
        if (groundTilemap != null) groundTilemap.ClearAllTiles();
        if (contentTilemap != null) contentTilemap.ClearAllTiles();
    }

    private void CarveRect(int x, int y, int width, int height)
    {
        for (int ix = x; ix < x + width; ix++)
        {
            for (int iy = y; iy < y + height; iy++)
            {
                if (InBounds(ix, iy))
                    floorMap[ix, iy] = true;
            }
        }
    }

    private bool InBounds(int x, int y)
    {
        return x >= 0 && y >= 0 && x < mapWidth && y < mapHeight;
    }

    private void DrawFloorMap()
    {
        if (groundTilemap == null || floorTile == null) return;

        for (int x = 0; x < mapWidth; x++)
        {
            for (int y = 0; y < mapHeight; y++)
            {
                if (floorMap[x, y])
                    groundTilemap.SetTile(new Vector3Int(x, y, 0), floorTile);
            }
        }
    }

    private void AnalyzeLayout()
    {
        floorCells.Clear();
        deadEnds.Clear();

        distanceMap = new int[mapWidth, mapHeight];
        for (int x = 0; x < mapWidth; x++)
        {
            for (int y = 0; y < mapHeight; y++)
                distanceMap[x, y] = -1;
        }

        for (int x = 0; x < mapWidth; x++)
        {
            for (int y = 0; y < mapHeight; y++)
            {
                if (floorMap[x, y])
                    floorCells.Add(new Vector2Int(x, y));
            }
        }

        if (floorCells.Count == 0) return;

        RectInt startRoom = rooms[rng.Next(rooms.Count)];
        Vector2Int startCenter = GetRoomCenterCell(startRoom);

        RunBfsFrom(startCenter, out _);

        int bestDist = -1;
        Vector2Int bestEndCenter = startCenter;
        foreach (RectInt room in rooms)
        {
            Vector2Int center = GetRoomCenterCell(room);
            int d = InBounds(center.x, center.y) ? distanceMap[center.x, center.y] : -1;
            if (d > bestDist && center != startCenter)
            {
                bestDist = d;
                bestEndCenter = center;
            }
        }

        if (bestEndCenter == startCenter)
        {
            foreach (var n in GetFloorNeighbors4(startCenter))
            {
                bestEndCenter = n;
                break;
            }
        }

        startPos = startCenter;
        farthestPos = bestEndCenter;

        RunBfsFrom(startPos, out _);
        FindDeadEnds();
    }

    private void RunBfsFrom(Vector2Int origin, out Vector2Int farthest)
    {
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        distanceMap[origin.x, origin.y] = 0;
        queue.Enqueue(origin);

        farthest = origin;
        int farthestDist = 0;

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            int currentDist = distanceMap[current.x, current.y];

            if (currentDist > farthestDist)
            {
                farthestDist = currentDist;
                farthest = current;
            }

            foreach (Vector2Int n in GetFloorNeighbors4(current))
            {
                if (distanceMap[n.x, n.y] != -1) continue;
                distanceMap[n.x, n.y] = currentDist + 1;
                queue.Enqueue(n);
            }
        }
    }

    private void FindDeadEnds()
    {
        foreach (Vector2Int c in floorCells)
        {
            int openNeighbors = 0;
            foreach (Vector2Int _ in GetFloorNeighbors4(c))
                openNeighbors++;

            if (openNeighbors == 1)
                deadEnds.Add(c);
        }
    }

    private IEnumerable<Vector2Int> GetFloorNeighbors4(Vector2Int p)
    {
        Vector2Int[] dirs =
        {
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1)
        };

        foreach (Vector2Int d in dirs)
        {
            Vector2Int n = p + d;
            if (InBounds(n.x, n.y) && floorMap[n.x, n.y])
                yield return n;
        }
    }

    private void DrawContent()
    {
        if (contentTilemap == null)
        {
            return;
        }

        if (endTile != null)
        {
            contentTilemap.SetTile(new Vector3Int(farthestPos.x, farthestPos.y, 0), endTile);
        }

        if (challengeATile != null)
        {
            contentTilemap.SetTile(new Vector3Int(challengeAPos.x, challengeAPos.y, 0), challengeATile);
        }

        if (challengeBTile != null)
        {
            contentTilemap.SetTile(new Vector3Int(challengeBPos.x, challengeBPos.y, 0), challengeBTile);
        }

		if (rewardTile != null)
		{
			contentTilemap.SetTile(new Vector3Int(rewardPos.x, rewardPos.y, 0), rewardTile);
		}
        
        if (startTile != null)
        {
            contentTilemap.SetTile(new Vector3Int(startPos.x, startPos.y, 0), startTile);
        }

        Debug.Log($"Start:{startPos} tile?{startTile!=null}  End:{farthestPos}  A:{challengeAPos}  B:{challengeBPos}");
    }

    private void PlaceChallenges()
    {
        // fallbacks
        challengeAPos = startPos;
        challengeBPos = farthestPos;

        challengeAPos = FindCellClosestToTargetWithConstraints(
        Mathf.RoundToInt(distanceMap[farthestPos.x, farthestPos.y] * challengeATargetDistanceFactor),
        minChallengeADistanceFromStart,
        minChallengeADistanceFromEnd
        );

        Vector2Int? deadEndChoice = GetBestDeadEndForChallenge();
        if (deadEndChoice.HasValue)
        {
            challengeBPos = deadEndChoice.Value;
        }
        else
        {
            challengeBPos = FindFarFloorNotReserved();
        }
    }


    private Vector2Int? GetBestDeadEndForChallenge()
    {
		// distanceMap is from Start; compute distances from End locally
		var distFromEnd = ComputeDistanceMap(farthestPos);

		Vector2Int? best = null;
		int bestScore = int.MinValue;

        foreach (Vector2Int d in deadEnds)
        {
            if (IsReserved(d)) continue;

			int ds = distanceMap[d.x, d.y];   // distance from Start
			int de = distFromEnd[d.x, d.y];   // distance from End
			if (ds < 0 || de < 0) continue;

			// Enforce minimum distances from both ends
			if (ds < minChallengeBDistanceFromStart) continue;
			if (de < minChallengeBDistanceFromEnd) continue;

			// Score: farther from both Start and End
			int score = ds + de;

			if (score > bestScore)
            {
				bestScore = score;
                best = d;
            }
        }

		// Fallback if none qualifies: farthest free floor cell
		if (!best.HasValue)
			best = FindFarFloorNotReserved();

		return best;
    }

    private Vector2Int FindFarFloorNotReserved()
    {
        Vector2Int best = startPos;
        int bestDist = -1;

        foreach (Vector2Int c in floorCells)
        {
            if (IsReserved(c)) continue;

            int dist = distanceMap[c.x, c.y];
            if (dist > bestDist)
            {
                bestDist = dist;
                best = c;
            }
        }
        return best;
    }

    private bool IsReserved(Vector2Int p)
    {
		return p == startPos || p == farthestPos || p == challengeAPos || p == challengeBPos || p == rewardPos;
    }

	private void PlaceReward()
	{
		// Fallback: verst mogelijke vrije vloer
		rewardPos = FindFarFloorNotReserved();

		// Prefer: verste dead end die niet gereserveerd is
		Vector2Int? bestDeadEnd = GetBestDeadEndForReward();
		if (bestDeadEnd.HasValue)
		{
			rewardPos = bestDeadEnd.Value;
		}
	}

	private Vector2Int? GetBestDeadEndForReward()
	{
		// distanceMap is from Start; compute distances from End and from Challenge B locally
		var distFromEnd = ComputeDistanceMap(farthestPos);
		var distFromB = ComputeDistanceMap(challengeBPos);

		Vector2Int? best = null;
		int bestScore = int.MinValue;

		foreach (Vector2Int d in deadEnds)
		{
			if (IsReserved(d)) continue;

			int ds = distanceMap[d.x, d.y];   // distance from Start
			int de = distFromEnd[d.x, d.y];   // distance from End
			int db = distFromB[d.x, d.y];     // distance from Challenge B
			if (ds < 0 || de < 0 || db < 0) continue;

			// Enforce minimum distances
			if (ds < minRewardDistanceFromStart) continue;
			if (de < minRewardDistanceFromEnd) continue;
			if (db < minSeparationBetweenBAndReward) continue;

			int score = ds + de; // far from both ends

			if (score > bestScore)
			{
				bestScore = score;
				best = d;
			}
		}

		// Fallback if none qualifies
		if (!best.HasValue)
			best = FindFarFloorNotReserved();

		return best;
	}

	private int[,] ComputeDistanceMap(Vector2Int origin)
	{
		int[,] dist = new int[mapWidth, mapHeight];
		for (int x = 0; x < mapWidth; x++)
			for (int y = 0; y < mapHeight; y++)
				dist[x, y] = -1;

		if (!InBounds(origin.x, origin.y) || !floorMap[origin.x, origin.y])
			return dist;

		Queue<Vector2Int> q = new Queue<Vector2Int>();
		dist[origin.x, origin.y] = 0;
		q.Enqueue(origin);

		while (q.Count > 0)
		{
			var c = q.Dequeue();
			int cd = dist[c.x, c.y];
			foreach (var n in GetFloorNeighbors4(c))
			{
				if (dist[n.x, n.y] != -1) continue;
				dist[n.x, n.y] = cd + 1;
				q.Enqueue(n);
			}
		}

		return dist;
	}

    private void FitCameraToDungeon()
    {
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null) return;
        cam.orthographic = true;

        if (floorCells == null || floorCells.Count == 0) return;

        float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;

        foreach (var c in floorCells)
        {
            if (c.x < minX) minX = c.x;
            if (c.y < minY) minY = c.y;
            if (c.x > maxX) maxX = c.x;
            if (c.y > maxY) maxY = c.y;
        }

        float width = (maxX - minX + 1) + cameraPadding * 2f;
        float height = (maxY - minY + 1) + cameraPadding * 2f;

        float centerX = (minX + maxX + 1) * 0.5f;
        float centerY = (minY + maxY + 1) * 0.5f;

        float aspect = cam.aspect > 0 ? cam.aspect : 1f;
        float sizeByHeight = height * 0.5f;
        float sizeByWidth = (width * 0.5f) / aspect;
        cam.orthographicSize = Mathf.Max(sizeByHeight, sizeByWidth);

        Vector3 pos = new Vector3(centerX, centerY, cam.transform.position.z);
        if (pos.z >= 0f) pos.z = -10f;
        cam.transform.position = pos;
    }

    [ContextMenu("Randomize Seed + Generate")]
    private void RandomizeSeedAndGenerate()
    {
        seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        Generate();
    }

    public void RandomizeSeedAndGenerateButton()
    {
        seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        Generate();
    }

    private Vector2Int GetRoomCenterCell(RectInt room)
    {
        return new Vector2Int(room.xMin + room.width / 2, room.yMin + room.height / 2);
    }

    private Vector2Int FindCellClosestToTargetWithConstraints(int targetDistance, int minFromStart, int minFromEnd)
    {
        var distFromEnd = ComputeDistanceMap(farthestPos);

        Vector2Int best = startPos;
        int bestScore = int.MaxValue; 
        int bestTieBreak = int.MinValue; 

        foreach (var c in floorCells)
        {
            if (IsReserved(c)) continue;

            int ds = distanceMap[c.x, c.y];      // from Start
            int de = distFromEnd[c.x, c.y];      // from End
            if (ds < 0 || de < 0) continue;

            if (ds < minFromStart) continue;
            if (de < minFromEnd) continue;

            int score = Mathf.Abs(ds - targetDistance);
            int tieBreak = ds + de; // prefer cells far from both if equal score

            if (score < bestScore || (score == bestScore && tieBreak > bestTieBreak))
            {
                bestScore = score;
                bestTieBreak = tieBreak;
                best = c;
            }
        }

        // Fallback if nothing matched constraints
        if (best == startPos)
            best = FindFarFloorNotReserved();

        return best;
    }
}