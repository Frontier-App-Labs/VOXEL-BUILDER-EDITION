using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Voxel;

namespace VoxelSiege.Army;

public class TroopPathfinder
{
    private const int MaxDropScan = 10;

    private static readonly Vector3I[] HorizontalNeighbors =
    {
        new( 1, 0, 0),
        new(-1, 0, 0),
        new( 0, 0, 1),
        new( 0, 0,-1),
    };

    public List<Vector3I>? FindPath(
        VoxelWorld world,
        Vector3I start,
        Vector3I goal,
        Func<Vector3I, bool>? isDoorForOwner = null,
        int maxNodes = 5000)
    {
        if (start == goal)
            return new List<Vector3I> { start };

        if (!IsWalkable(world, start, isDoorForOwner) || !IsWalkable(world, goal, isDoorForOwner))
            return null;

        var openSet = new PriorityQueue();
        var cameFrom = new Dictionary<Vector3I, Vector3I>();
        var gScore = new Dictionary<Vector3I, int>();

        gScore[start] = 0;
        openSet.Enqueue(start, Heuristic(start, goal));

        int nodesExplored = 0;

        while (openSet.Count > 0)
        {
            Vector3I current = openSet.Dequeue();
            nodesExplored++;

            if (nodesExplored > maxNodes)
                return null;

            if (current == goal)
                return ReconstructPath(cameFrom, current);

            foreach (var (neighbor, moveCost) in GetNeighbors(world, current, isDoorForOwner))
            {
                int tentativeG = gScore[current] + moveCost;

                if (!gScore.TryGetValue(neighbor, out int existingG) || tentativeG < existingG)
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    int fScore = tentativeG + Heuristic(neighbor, goal);
                    openSet.Enqueue(neighbor, fScore);
                }
            }
        }

        return null;
    }

    private IEnumerable<(Vector3I Position, int Cost)> GetNeighbors(
        VoxelWorld world,
        Vector3I current,
        Func<Vector3I, bool>? isDoorForOwner)
    {
        foreach (Vector3I offset in HorizontalNeighbors)
        {
            Vector3I flat = new Vector3I(current.X + offset.X, current.Y, current.Z + offset.Z);

            // 1. Flat move: same Y level
            if (IsWalkable(world, flat, isDoorForOwner))
            {
                yield return (flat, 1);
                continue;
            }

            // 2. Step-up: check one voxel higher
            Vector3I up = new Vector3I(flat.X, flat.Y + 1, flat.Z);
            if (IsWalkable(world, up, isDoorForOwner))
            {
                // Also need clearance at current Y+2 to step up (headroom above current pos)
                Vector3I headroom = new Vector3I(current.X, current.Y + 2, current.Z);
                if (IsPassable(world, headroom, isDoorForOwner))
                {
                    yield return (up, 2);
                    continue;
                }
            }

            // 3. Step-down: scan downward for ground
            for (int drop = 1; drop <= MaxDropScan; drop++)
            {
                Vector3I down = new Vector3I(flat.X, flat.Y - drop, flat.Z);
                if (IsWalkable(world, down, isDoorForOwner))
                {
                    // Verify clearance for the entire drop column
                    bool clearPath = true;
                    for (int c = 0; c < drop; c++)
                    {
                        Vector3I check = new Vector3I(flat.X, flat.Y - c, flat.Z);
                        if (!IsPassable(world, check, isDoorForOwner))
                        {
                            clearPath = false;
                            break;
                        }
                    }

                    if (clearPath)
                    {
                        yield return (down, 1 + drop);
                    }

                    break; // Found ground, stop scanning
                }

                // If we hit solid that isn't walkable (no ground below), stop
                if (world.GetVoxel(down).IsSolid)
                    break;
            }
        }
    }

    private bool IsPassable(VoxelWorld world, Vector3I pos, Func<Vector3I, bool>? isDoorForOwner)
    {
        if (world.GetVoxel(pos).IsAir) return true;
        if (isDoorForOwner != null && isDoorForOwner(pos)) return true;
        return false;
    }

    public bool IsWalkable(VoxelWorld world, Vector3I pos, Func<Vector3I, bool>? isDoorForOwner = null)
    {
        return world.GetVoxel(new Vector3I(pos.X, pos.Y - 1, pos.Z)).IsSolid
            && IsPassable(world, pos, isDoorForOwner)
            && IsPassable(world, new Vector3I(pos.X, pos.Y + 1, pos.Z), isDoorForOwner);
    }

    private static int Heuristic(Vector3I a, Vector3I b)
    {
        return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z);
    }

    private static List<Vector3I> ReconstructPath(Dictionary<Vector3I, Vector3I> cameFrom, Vector3I current)
    {
        var path = new List<Vector3I> { current };
        while (cameFrom.TryGetValue(current, out Vector3I prev))
        {
            current = prev;
            path.Add(current);
        }
        path.Reverse();
        return path;
    }

    /// <summary>
    /// Min-heap priority queue for A* open set.
    /// </summary>
    private class PriorityQueue
    {
        private readonly List<(Vector3I Position, int Priority)> _heap = new();

        public int Count => _heap.Count;

        public void Enqueue(Vector3I position, int priority)
        {
            _heap.Add((position, priority));
            BubbleUp(_heap.Count - 1);
        }

        public Vector3I Dequeue()
        {
            var top = _heap[0];
            int last = _heap.Count - 1;
            _heap[0] = _heap[last];
            _heap.RemoveAt(last);
            if (_heap.Count > 0)
                BubbleDown(0);
            return top.Position;
        }

        private void BubbleUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (_heap[index].Priority >= _heap[parent].Priority)
                    break;
                (_heap[index], _heap[parent]) = (_heap[parent], _heap[index]);
                index = parent;
            }
        }

        private void BubbleDown(int index)
        {
            int count = _heap.Count;
            while (true)
            {
                int left = 2 * index + 1;
                int right = 2 * index + 2;
                int smallest = index;

                if (left < count && _heap[left].Priority < _heap[smallest].Priority)
                    smallest = left;
                if (right < count && _heap[right].Priority < _heap[smallest].Priority)
                    smallest = right;

                if (smallest == index)
                    break;

                (_heap[index], _heap[smallest]) = (_heap[smallest], _heap[index]);
                index = smallest;
            }
        }
    }
}
