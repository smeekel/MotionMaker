using MotionMaker.Core;
using TessVec = LibTessDotNet.Double.Vec3;

namespace MotionMaker.Geometry;

public sealed class MeshBuilder
{
    private readonly List<double> _positions = [];
    private readonly List<int> _indices = [];
    private readonly List<string> _triangleFaces = [];

    public void Add(Vec3 a, Vec3 b, Vec3 c, string faceId)
    {
        var normal = Vec3.Cross(b - a, c - a);
        if (normal.Length < 1e-10)
        {
            return;
        }

        var ia = AddVertex(a);
        var ib = AddVertex(b);
        var ic = AddVertex(c);
        _indices.Add(ia);
        _indices.Add(ib);
        _indices.Add(ic);
        _triangleFaces.Add(faceId);
    }

    public MeshData Build()
    {
        var mesh = new MeshData
        {
            Positions = _positions,
            Indices = _indices,
            TriangleFaces = _triangleFaces
        };
        var groups = new Dictionary<string, List<int>>();
        for (var index = 0; index < _triangleFaces.Count; index++)
        {
            if (!groups.TryGetValue(_triangleFaces[index], out var list))
            {
                list = [];
                groups.Add(_triangleFaces[index], list);
            }

            list.Add(index);
        }

        foreach (var (id, triangles) in groups)
        {
            Vec3 normal = default;
            Vec3 origin = default;
            var count = 0;
            Vec3 axis = default;
            foreach (var triangle in triangles)
            {
                var a = Vertex(_indices[triangle * 3]);
                var b = Vertex(_indices[triangle * 3 + 1]);
                var c = Vertex(_indices[triangle * 3 + 2]);
                var area = Vec3.Cross(b - a, c - a);
                if (area.Length < 1e-10)
                {
                    continue;
                }

                normal += area;
                origin += (a + b + c) * (1.0 / 3.0);
                if (axis.Length < 1e-8)
                {
                    axis = b - a;
                }

                count++;
            }

            if (count == 0)
            {
                continue;
            }

            var plane = SketchPlane.FromAxes(origin * (1.0 / count), normal, axis);
            mesh.Faces.Add(new MeshFace
            {
                Id = id,
                OriginX = plane.OriginX,
                OriginY = plane.OriginY,
                OriginZ = plane.OriginZ,
                NormalX = plane.NormalX,
                NormalY = plane.NormalY,
                NormalZ = plane.NormalZ,
                AxisX = plane.AxisX,
                AxisY = plane.AxisY,
                AxisZ = plane.AxisZ
            });
        }

        return mesh;
    }

    private int AddVertex(Vec3 point)
    {
        _positions.Add(point.X);
        _positions.Add(point.Y);
        _positions.Add(point.Z);
        return _positions.Count / 3 - 1;
    }

    private Vec3 Vertex(int index) => new(_positions[index * 3], _positions[index * 3 + 1], _positions[index * 3 + 2]);
}

public static class SolidBuilder
{
    public static MeshData Extrude(
        IReadOnlyList<ProfileLoop> loops,
        SketchPlane plane,
        double distance,
        bool reversed,
        string featureKey)
    {
        var builder = new MeshBuilder();
        var sign = reversed ? -1 : 1;
        var height = Math.Abs(distance) * sign;
        AddCap(builder, loops, plane, 0, flip: sign > 0, $"{featureKey}:bottom");
        AddCap(builder, loops, plane, height, flip: sign < 0, $"{featureKey}:top");
        for (var loopIndex = 0; loopIndex < loops.Count; loopIndex++)
        {
            var points = loops[loopIndex].Points;
            for (var index = 0; index < points.Count; index++)
            {
                var current = points[index];
                var next = points[(index + 1) % points.Count];
                var dx = next.X - current.X;
                var dy = next.Y - current.Y;
                if (dx * dx + dy * dy < 1e-12)
                {
                    continue;
                }

                var outward = plane.AxisU * dy + plane.AxisV * (-dx);
                var bottomA = plane.ToWorld(current.X, current.Y, 0);
                var bottomB = plane.ToWorld(next.X, next.Y, 0);
                var topA = plane.ToWorld(current.X, current.Y, height);
                var topB = plane.ToWorld(next.X, next.Y, height);
                var face = $"{featureKey}:side:{loopIndex}:{index}";
                AddOriented(builder, bottomA, bottomB, topB, outward, face);
                AddOriented(builder, bottomA, topB, topA, outward, face);
            }
        }

        return builder.Build();
    }

    public static void AddOriented(MeshBuilder builder, Vec3 a, Vec3 b, Vec3 c, Vec3 outward, string faceId)
    {
        if (Vec3.Dot(Vec3.Cross(b - a, c - a), outward) < 0)
        {
            builder.Add(a, c, b, faceId);
        }
        else
        {
            builder.Add(a, b, c, faceId);
        }
    }

    private static void AddCap(
        MeshBuilder builder,
        IReadOnlyList<ProfileLoop> loops,
        SketchPlane plane,
        double height,
        bool flip,
        string faceId)
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
        var elements = tess.Elements;
        var vertices = tess.Vertices;
        if (elements is null || vertices is null)
        {
            return;
        }

        for (var index = 0; index + 2 < elements.Length; index += 3)
        {
            var a = vertices[elements[index]].Position;
            var b = vertices[elements[index + 1]].Position;
            var c = vertices[elements[index + 2]].Position;
            var worldA = plane.ToWorld(a.X, a.Y, height);
            var worldB = plane.ToWorld(b.X, b.Y, height);
            var worldC = plane.ToWorld(c.X, c.Y, height);
            if (flip)
            {
                builder.Add(worldA, worldC, worldB, faceId);
            }
            else
            {
                builder.Add(worldA, worldB, worldC, faceId);
            }
        }
    }
}
