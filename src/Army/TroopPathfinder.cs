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
        // Diagonals for smoother terrain navigation
        new( 1, 0, 1),
        new( 1, 0,-1),
        new(-1, 0, 1),
        new(-1, 0,-1),
    };

    public List<Vector3I>? FindPath(
        VoxelWorld world,
        Vector3I start,
        Vector3I goal,
        Func<Vector3I, bool>? isDoorForOwner = null,
        int maxNodes = 15000)
    {
        if (start == goal)
            return new List<Vector3I> { start };

        if (!IsWalkable(world, start, isDoorForOwner))
            return null;

        // If the exact goal isn't walkable, allow pathfinding to the closest
        // reachable position (within AttackRange of the goal)
        bool goalWalkable = IsWalkable(world, goal, isDoorForOwner);

        var openSet = new PriorityQueue();
        var cameFrom = new Dictionary<Vector3I, Vector3I>();
        var gScore = new Dictionary<Vector3I, int>();

        gScore[start] = 0;
        openSet.Enqueue(start, Heuristic(start, goal));

        int nodesExplored = 0;
        // Track the closest node to goal in case we can't reach it exactly
        Vector3I closestNode = start;
        int closestDist = Heuristic(start, goal);

        while (openSet.Count > 0)
        {
            Vector3I current = openSet.Dequeue();
            nodesExplored++;

            if (nodesExplored > maxNodes)
            {
                // Return path to closest reachable node if we got reasonably close
                if (closestDist <= 50 && closestNode != start)
                    return ReconstructPath(cameFrom, closestNode);
                return null;
            }

            if (current == goal)
                return ReconstructPath(cameFrom, current);

            // If the exact goal isn't walkable, accept being adjacent to it
            if (!goalWalkable && Heuristic(current, goal) <= 20)
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

                    // Track closest approach to goal
                    int h = Heuristic(neighbor, goal);
                    if (h < closestDist)
                    {
                        closestDist = h;
                        closestNode = neighbor;
                    }
                }
            }
        }

        // Exhausted search — return path to closest node if reasonably close
        if (closestDist <= 50 && closestNode != start)
            return ReconstructPath(cameFrom, closestNode);

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
            bool isDiagonal = offset.X != 0 && offset.Z != 0;
            int baseCost = isDiagonal ? 14 : 10; // 14 ~= 10 * sqrt(2), scaled by 10 for integer math

            // 1. Flat move: same Y level
            if (IsWalkable(world, flat, isDoorForOwner))
            {
                yield return (flat, baseCost);
                continue;
            }

            // Skip step-up/step-down for diagonal moves (troops can only step up/down cardinally)
            if (isDiagonal)
                continue;

            // 2. Step-up: check up to 2 voxels higher (troops can jump 2 blocks)
            for (int stepUp = 1; stepUp <= 2; stepUp++)
            {
                Vector3I up = new Vector3I(flat.X, flat.Y + stepUp, flat.Z);
                if (IsWalkable(world, up, isDoorForOwner))
                {
                    // Need clearance above current position for the full step-up height,
                    // plus 2 blocks of headroom at the destination
                    bool hasClearance = true;
                    for (int c = 2; c <= stepUp + 1; c++)
                    {
                        Vector3I headroom = new Vector3I(current.X, current.Y + c, current.Z);
                        if (!IsPassable(world, headroom, isDoorForOwner))
                        {
                            hasClearance = false;
                            break;
                        }
                    }

                    if (hasClearance)
                    {
                        yield return (up, 10 + stepUp * 10); // step-up costs scale with height
                        break; // found a valid step-up, don't check higher
                    }
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
                        yield return (down, 10 + drop * 10);
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

    /// <summary>
    /// Octile distance heuristic scaled to match movement costs (cardinal=10, diagonal=14).
    /// Admissible and consistent for the grid with diagonal movement.
    /// </summary>
    private static int Heuristic(Vector3I a, Vector3I b)
    {
        int dx = Math.Abs(a.X - b.X);
        int dy = Math.Abs(a.Y - b.Y);
        int dz = Math.Abs(a.Z - b.Z);
        // Octile distance on XZ plane + vertical component
        int minXZ = Math.Min(dx, dz);
        int maxXZ = Math.Max(dx, dz);
        return 14 * minXZ + 10 * (maxXZ - minXZ) + 10 * dy;
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
