using Godot;
using System.Collections.Generic;

namespace VoxelSiege.Art;

/// <summary>
/// Builds 3D meshes from voxel color arrays. Foundation for all procedural voxel art.
/// Each voxel is a small cube; faces between adjacent solid voxels are culled.
/// </summary>
public class VoxelModelBuilder
{
    private static readonly Vector3I[] FaceDirections =
    {
        Vector3I.Right,   // +X
        Vector3I.Left,    // -X
        Vector3I.Up,      // +Y
        Vector3I.Down,    // -Y
        Vector3I.Forward, // +Z  (Godot forward is -Z, but we use +Z here for back face)
        Vector3I.Back,    // -Z
    };

    // Each face has 4 vertices (as offsets from voxel origin) and a normal.
    // Vertices are wound clockwise when viewed from outside (Godot 4 front-face convention).
    private static readonly Vector3[][] FaceVertices =
    {
        // +X face
        new[] { new Vector3(1,0,0), new Vector3(1,1,0), new Vector3(1,1,1), new Vector3(1,0,1) },
        // -X face
        new[] { new Vector3(0,0,1), new Vector3(0,1,1), new Vector3(0,1,0), new Vector3(0,0,0) },
        // +Y face
        new[] { new Vector3(0,1,0), new Vector3(0,1,1), new Vector3(1,1,1), new Vector3(1,1,0) },
        // -Y face
        new[] { new Vector3(0,0,1), new Vector3(0,0,0), new Vector3(1,0,0), new Vector3(1,0,1) },
        // +Z face
        new[] { new Vector3(1,0,1), new Vector3(1,1,1), new Vector3(0,1,1), new Vector3(0,0,1) },
        // -Z face
        new[] { new Vector3(0,0,0), new Vector3(0,1,0), new Vector3(1,1,0), new Vector3(1,0,0) },
    };

    private static readonly Vector3[] FaceNormals =
    {
        Vector3.Right,    // +X
        Vector3.Left,     // -X
        Vector3.Up,       // +Y
        Vector3.Down,     // -Y
        Vector3.Back,     // +Z face outward normal is (0,0,+1) = Back in Godot
        Vector3.Forward,  // -Z face outward normal is (0,0,-1) = Forward in Godot
    };

    /// <summary>
    /// Size of each voxel cube in meters.
    /// </summary>
    public float VoxelSize { get; set; } = 0.1f;

    /// <summary>
    /// When greater than 0, adds a slight random offset to each voxel position for an organic look.
    /// Recommended range: 0.0 to 0.02.
    /// </summary>
    public float JitterAmount { get; set; } = 0.0f;

    /// <summary>
    /// Optional origin offset so the model can be centered (e.g., center-bottom).
    /// </summary>
    public Vector3 OriginOffset { get; set; } = Vector3.Zero;

    /// <summary>
    /// Build an ArrayMesh from a 3D voxel color array.
    /// Null entries are air; non-null entries are solid voxels of that color.
    /// The array is indexed [x, y, z].
    /// </summary>
    public ArrayMesh BuildMesh(Color?[,,] voxels)
    {
        int sizeX = voxels.GetLength(0);
        int sizeY = voxels.GetLength(1);
        int sizeZ = voxels.GetLength(2);

        List<Vector3> vertices = new();
        List<Vector3> normals = new();
        List<Color> colors = new();
        List<int> indices = new();

        RandomNumberGenerator rng = new();
        if (JitterAmount > 0f)
        {
            rng.Seed = 42; // deterministic jitter
        }

        for (int x = 0; x < sizeX; x++)
        {
            for (int y = 0; y < sizeY; y++)
            {
                for (int z = 0; z < sizeZ; z++)
                {
                    Color? voxelColor = voxels[x, y, z];
                    if (voxelColor == null)
                    {
                        continue;
                    }

                    Vector3 basePos = new Vector3(x, y, z) * VoxelSize + OriginOffset;

                    if (JitterAmount > 0f)
                    {
                        basePos += new Vector3(
                            rng.RandfRange(-JitterAmount, JitterAmount),
                            rng.RandfRange(-JitterAmount, JitterAmount),
                            rng.RandfRange(-JitterAmount, JitterAmount)
                        );
                    }

                    for (int face = 0; face < 6; face++)
                    {
                        Vector3I dir = FaceDirections[face];
                        int nx = x + dir.X;
                        int ny = y + dir.Y;
                        int nz = z + dir.Z;

                        // Only emit face if neighbor is air or out of bounds
                        if (nx >= 0 && nx < sizeX && ny >= 0 && ny < sizeY && nz >= 0 && nz < sizeZ
                            && voxels[nx, ny, nz] != null)
                        {
                            continue;
                        }

                        int baseIndex = vertices.Count;
                        Vector3 normal = FaceNormals[face];
                        Vector3[] fv = FaceVertices[face];

                        for (int v = 0; v < 4; v++)
                        {
                            vertices.Add(basePos + fv[v] * VoxelSize);
                            normals.Add(normal);
                            colors.Add(voxelColor.Value);
                        }

                        // Two triangles per quad — clockwise winding for Godot 4 front faces
                        indices.Add(baseIndex);
                        indices.Add(baseIndex + 2);
                        indices.Add(baseIndex + 1);
                        indices.Add(baseIndex);
                        indices.Add(baseIndex + 3);
                        indices.Add(baseIndex + 2);
                    }
                }
            }
        }

        ArrayMesh mesh = new();
        if (vertices.Count == 0)
        {
            return mesh;
        }

        Godot.Collections.Array arrays = new();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();

        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>
    /// Build a mesh from a sub-region of a voxel array (for body part extraction).
    /// </summary>
    public ArrayMesh BuildMeshRegion(Color?[,,] voxels, Vector3I regionMin, Vector3I regionMax)
    {
        int rx = regionMax.X - regionMin.X;
        int ry = regionMax.Y - regionMin.Y;
        int rz = regionMax.Z - regionMin.Z;

        Color?[,,] region = new Color?[rx, ry, rz];
        for (int x = 0; x < rx; x++)
        {
            for (int y = 0; y < ry; y++)
            {
                for (int z = 0; z < rz; z++)
                {
                    int sx = regionMin.X + x;
                    int sy = regionMin.Y + y;
                    int sz = regionMin.Z + z;
                    if (sx < voxels.GetLength(0) && sy < voxels.GetLength(1) && sz < voxels.GetLength(2))
                    {
                        region[x, y, z] = voxels[sx, sy, sz];
                    }
                }
            }
        }

        // Offset so the region mesh is positioned relative to the original model origin
        Vector3 savedOffset = OriginOffset;
        OriginOffset += new Vector3(regionMin.X, regionMin.Y, regionMin.Z) * VoxelSize;
        ArrayMesh mesh = BuildMesh(region);
        OriginOffset = savedOffset;
        return mesh;
    }

    /// <summary>
    /// Create a StandardMaterial3D suitable for voxel art with vertex colors.
    /// </summary>
    public static StandardMaterial3D CreateVoxelMaterial(float metallic = 0.0f, float roughness = 0.8f)
    {
        StandardMaterial3D mat = new();
        mat.VertexColorUseAsAlbedo = true;
        mat.Metallic = metallic;
        mat.Roughness = roughness;
        return mat;
    }

    /// <summary>
    /// Create a ShaderMaterial using the commander_toon shader with vertex color support.
    /// </summary>
    public static ShaderMaterial? CreateToonMaterial()
    {
        Shader? shader = GD.Load<Shader>("res://assets/shaders/commander_toon.gdshader");
        if (shader == null)
        {
            return null;
        }

        ShaderMaterial mat = new();
        mat.Shader = shader;
        mat.SetShaderParameter("tint", Colors.White);
        return mat;
    }

    /// <summary>
    /// Build a BoxShape3D collision shape for a body part region.
    /// </summary>
    public BoxShape3D BuildCollisionBox(Vector3I regionMin, Vector3I regionMax)
    {
        Vector3 size = new Vector3(
            (regionMax.X - regionMin.X) * VoxelSize,
            (regionMax.Y - regionMin.Y) * VoxelSize,
            (regionMax.Z - regionMin.Z) * VoxelSize
        );
        return new BoxShape3D { Size = size };
    }

    /// <summary>
    /// Get the center position of a region in local space.
    /// </summary>
    public Vector3 GetRegionCenter(Vector3I regionMin, Vector3I regionMax)
    {
        return OriginOffset + new Vector3(
            (regionMin.X + regionMax.X) * 0.5f * VoxelSize,
            (regionMin.Y + regionMax.Y) * 0.5f * VoxelSize,
            (regionMin.Z + regionMax.Z) * 0.5f * VoxelSize
        );
    }
}
