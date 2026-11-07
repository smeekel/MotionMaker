using MotionMaker.Core;

namespace MotionMaker.Geometry;

public sealed class ProfileLoop
{
    public List<(double X, double Y)> Points { get; } = [];

    public bool Hole { get; set; }
}

public static class ProfileBuilder
{
    public const string OpenProfileMessage = "The sketch needs a closed profile.";

    public static bool TryBuild(Sketch sketch, out List<ProfileLoop> loops, out string? error)
    {
        loops = [];
        error = null;
        foreach (var circle in sketch.Circles)
        {
            var center = sketch.Points.FirstOrDefault(point => point.Id == circle.CenterId);
            if (center is null || circle.Radius < 1e-6)
            {
                continue;
            }

            var loop = new ProfileLoop();
            const int steps = 48;
            for (var step = 0; step < steps; step++)
            {
                var angle = step * Math.PI * 2 / steps;
                loop.Points.Add((center.X + Math.Cos(angle) * circle.Radius, center.Y + Math.Sin(angle) * circle.Radius));
            }

            loops.Add(loop);
        }

        var pieces = new List<List<(double X, double Y)>>();
        foreach (var line in sketch.Lines)
        {
            var start = sketch.Points.FirstOrDefault(point => point.Id == line.StartId);
            var end = sketch.Points.FirstOrDefault(point => point.Id == line.EndId);
            if (start is null || end is null || Distance(start.X, start.Y, end.X, end.Y) < 1e-6)
            {
                continue;
            }

            pieces.Add([(start.X, start.Y), (end.X, end.Y)]);
        }

        foreach (var arc in sketch.Arcs)
        {
            var samples = SketchEditor.TessellateArc(sketch, arc);
            if (samples.Count >= 2)
            {
                pieces.Add(samples);
            }
        }

        var used = new bool[pieces.Count];
        for (var index = 0; index < pieces.Count; index++)
        {
            if (used[index])
            {
                continue;
            }

            var chain = new List<(double X, double Y)>(pieces[index]);
            used[index] = true;
            var progressed = true;
            while (progressed && !IsClosed(chain))
            {
                progressed = false;
                for (var other = 0; other < pieces.Count; other++)
                {
                    if (used[other])
                    {
                        continue;
                    }

                    var piece = pieces[other];
                    if (Near(chain[^1], piece[0]))
                    {
                        chain.AddRange(piece.Skip(1));
                        used[other] = true;
                        progressed = true;
                        break;
                    }

                    if (Near(chain[^1], piece[^1]))
                    {
                        chain.AddRange(piece.AsEnumerable().Reverse().Skip(1));
                        used[other] = true;
                        progressed = true;
                        break;
                    }
                }
            }

            if (!IsClosed(chain))
            {
                loops = [];
                error = OpenProfileMessage;
                return false;
            }

            if (Near(chain[0], chain[^1]))
            {
                chain.RemoveAt(chain.Count - 1);
            }

            if (chain.Count >= 3)
            {
                loops.Add(new ProfileLoop { Points = { } });
                loops[^1].Points.AddRange(chain);
            }
        }

        if (loops.Count == 0)
        {
            error = OpenProfileMessage;
            return false;
        }

        Orient(loops);
        return true;
    }

    public static double OutlineLength(IReadOnlyList<ProfileLoop> loops)
    {
        double length = 0;
        foreach (var loop in loops.Where(loop => !loop.Hole))
        {
            for (var index = 0; index < loop.Points.Count; index++)
            {
                var next = loop.Points[(index + 1) % loop.Points.Count];
                var point = loop.Points[index];
                length += Distance(point.X, point.Y, next.X, next.Y);
            }
        }

        return length;
    }

    private static void Orient(List<ProfileLoop> loops)
    {
        var centroids = loops.Select(Centroid).ToList();
        for (var index = 0; index < loops.Count; index++)
        {
            var depth = 0;
            for (var other = 0; other < loops.Count; other++)
            {
                if (other != index && Contains(loops[other].Points, centroids[index]))
                {
                    depth++;
                }
            }

            loops[index].Hole = depth % 2 == 1;
            var area = SignedArea(loops[index].Points);
            var shouldReverse = loops[index].Hole ? area > 0 : area < 0;
            if (shouldReverse)
            {
                loops[index].Points.Reverse();
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

    private static (double X, double Y) Centroid(ProfileLoop loop)
    {
        double x = 0;
        double y = 0;
        foreach (var point in loop.Points)
        {
            x += point.X;
            y += point.Y;
        }

        return (x / loop.Points.Count, y / loop.Points.Count);
    }

    private static double SignedArea(List<(double X, double Y)> points)
    {
        double area = 0;
        for (var index = 0; index < points.Count; index++)
        {
            var next = points[(index + 1) % points.Count];
            area += points[index].X * next.Y - next.X * points[index].Y;
        }

        return area / 2;
    }

    private static bool IsClosed(List<(double X, double Y)> chain)
        => chain.Count >= 4 && Near(chain[0], chain[^1]);

    private static bool Near((double X, double Y) a, (double X, double Y) b)
        => Distance(a.X, a.Y, b.X, b.Y) <= 1e-4;

    private static double Distance(double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
