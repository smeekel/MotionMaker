using MotionMaker.Core;
using TessVec = LibTessDotNet.Double.Vec3;

namespace MotionMaker.Geometry;

public static class FaceCarve
{
    public static bool TrySubtract(
        MeshData body,
        SketchPlane plane,
        string faceId,
        IReadOnlyList<ProfileLoop> tool,
        double depth,
        bool throughAll,
        string toolId,
        out MeshData result)
    {
        result = body;
        if (!MeshQuery.TryGetFace(body, faceId, out _))
        {
            return false;
        }

        var boundary = BoundaryLoops(body, faceId, plane);
        if (boundary.Count == 0)
        {
            return false;
        }

        var cutDepth = Math.Abs(depth);
        string? oppositeId = null;
        if (throughAll)
        {
            if (!TryOpposite(body, plane, faceId, out oppositeId, out cutDepth))
            {
                return false;
            }
        }

        var builder = new MeshBuilder();
        foreach (var triangle in MeshQuery.Triangles(body))
        {
            if (triangle.Face == faceId || triangle.Face == oppositeId)
            {
                continue;
            }

            builder.Add(triangle.A, triangle.B, triangle.C, triangle.Face);
        }

        var faceLoops = new List<ProfileLoop>(boundary);
        faceLoops.AddRange(tool);
        AddPlanarCap(builder, faceLoops, plane, 0, faceId);
        if (oppositeId is not null && MeshQuery.TryGetFace(body, oppositeId, out var opposite))
        {
            var oppositeBoundary = BoundaryLoops(body, oppositeId, opposite);
            var projected = Project(tool, plane, opposite, -cutDepth);
            oppositeBoundary.AddRange(projected);
            AddPlanarCap(builder, oppositeBoundary, opposite, 0, oppositeId);
        }
        else
        {
            AddPlanarCap(builder, tool, plane, -cutDepth, $"{toolId}:floor");
        }

        AddWalls(builder, tool, plane, 0, -cutDepth, toolId, cavity: true);
        result = builder.Build();
        return result.Indices.Count > 0 && MeshQuery.IsClosed(result);
    }

    public static bool TryUnion(
        MeshData body,
        SketchPlane plane,
        string faceId,
        IReadOnlyList<ProfileLoop> tool,
        double distance,
        string toolId,
        out MeshData result)
    {
        result = body;
        if (!MeshQuery.TryGetFace(body, faceId, out _) || distance <= 1e-6)
        {
            return false;
        }

        var boundary = BoundaryLoops(body, faceId, plane);
        if (boundary.Count == 0)
        {
            return false;
        }

        var builder = new MeshBuilder();
        foreach (var triangle in MeshQuery.Triangles(body))
        {
            if (triangle.Face == faceId)
            {
                continue;
            }

            builder.Add(triangle.A, triangle.B, triangle.C, triangle.Face);
        }

        var faceLoops = new List<ProfileLoop>(boundary);
        faceLoops.AddRange(tool);
        AddPlanarCap(builder, faceLoops, plane, 0, faceId);
        AddPlanarCap(builder, tool, plane, distance, $"{toolId}:top");
        AddWalls(builder, tool, plane, 0, distance, toolId, cavity: false);
        result = builder.Build();
        return result.Indices.Count > 0 && MeshQuery.IsClosed(result);
    }

    private static bool TryOpposite(MeshData body, SketchPlane plane, string faceId, out string oppositeId, out double depth)
    {
        oppositeId = "";
        depth = 0;
        var best = double.PositiveInfinity;
        foreach (var face in body.Faces)
        {
            if (face.Id == faceId)
            {
                continue;
            }

            var normal = new Vec3(face.NormalX, face.NormalY, face.NormalZ);
            if (Vec3.Dot(normal, plane.Normal) > -0.99)
            {
                continue;
            }

            var origin = new Vec3(face.OriginX, face.OriginY, face.OriginZ);
            var height = Vec3.Dot(origin - plane.Origin, plane.Normal);
            if (height < -1e-4 && -height < best)
            {
                best = -height;
                oppositeId = face.Id;
            }
        }

        if (string.IsNullOrEmpty(oppositeId))
        {
            return false;
        }

        depth = best;
        return true;
    }

    private static List<ProfileLoop> Project(IReadOnlyList<ProfileLoop> loops, SketchPlane from, SketchPlane to, double height)
    {
        var projected = new List<ProfileLoop>();
        foreach (var loop in loops)
        {
            var copy = new ProfileLoop { Hole = loop.Hole };
            foreach (var point in loop.Points)
            {
                var world = from.ToWorld(point.X, point.Y, height);
                to.ToLocal(world, out var u, out var v);
                copy.Points.Add((u, v));
            }

            projected.Add(copy);
        }

        return projected;
    }

    private static void AddWalls(MeshBuilder builder, IReadOnlyList<ProfileLoop> loops, SketchPlane plane, double from, double to, string toolId, bool cavity)
    {
        for (var loopIndex = 0; loopIndex < loops.Count; loopIndex++)
        {
            var points = loops[loopIndex].Points;
            for (var index = 0; index < points.Count; index++)
            {
                var current = points[index];
                var next = points[(index + 1) % points.Count];
                var dx = next.X - current.X;
                var dy = next.Y - current.Y;
                var outward = (plane.AxisU * dy + plane.AxisV * (-dx)) * (cavity ? -1 : 1);
                var face = $"{toolId}:wall:{loopIndex}:{index}";
                var a = plane.ToWorld(current.X, current.Y, from);
                var b = plane.ToWorld(next.X, next.Y, from);
                var c = plane.ToWorld(next.X, next.Y, to);
                var d = plane.ToWorld(current.X, current.Y, to);
                SolidBuilder.AddOriented(builder, a, b, c, outward, face);
                SolidBuilder.AddOriented(builder, a, c, d, outward, face);
            }
        }
    }

    private static void AddPlanarCap(MeshBuilder builder, IReadOnlyList<ProfileLoop> loops, SketchPlane plane, double height, string faceId)
    {
        var tess = new LibTessDotNet.Double.Tess();
        var added = false;
        foreach (var loop in loops)
        {
            if (loop.Points.Count < 3)
            {
                continue;
            }

            var contour = new LibTessDotNet.Double.ContourVertex[loop.Points.Count];
            for (var index = 0; index < loop.Points.Count; index++)
            {
                var point = loop.Points[index];
                contour[index] = new LibTessDotNet.Double.ContourVertex(new TessVec(point.X, point.Y, 0), null!);
            }

            tess.AddContour(contour, LibTessDotNet.Double.ContourOrientation.Original);
            added = true;
        }

        if (!added)
        {
            return;
        }

        tess.Tessellate(LibTessDotNet.Double.WindingRule.EvenOdd, LibTessDotNet.Double.ElementType.Polygons, 3, combineCallback: null, new TessVec(0, 0, 1));
        if (tess.Elements is null || tess.Vertices is null)
        {
            return;
        }

        var outward = height >= 0 ? plane.Normal : plane.Normal * -1;
        if (Math.Abs(height) < 1e-8)
        {
            outward = plane.Normal;
        }

        for (var index = 0; index + 2 < tess.Elements.Length; index += 3)
        {
            var a = tess.Vertices[tess.Elements[index]].Position;
            var b = tess.Vertices[tess.Elements[index + 1]].Position;
            var c = tess.Vertices[tess.Elements[index + 2]].Position;
            var worldA = plane.ToWorld(a.X, a.Y, height);
            var worldB = plane.ToWorld(b.X, b.Y, height);
            var worldC = plane.ToWorld(c.X, c.Y, height);
            SolidBuilder.AddOriented(builder, worldA, worldB, worldC, outward, faceId);
        }
    }

    private static List<ProfileLoop> BoundaryLoops(MeshData mesh, string faceId, SketchPlane plane)
    {
        var links = new Dictionary<string, List<(string To, double U, double V)>>();
        foreach (var triangle in MeshQuery.Triangles(mesh).Where(item => item.Face == faceId))
        {
            AddBoundary(links, triangle.A, triangle.B, plane);
            AddBoundary(links, triangle.B, triangle.C, plane);
            AddBoundary(links, triangle.C, triangle.A, plane);
        }

        var loops = new List<ProfileLoop>();
        var used = new HashSet<string>();
        foreach (var start in links.Keys.ToList())
        {
            if (!links.TryGetValue(start, out var options) || options.Count == 0)
            {
                continue;
            }

            var token = start + ">" + options[0].To;
            if (!used.Add(token))
            {
                continue;
            }

            var loop = new ProfileLoop();
            var cursor = start;
            var guard = 0;
            while (guard++ < 10000)
            {
                if (!links.TryGetValue(cursor, out var nexts) || nexts.Count == 0)
                {
                    break;
                }

                var next = nexts[0];
                nexts.RemoveAt(0);
                used.Add(cursor + ">" + next.To);
                loop.Points.Add((next.U, next.V));
                cursor = next.To;
                if (cursor == start)
                {
                    break;
                }
            }

            if (loop.Points.Count >= 3)
            {
                loops.Add(loop);
            }
        }

        Orient(loops);
        return loops;
    }

    private static void AddBoundary(Dictionary<string, List<(string To, double U, double V)>> links, Vec3 from, Vec3 to, SketchPlane plane)
    {
        var fromKey = Key(from);
        var toKey = Key(to);
        var reverse = fromKey + ">" + toKey;
        var forward = toKey + ">" + fromKey;
        if (links.TryGetValue(toKey, out var existing) && existing.RemoveAll(item => item.To == fromKey) > 0)
        {
            if (existing.Count == 0)
            {
                links.Remove(toKey);
            }

            return;
        }

        _ = reverse;
        _ = forward;
        plane.ToLocal(to, out var u, out var v);
        if (!links.TryGetValue(fromKey, out var list))
        {
            list = [];
            links.Add(fromKey, list);
        }

        list.Add((toKey, u, v));
    }

    private static void Orient(List<ProfileLoop> loops)
    {
        for (var index = 0; index < loops.Count; index++)
        {
            var centroid = (0d, 0d);
            foreach (var point in loops[index].Points)
            {
                centroid.Item1 += point.X;
                centroid.Item2 += point.Y;
            }

            centroid.Item1 /= loops[index].Points.Count;
            centroid.Item2 /= loops[index].Points.Count;
            var depth = 0;
            for (var other = 0; other < loops.Count; other++)
            {
                if (other != index && Contains(loops[other].Points, (centroid.Item1, centroid.Item2)))
                {
                    depth++;
                }
            }

            loops[index].Hole = depth % 2 == 1;
            double area = 0;
            var points = loops[index].Points;
            for (var point = 0; point < points.Count; point++)
            {
                var next = points[(point + 1) % points.Count];
                area += points[point].X * next.Y - next.X * points[point].Y;
            }

            var reverse = loops[index].Hole ? area > 0 : area < 0;
            if (reverse)
            {
                points.Reverse();
            }
        }
    }

    private static bool Contains(List<(double X, double Y)> polygon, (double X, double Y) point)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];
            if ((a.Y > point.Y) != (b.Y > point.Y)
                && point.X < (b.X - a.X) * (point.Y - a.Y) / ((b.Y - a.Y) + 1e-18) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static string Key(Vec3 point)
        => $"{(long)Math.Round(point.X * 10000)},{(long)Math.Round(point.Y * 10000)},{(long)Math.Round(point.Z * 10000)}";
}
