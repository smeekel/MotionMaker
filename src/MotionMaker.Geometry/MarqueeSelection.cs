using MotionMaker.Core;

namespace MotionMaker.Geometry;

public static class MarqueeSelection
{
    public sealed class Hit
    {
        public List<Guid> Points { get; } = [];

        public List<Guid> Lines { get; } = [];

        public List<Guid> Circles { get; } = [];

        public List<Guid> Slots { get; } = [];
    }

    public static Hit Select(Sketch sketch, double x1, double y1, double x2, double y2)
        => Select(sketch, x1, y1, x2, y2, static (x, y) => (x, y));

    public static Hit Select(
        Sketch sketch,
        double x1,
        double y1,
        double x2,
        double y2,
        Func<double, double, (double X, double Y)> map)
    {
        var crossing = x2 < x1;
        var minX = Math.Min(x1, x2);
        var maxX = Math.Max(x1, x2);
        var minY = Math.Min(y1, y2);
        var maxY = Math.Max(y1, y2);
        var hit = new Hit();

        bool Inside(double x, double y) => x >= minX && x <= maxX && y >= minY && y <= maxY;

        foreach (var point in sketch.Points)
        {
            if (point.Construction)
            {
                continue;
            }

            var mapped = map(point.X, point.Y);
            if (Inside(mapped.X, mapped.Y))
            {
                hit.Points.Add(point.Id);
            }
        }

        foreach (var line in sketch.Lines)
        {
            var start = sketch.Points.FirstOrDefault(point => point.Id == line.StartId);
            var end = sketch.Points.FirstOrDefault(point => point.Id == line.EndId);
            if (start is null || end is null)
            {
                continue;
            }

            var a = map(start.X, start.Y);
            var b = map(end.X, end.Y);
            var selected = crossing
                ? Inside(a.X, a.Y) || Inside(b.X, b.Y) || SegmentCrosses(a.X, a.Y, b.X, b.Y, minX, minY, maxX, maxY)
                : Inside(a.X, a.Y) && Inside(b.X, b.Y);
            if (!selected)
            {
                continue;
            }

            if (line.SlotId is Guid slotId)
            {
                if (!hit.Slots.Contains(slotId))
                {
                    hit.Slots.Add(slotId);
                }
            }
            else
            {
                hit.Lines.Add(line.Id);
            }
        }

        foreach (var circle in sketch.Circles)
        {
            var center = sketch.Points.FirstOrDefault(point => point.Id == circle.CenterId);
            if (center is null)
            {
                continue;
            }

            var samples = new List<(double X, double Y)>();
            for (var step = 0; step < 16; step++)
            {
                var angle = step * Math.PI * 2 / 16;
                samples.Add(map(center.X + Math.Cos(angle) * circle.Radius, center.Y + Math.Sin(angle) * circle.Radius));
            }

            var selected = crossing
                ? samples.Any(sample => Inside(sample.X, sample.Y))
                : samples.All(sample => Inside(sample.X, sample.Y));
            if (selected)
            {
                hit.Circles.Add(circle.Id);
            }
        }

        foreach (var slot in sketch.Slots)
        {
            if (hit.Slots.Contains(slot.Id))
            {
                continue;
            }

            var junctions = new[] { slot.JunctionAId, slot.JunctionBId, slot.JunctionCId, slot.JunctionDId }
                .Select(id => sketch.Points.FirstOrDefault(point => point.Id == id))
                .Where(point => point is not null)
                .Cast<SketchPoint>()
                .Select(point => map(point.X, point.Y))
                .ToList();
            if (junctions.Count == 0)
            {
                continue;
            }

            var selected = crossing
                ? junctions.Any(point => Inside(point.X, point.Y))
                : junctions.All(point => Inside(point.X, point.Y));
            if (selected)
            {
                hit.Slots.Add(slot.Id);
            }
        }

        return hit;
    }

    private static bool SegmentCrosses(double x1, double y1, double x2, double y2, double minX, double minY, double maxX, double maxY)
    {
        if ((x1 < minX && x2 < minX) || (x1 > maxX && x2 > maxX) || (y1 < minY && y2 < minY) || (y1 > maxY && y2 > maxY))
        {
            return false;
        }

        return LiangBarsky(x1, y1, x2, y2, minX, minY, maxX, maxY);
    }

    private static bool LiangBarsky(double x1, double y1, double x2, double y2, double minX, double minY, double maxX, double maxY)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        double t0 = 0;
        double t1 = 1;
        if (!Clip(-dx, x1 - minX, ref t0, ref t1)
            || !Clip(dx, maxX - x1, ref t0, ref t1)
            || !Clip(-dy, y1 - minY, ref t0, ref t1)
            || !Clip(dy, maxY - y1, ref t0, ref t1))
        {
            return false;
        }

        return t1 >= t0;
    }

    private static bool Clip(double p, double q, ref double t0, ref double t1)
    {
        if (Math.Abs(p) < 1e-12)
        {
            return q >= 0;
        }

        var t = q / p;
        if (p < 0)
        {
            if (t > t1)
            {
                return false;
            }

            if (t > t0)
            {
                t0 = t;
            }
        }
        else
        {
            if (t < t0)
            {
                return false;
            }

            if (t < t1)
            {
                t1 = t;
            }
        }

        return true;
    }
}
