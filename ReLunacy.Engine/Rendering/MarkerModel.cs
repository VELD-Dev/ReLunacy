using System.Globalization;
using System.Numerics;

namespace ReLunacy.Engine.Rendering;

/// <summary>
/// A small editor model (the camera marker): a Wavefront OBJ with per-vertex colours ("v x y z r g b",
/// as Blender writes them), flattened to one vertex per face corner so each corner keeps its face's normal.
/// </summary>
public sealed class MarkerModel
{
    public required Vector3[] Positions { get; init; }
    public required Vector3[] Normals { get; init; }
    public required Vector3[] Colours { get; init; }
    public required uint[] Indices { get; init; }

    public static MarkerModel LoadObj(string path)
    {
        var positions = new List<Vector3>();
        var colours = new List<Vector3>();
        var normals = new List<Vector3>();
        var outPositions = new List<Vector3>();
        var outNormals = new List<Vector3>();
        var outColours = new List<Vector3>();
        static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        foreach (string raw in File.ReadLines(path))
        {
            var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            switch (parts[0])
            {
                case "v":
                    positions.Add(new Vector3(F(parts[1]), F(parts[2]), F(parts[3])));
                    colours.Add(parts.Length >= 7 ? new Vector3(F(parts[4]), F(parts[5]), F(parts[6])) : Vector3.One);
                    break;
                case "vn":
                    normals.Add(new Vector3(F(parts[1]), F(parts[2]), F(parts[3])));
                    break;
                case "f":
                    // A fan over the face's corners; "v", "v/vt", "v//vn" and "v/vt/vn" all read.
                    var corners = parts.Skip(1).Select(c => c.Split('/')).ToList();
                    for (int i = 1; i + 1 < corners.Count; i++)
                        foreach (var corner in new[] { corners[0], corners[i], corners[i + 1] })
                        {
                            int v = int.Parse(corner[0]) - 1;
                            outPositions.Add(positions[v]);
                            outColours.Add(colours[v]);
                            outNormals.Add(corner.Length >= 3 && corner[2].Length > 0 ? normals[int.Parse(corner[2]) - 1] : Vector3.UnitY);
                        }
                    break;
            }
        }
        return new MarkerModel
        {
            Positions = [.. outPositions],
            Normals = [.. outNormals],
            Colours = [.. outColours],
            Indices = [.. Enumerable.Range(0, outPositions.Count).Select(i => (uint)i)],
        };
    }
}
