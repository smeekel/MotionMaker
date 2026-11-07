using MotionMaker.Core;

namespace MotionMaker.Geometry;

public static class MeshBoolean
{
    private const double Epsilon = 1e-5;

    public static MeshData Union(MeshData a, MeshData b) => Combine(a, b, subtract: false);

    public static MeshData Subtract(MeshData a, MeshData b) => Combine(a, b, subtract: true);

    private static MeshData Combine(MeshData a, MeshData b, bool subtract)
    {
        var left = new Node(Polygons(a));
        var right = new Node(Polygons(b));
        if (subtract)
        {
            left.Invert();
            left.ClipTo(right);
            right.ClipTo(left);
            right.Invert();
            right.ClipTo(left);
            right.Invert();
            left.Build(right.All());
            left.Invert();
        }
        else
        {
            left.ClipTo(right);
            right.ClipTo(left);
            right.Invert();
            right.ClipTo(left);
            right.Invert();
            left.Build(right.All());
        }

        var builder = new MeshBuilder();
        foreach (var polygon in left.All())
        {
            if (polygon.Vertices.Count < 3)
            {
                continue;
            }

            var origin = polygon.Vertices[0];
            for (var index = 1; index < polygon.Vertices.Count - 1; index++)
            {
                builder.Add(origin, polygon.Vertices[index], polygon.Vertices[index + 1], polygon.FaceId);
            }
        }

        return builder.Build();
    }

    private static List<Polygon> Polygons(MeshData mesh)
    {
        var polygons = new List<Polygon>();
        foreach (var triangle in MeshQuery.Triangles(mesh))
        {
            var polygon = new Polygon([triangle.A, triangle.B, triangle.C], triangle.Face);
            if (polygon.Plane is not null)
            {
                polygons.Add(polygon);
            }
        }

        return polygons;
    }

    private sealed class Polygon
    {
        public Polygon(List<Vec3> vertices, string faceId)
        {
            Vertices = vertices;
            FaceId = faceId;
            Plane = Plane.From(vertices);
        }

        public List<Vec3> Vertices { get; }

        public string FaceId { get; }

        public Plane? Plane { get; private set; }

        public void Flip()
        {
            Vertices.Reverse();
            Plane = Plane?.Flipped();
        }

        public Polygon Clone() => new(Vertices.ToList(), FaceId);
    }

    private sealed class Plane
    {
        private Plane(Vec3 normal, double w)
        {
            Normal = normal;
            W = w;
        }

        public Vec3 Normal { get; private set; }

        public double W { get; private set; }

        public static Plane? From(List<Vec3> vertices)
        {
            var normal = Vec3.Cross(vertices[1] - vertices[0], vertices[2] - vertices[0]);
            if (normal.Length < 1e-12)
            {
                return null;
            }

            normal = normal.Normalized();
            return new Plane(normal, Vec3.Dot(normal, vertices[0]));
        }

        public Plane Flipped() => new(Normal * -1, -W);

        public void Split(Polygon polygon, List<Polygon> coplanarFront, List<Polygon> coplanarBack, List<Polygon> front, List<Polygon> back)
        {
            const int Coplanar = 0;
            const int Front = 1;
            const int Back = 2;
            const int Spanning = 3;
            var types = new int[polygon.Vertices.Count];
            var polygonType = 0;
            for (var index = 0; index < polygon.Vertices.Count; index++)
            {
                var distance = Vec3.Dot(Normal, polygon.Vertices[index]) - W;
                var type = distance < -Epsilon ? Back : distance > Epsilon ? Front : Coplanar;
                polygonType |= type;
                types[index] = type;
            }

            switch (polygonType)
            {
                case Coplanar:
                    (Vec3.Dot(Normal, polygon.Plane?.Normal ?? Normal) > 0 ? coplanarFront : coplanarBack).Add(polygon);
                    break;
                case Front:
                    front.Add(polygon);
                    break;
                case Back:
                    back.Add(polygon);
                    break;
                default:
                    var frontVertices = new List<Vec3>();
                    var backVertices = new List<Vec3>();
                    for (var index = 0; index < polygon.Vertices.Count; index++)
                    {
                        var next = (index + 1) % polygon.Vertices.Count;
                        var type = types[index];
                        var nextType = types[next];
                        var current = polygon.Vertices[index];
                        var following = polygon.Vertices[next];
                        if (type != Back)
                        {
                            frontVertices.Add(current);
                        }

                        if (type != Front)
                        {
                            backVertices.Add(current);
                        }

                        if ((type | nextType) == Spanning)
                        {
                            var denominator = Vec3.Dot(Normal, following - current);
                            var t = Math.Abs(denominator) < 1e-12 ? 0.5 : (W - Vec3.Dot(Normal, current)) / denominator;
                            t = Math.Clamp(t, 0, 1);
                            var point = current + (following - current) * t;
                            frontVertices.Add(point);
                            backVertices.Add(point);
                        }
                    }

                    if (frontVertices.Count >= 3)
                    {
                        var piece = new Polygon(frontVertices, polygon.FaceId);
                        if (piece.Plane is not null)
                        {
                            front.Add(piece);
                        }
                    }

                    if (backVertices.Count >= 3)
                    {
                        var piece = new Polygon(backVertices, polygon.FaceId);
                        if (piece.Plane is not null)
                        {
                            back.Add(piece);
                        }
                    }

                    break;
            }
        }
    }

    private sealed class Node
    {
        private Plane? _plane;
        private readonly List<Polygon> _polygons = [];
        private Node? _front;
        private Node? _back;

        public Node(List<Polygon>? polygons = null)
        {
            if (polygons is { Count: > 0 })
            {
                Build(polygons);
            }
        }

        public void Invert()
        {
            foreach (var polygon in _polygons)
            {
                polygon.Flip();
            }

            _plane = _plane?.Flipped();
            _front?.Invert();
            _back?.Invert();
            (_front, _back) = (_back, _front);
        }

        public void ClipTo(Node other)
        {
            var clipped = other.Clip(_polygons);
            _polygons.Clear();
            _polygons.AddRange(clipped);
            _front?.ClipTo(other);
            _back?.ClipTo(other);
        }

        public void Build(List<Polygon> polygons)
        {
            if (polygons.Count == 0)
            {
                return;
            }

            _plane ??= polygons[0].Plane;
            if (_plane is null)
            {
                return;
            }

            var front = new List<Polygon>();
            var back = new List<Polygon>();
            foreach (var polygon in polygons)
            {
                _plane.Split(polygon, _polygons, _polygons, front, back);
            }

            if (front.Count > 0)
            {
                _front ??= new Node();
                _front.Build(front);
            }

            if (back.Count > 0)
            {
                _back ??= new Node();
                _back.Build(back);
            }
        }

        public List<Polygon> All()
        {
            var polygons = _polygons.Select(polygon => polygon.Clone()).ToList();
            if (_front is not null)
            {
                polygons.AddRange(_front.All());
            }

            if (_back is not null)
            {
                polygons.AddRange(_back.All());
            }

            return polygons;
        }

        private List<Polygon> Clip(List<Polygon> polygons)
        {
            if (_plane is null)
            {
                return polygons.ToList();
            }

            var front = new List<Polygon>();
            var back = new List<Polygon>();
            foreach (var polygon in polygons)
            {
                _plane.Split(polygon, front, back, front, back);
            }

            if (_front is not null)
            {
                front = _front.Clip(front);
            }

            back = _back is null ? [] : _back.Clip(back);
            front.AddRange(back);
            return front;
        }
    }
}
