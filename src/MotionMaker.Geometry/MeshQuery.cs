using MotionMaker.Core;

namespace MotionMaker.Geometry;

public static class MeshQuery
{
    public static double SignedVolume(MeshData mesh)
    {
        double volume = 0;
        for (var index = 0; index + 2 < mesh.Indices.Count; index += 3)
        {
            var a = At(mesh, mesh.Indices[index]);
            var b = At(mesh, mesh.Indices[index + 1]);
            var c = At(mesh, mesh.Indices[index + 2]);
            volume += Vec3.Dot(a, Vec3.Cross(b, c)) / 6.0;
        }

        return volume;
    }

    public static bool IsClosed(MeshData mesh)
    {
        var counts = new Dictionary<(long, long, long, long, long, long), int>();
        for (var index = 0; index + 2 < mesh.Indices.Count; index += 3)
        {
            var a = At(mesh, mesh.Indices[index]);
            var b = At(mesh, mesh.Indices[index + 1]);
            var c = At(mesh, mesh.Indices[index + 2]);
            if (Vec3.Cross(b - a, c - a).Length < 1e-10)
            {
                continue;
            }

            Count(counts, a, b);
            Count(counts, b, c);
            Count(counts, c, a);
        }

        return counts.Count > 0 && counts.Values.All(count => count == 2);
    }

    public static bool TryGetFace(MeshData? mesh, string faceId, out SketchPlane plane)
    {
        plane = SketchPlane.Base();
        var face = mesh?.Faces.FirstOrDefault(item => item.Id == faceId);
        if (face is null)
        {
            return false;
        }

        plane = SketchPlane.FromAxes(
            new Vec3(face.OriginX, face.OriginY, face.OriginZ),
            new Vec3(face.NormalX, face.NormalY, face.NormalZ),
            new Vec3(face.AxisX, face.AxisY, face.AxisZ));
        return true;
    }

    public static (Vec3 Min, Vec3 Max) Bounds(MeshData mesh)
    {
        var min = new Vec3(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        var max = new Vec3(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);
        for (var index = 0; index < mesh.Positions.Count; index += 3)
        {
            var point = new Vec3(mesh.Positions[index], mesh.Positions[index + 1], mesh.Positions[index + 2]);
            min = new Vec3(Math.Min(min.X, point.X), Math.Min(min.Y, point.Y), Math.Min(min.Z, point.Z));
            max = new Vec3(Math.Max(max.X, point.X), Math.Max(max.Y, point.Y), Math.Max(max.Z, point.Z));
        }

        return (min, max);
    }

    public static Vec3 Centroid(MeshData mesh)
    {
        if (mesh.Positions.Count < 3)
        {
            return default;
        }

        double x = 0;
        double y = 0;
        double z = 0;
        var count = mesh.Positions.Count / 3;
        for (var index = 0; index < mesh.Positions.Count; index += 3)
        {
            x += mesh.Positions[index];
            y += mesh.Positions[index + 1];
            z += mesh.Positions[index + 2];
        }

        return new Vec3(x / count, y / count, z / count);
    }

    public static IEnumerable<(Vec3 A, Vec3 B, Vec3 C, string Face)> Triangles(MeshData mesh)
    {
        for (var index = 0; index + 2 < mesh.Indices.Count; index += 3)
        {
            var face = index / 3 < mesh.TriangleFaces.Count ? mesh.TriangleFaces[index / 3] : "";
            yield return (
                At(mesh, mesh.Indices[index]),
                At(mesh, mesh.Indices[index + 1]),
                At(mesh, mesh.Indices[index + 2]),
                face);
        }
    }

    public static bool TryHit(MeshData mesh, Vec3 origin, Vec3 direction, out string faceId)
    {
        faceId = "";
        var best = double.PositiveInfinity;
        foreach (var triangle in Triangles(mesh))
        {
            if (RayTriangle(origin, direction, triangle.A, triangle.B, triangle.C, out var distance)
                && distance > 1e-4
                && distance < best
                && !string.IsNullOrEmpty(triangle.Face))
            {
                best = distance;
                faceId = triangle.Face;
            }
        }

        return faceId.Length > 0;
    }

    public static Vec3 At(MeshData mesh, int index)
        => new(mesh.Positions[index * 3], mesh.Positions[index * 3 + 1], mesh.Positions[index * 3 + 2]);

    private static bool RayTriangle(Vec3 origin, Vec3 direction, Vec3 a, Vec3 b, Vec3 c, out double distance)
    {
        distance = 0;
        var edge1 = b - a;
        var edge2 = c - a;
        var p = Vec3.Cross(direction, edge2);
        var determinant = Vec3.Dot(edge1, p);
        if (Math.Abs(determinant) < 1e-10)
        {
            return false;
        }

        var inverse = 1 / determinant;
        var t = origin - a;
        var u = Vec3.Dot(t, p) * inverse;
        if (u is < 0 or > 1)
        {
            return false;
        }

        var q = Vec3.Cross(t, edge1);
        var v = Vec3.Dot(direction, q) * inverse;
        if (v < 0 || u + v > 1)
        {
            return false;
        }

        distance = Vec3.Dot(edge2, q) * inverse;
        return distance > 0;
    }

    private static void Count(Dictionary<(long, long, long, long, long, long), int> counts, Vec3 a, Vec3 b)
    {
        var key = EdgeKey(a, b);
        counts[key] = counts.GetValueOrDefault(key) + 1;
    }

    private static (long, long, long, long, long, long) EdgeKey(Vec3 a, Vec3 b)
    {
        var aKey = (Quantize(a.X), Quantize(a.Y), Quantize(a.Z));
        var bKey = (Quantize(b.X), Quantize(b.Y), Quantize(b.Z));
        return Compare(aKey, bKey) <= 0
            ? (aKey.Item1, aKey.Item2, aKey.Item3, bKey.Item1, bKey.Item2, bKey.Item3)
            : (bKey.Item1, bKey.Item2, bKey.Item3, aKey.Item1, aKey.Item2, aKey.Item3);
    }

    private static int Compare((long, long, long) a, (long, long, long) b)
    {
        var compare = a.Item1.CompareTo(b.Item1);
        if (compare != 0)
        {
            return compare;
        }

        compare = a.Item2.CompareTo(b.Item2);
        return compare != 0 ? compare : a.Item3.CompareTo(b.Item3);
    }

    private static long Quantize(double value) => (long)Math.Round(value * 10000);
}
