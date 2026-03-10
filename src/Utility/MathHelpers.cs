using Godot;
using System;
using System.Collections.Generic;
using VoxelSiege.Core;

namespace VoxelSiege.Utility;

public static class MathHelpers
{
    public static int PositiveMod(int value, int mod)
    {
        int result = value % mod;
        return result < 0 ? result + mod : result;
    }

    public static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;
        if (remainder != 0 && ((remainder < 0) ^ (divisor < 0)))
        {
            quotient--;
        }

        return quotient;
    }

    public static Vector3I WorldToChunk(Vector3I microvoxelPosition)
    {
        return new Vector3I(
            FloorDiv(microvoxelPosition.X, GameConfig.ChunkSize),
            FloorDiv(microvoxelPosition.Y, GameConfig.ChunkSize),
            FloorDiv(microvoxelPosition.Z, GameConfig.ChunkSize));
    }

    public static Vector3I WorldToLocal(Vector3I microvoxelPosition)
    {
        return new Vector3I(
            PositiveMod(microvoxelPosition.X, GameConfig.ChunkSize),
            PositiveMod(microvoxelPosition.Y, GameConfig.ChunkSize),
            PositiveMod(microvoxelPosition.Z, GameConfig.ChunkSize));
    }

    public static int LocalToFlatIndex(Vector3I localPosition)
    {
        return localPosition.X + (GameConfig.ChunkSize * (localPosition.Y + (GameConfig.ChunkSize * localPosition.Z)));
    }

    public static Vector3 MicrovoxelToWorld(Vector3I microvoxelPosition)
    {
        float scale = GameConfig.MicrovoxelMeters;
        return new Vector3(microvoxelPosition.X * scale, microvoxelPosition.Y * scale, microvoxelPosition.Z * scale);
    }

    public static Vector3I WorldToMicrovoxel(Vector3 worldPosition)
    {
        float inv = 1.0f / GameConfig.MicrovoxelMeters;
        return new Vector3I(
            Mathf.RoundToInt(worldPosition.X * inv),
            Mathf.RoundToInt(worldPosition.Y * inv),
            Mathf.RoundToInt(worldPosition.Z * inv));
    }

    /// <summary>
    /// Converts a world position to a microvoxel position using FloorToInt.
    /// Unlike WorldToMicrovoxel (which rounds), this always returns the cell
    /// that *contains* the point. Use for ground detection and collision checks
    /// where boundary precision matters — RoundToInt can oscillate between
    /// adjacent cells at voxel boundaries, causing jitter.
    /// </summary>
    public static Vector3I WorldToMicrovoxelFloor(Vector3 worldPosition)
    {
        float inv = 1.0f / GameConfig.MicrovoxelMeters;
        return new Vector3I(
            Mathf.FloorToInt(worldPosition.X * inv),
            Mathf.FloorToInt(worldPosition.Y * inv),
            Mathf.FloorToInt(worldPosition.Z * inv));
    }

    public static Vector3I BuildToMicrovoxel(Vector3I buildUnitPosition)
    {
        return buildUnitPosition * GameConfig.MicrovoxelsPerBuildUnit;
    }

    /// <summary>
    /// Converts a microvoxel position to a build unit position (integer division, floors).
    /// </summary>
    public static Vector3I MicrovoxelToBuild(Vector3I microvoxelPosition)
    {
        return new Vector3I(
            FloorDiv(microvoxelPosition.X, GameConfig.MicrovoxelsPerBuildUnit),
            FloorDiv(microvoxelPosition.Y, GameConfig.MicrovoxelsPerBuildUnit),
            FloorDiv(microvoxelPosition.Z, GameConfig.MicrovoxelsPerBuildUnit));
    }

    public static IEnumerable<Vector3I> EnumerateSphere(Vector3I center, int radius)
    {
        int radiusSquared = radius * radius;
        for (int z = center.Z - radius; z <= center.Z + radius; z++)
        {
            for (int y = center.Y - radius; y <= center.Y + radius; y++)
            {
                for (int x = center.X - radius; x <= center.X + radius; x++)
                {
                    int dx = x - center.X;
                    int dy = y - center.Y;
                    int dz = z - center.Z;
                    int distanceSquared = (dx * dx) + (dy * dy) + (dz * dz);
                    if (distanceSquared <= radiusSquared)
                    {
                        yield return new Vector3I(x, y, z);
                    }
                }
            }
        }
    }

    public static Vector3[] SampleBallisticArc(Vector3 origin, Vector3 initialVelocity, float gravity, int steps, float stepTime)
    {
        Vector3[] points = new Vector3[steps + 1];
        for (int index = 0; index <= steps; index++)
        {
            float t = index * stepTime;
            Vector3 point = origin + (initialVelocity * t);
            point.Y -= 0.5f * gravity * t * t;
            points[index] = point;
        }

        return points;
    }

    /// <summary>
    /// Steps through a ray in voxel (microvoxel) space using DDA algorithm.
    /// Returns true if a solid voxel was hit within maxDistance world units.
    /// hitPos is the microvoxel position of the solid voxel.
    /// hitNormal is the face normal (which face of the solid voxel was entered from).
    /// placementPos is hitPos + hitNormal (where a new block would go).
    /// </summary>
    public static bool RaycastVoxel(
        Vector3 worldOrigin,
        Vector3 worldDirection,
        float maxDistance,
        Func<Vector3I, bool> isSolid,
        out Vector3I hitPos,
        out Vector3I hitNormal)
    {
        hitPos = Vector3I.Zero;
        hitNormal = Vector3I.Zero;

        if (worldDirection.LengthSquared() < 1e-10f)
        {
            return false;
        }

        worldDirection = worldDirection.Normalized();

        // Convert to microvoxel space
        float scale = GameConfig.MicrovoxelMeters;
        Vector3 origin = worldOrigin / scale;
        Vector3 direction = worldDirection; // direction stays the same, just step size changes
        float maxSteps = maxDistance / scale;

        // Current microvoxel cell
        int x = Mathf.FloorToInt(origin.X);
        int y = Mathf.FloorToInt(origin.Y);
        int z = Mathf.FloorToInt(origin.Z);

        // Step direction
        int stepX = direction.X >= 0 ? 1 : -1;
        int stepY = direction.Y >= 0 ? 1 : -1;
        int stepZ = direction.Z >= 0 ? 1 : -1;

        // tMax: distance along the ray to the next voxel boundary in each axis
        float tMaxX = direction.X != 0 ? ((direction.X > 0 ? (x + 1) - origin.X : x - origin.X) / direction.X) : float.MaxValue;
        float tMaxY = direction.Y != 0 ? ((direction.Y > 0 ? (y + 1) - origin.Y : y - origin.Y) / direction.Y) : float.MaxValue;
        float tMaxZ = direction.Z != 0 ? ((direction.Z > 0 ? (z + 1) - origin.Z : z - origin.Z) / direction.Z) : float.MaxValue;

        // tDelta: how far along the ray to cross one full voxel in each axis
        float tDeltaX = direction.X != 0 ? Math.Abs(1.0f / direction.X) : float.MaxValue;
        float tDeltaY = direction.Y != 0 ? Math.Abs(1.0f / direction.Y) : float.MaxValue;
        float tDeltaZ = direction.Z != 0 ? Math.Abs(1.0f / direction.Z) : float.MaxValue;

        float t = 0f;
        Vector3I lastNormal = Vector3I.Zero;

        // Step up to maxSteps voxel cells
        int maxIterations = (int)(maxSteps * 2) + 1;
        for (int i = 0; i < maxIterations; i++)
        {
            Vector3I current = new Vector3I(x, y, z);

            if (isSolid(current))
            {
                hitPos = current;
                hitNormal = lastNormal;
                return true;
            }

            // Advance to next voxel boundary
            if (tMaxX < tMaxY)
            {
                if (tMaxX < tMaxZ)
                {
                    t = tMaxX;
                    x += stepX;
                    tMaxX += tDeltaX;
                    lastNormal = new Vector3I(-stepX, 0, 0);
                }
                else
                {
                    t = tMaxZ;
                    z += stepZ;
                    tMaxZ += tDeltaZ;
                    lastNormal = new Vector3I(0, 0, -stepZ);
                }
            }
            else
            {
                if (tMaxY < tMaxZ)
                {
                    t = tMaxY;
                    y += stepY;
                    tMaxY += tDeltaY;
                    lastNormal = new Vector3I(0, -stepY, 0);
                }
                else
                {
                    t = tMaxZ;
                    z += stepZ;
                    tMaxZ += tDeltaZ;
                    lastNormal = new Vector3I(0, 0, -stepZ);
                }
            }

            if (t > maxSteps)
            {
                break;
            }
        }

        return false;
    }
}
