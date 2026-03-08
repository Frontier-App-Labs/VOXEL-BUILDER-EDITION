using Godot;
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

    public static Vector3I BuildToMicrovoxel(Vector3I buildUnitPosition)
    {
        return buildUnitPosition * GameConfig.MicrovoxelsPerBuildUnit;
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
}
