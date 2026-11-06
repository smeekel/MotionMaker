using MotionMaker.Core;

namespace MotionMaker.Geometry;

public enum SketchHitKind
{
    Point,
    Line,
    Circle
}

public readonly record struct SketchHit(SketchHitKind Kind, Guid Id);

public static class SketchEditor
{
    public static SketchLine? AddLine(Sketch sketch, double x1, double y1, double x2, double y2)
    {
        if (Hypot(x2 - x1, y2 - y1) < 1e-6)
        {
            return null;
        }

        var start = AddPoint(sketch, x1, y1);
        var end = AddPoint(sketch, x2, y2);
        var line = new SketchLine { StartId = start.Id, EndId = end.Id };
        sketch.Lines.Add(line);
        return line;
    }

    public static bool AddRectangle(Sketch sketch, double x1, double y1, double x2, double y2)
    {
        var minX = Math.Min(x1, x2);
        var maxX = Math.Max(x1, x2);
        var minY = Math.Min(y1, y2);
        var maxY = Math.Max(y1, y2);
        if (maxX - minX < 1e-6 || maxY - minY < 1e-6)
        {
            return false;
        }

        var bottomLeft = AddPoint(sketch, minX, minY);
        var bottomRight = AddPoint(sketch, maxX, minY);
        var topRight = AddPoint(sketch, maxX, maxY);
        var topLeft = AddPoint(sketch, minX, maxY);
        var bottom = AddLineBetween(sketch, bottomLeft, bottomRight);
        var right = AddLineBetween(sketch, bottomRight, topRight);
        var top = AddLineBetween(sketch, topRight, topLeft);
        var left = AddLineBetween(sketch, topLeft, bottomLeft);
        AddUnique(sketch, ConstraintKind.Horizontal, bottom.Id);
        AddUnique(sketch, ConstraintKind.Vertical, right.Id);
        AddUnique(sketch, ConstraintKind.Horizontal, top.Id);
        AddUnique(sketch, ConstraintKind.Vertical, left.Id);
        return true;
    }

    public static SketchCircle? AddCircle(Sketch sketch, double centerX, double centerY, double radius)
    {
        if (radius < 1e-6)
        {
            return null;
        }

        var center = AddPoint(sketch, centerX, centerY);
        var circle = new SketchCircle { CenterId = center.Id, Radius = radius };
        sketch.Circles.Add(circle);
        sketch.Constraints.Add(new SketchConstraint
        {
            Kind = ConstraintKind.Radius,
            CircleId = circle.Id,
            Value = radius
        });
        return circle;
    }

    public static bool AddHorizontal(Sketch sketch, Guid lineId)
        => sketch.Lines.Any(line => line.Id == lineId) && AddUnique(sketch, ConstraintKind.Horizontal, lineId);

    public static bool AddVertical(Sketch sketch, Guid lineId)
        => sketch.Lines.Any(line => line.Id == lineId) && AddUnique(sketch, ConstraintKind.Vertical, lineId);

    public static bool AddCoincident(Sketch sketch, Guid pointA, Guid pointB)
    {
        if (pointA == pointB)
        {
            return false;
        }

        if (sketch.Points.All(point => point.Id != pointA) || sketch.Points.All(point => point.Id != pointB))
        {
            return false;
        }

        if (sketch.Constraints.Any(constraint =>
                constraint.Kind == ConstraintKind.Coincident
                && ((constraint.PointAId == pointA && constraint.PointBId == pointB)
                    || (constraint.PointAId == pointB && constraint.PointBId == pointA))))
        {
            return false;
        }

        sketch.Constraints.Add(new SketchConstraint
        {
            Kind = ConstraintKind.Coincident,
            PointAId = pointA,
            PointBId = pointB
        });
        return true;
    }

    public static bool AddLength(Sketch sketch, Guid lineId, double length)
    {
        var line = sketch.Lines.FirstOrDefault(item => item.Id == lineId);
        if (line is null || length <= 1e-6)
        {
            return false;
        }

        var existing = sketch.Constraints.FirstOrDefault(constraint =>
            constraint.Kind == ConstraintKind.Length && constraint.LineId == lineId);
        if (existing is not null)
        {
            existing.Value = length;
            return true;
        }

        sketch.Constraints.Add(new SketchConstraint
        {
            Kind = ConstraintKind.Length,
            LineId = lineId,
            Value = length
        });
        return true;
    }

    public static bool AddRadius(Sketch sketch, Guid circleId, double radius)
    {
        var circle = sketch.Circles.FirstOrDefault(item => item.Id == circleId);
        if (circle is null || radius <= 1e-6)
        {
            return false;
        }

        var existing = sketch.Constraints.FirstOrDefault(constraint =>
            constraint.Kind == ConstraintKind.Radius && constraint.CircleId == circleId);
        if (existing is not null)
        {
            existing.Value = radius;
            return true;
        }

        sketch.Constraints.Add(new SketchConstraint
        {
            Kind = ConstraintKind.Radius,
            CircleId = circleId,
            Value = radius
        });
        return true;
    }

    public static SketchHit? HitTest(Sketch sketch, double x, double y, double tolerance)
    {
        tolerance = Math.Max(tolerance, 1e-6);
        SketchPoint? closestPoint = null;
        var closestPointDistance = tolerance;
        foreach (var point in sketch.Points)
        {
            var distance = Hypot(point.X - x, point.Y - y);
            if (distance <= closestPointDistance)
            {
                closestPoint = point;
                closestPointDistance = distance;
            }
        }

        if (closestPoint is not null)
        {
            return new SketchHit(SketchHitKind.Point, closestPoint.Id);
        }

        SketchLine? closestLine = null;
        var closestLineDistance = tolerance;
        foreach (var line in sketch.Lines)
        {
            var start = sketch.Points.FirstOrDefault(point => point.Id == line.StartId);
            var end = sketch.Points.FirstOrDefault(point => point.Id == line.EndId);
            if (start is null || end is null)
            {
                continue;
            }

            var distance = DistanceToSegment(x, y, start.X, start.Y, end.X, end.Y);
            if (distance <= closestLineDistance)
            {
                closestLine = line;
                closestLineDistance = distance;
            }
        }

        if (closestLine is not null)
        {
            return new SketchHit(SketchHitKind.Line, closestLine.Id);
        }

        SketchCircle? closestCircle = null;
        var closestCircleDistance = tolerance;
        foreach (var circle in sketch.Circles)
        {
            var center = sketch.Points.FirstOrDefault(point => point.Id == circle.CenterId);
            if (center is null)
            {
                continue;
            }

            var distance = Math.Abs(Hypot(center.X - x, center.Y - y) - circle.Radius);
            if (distance <= closestCircleDistance)
            {
                closestCircle = circle;
                closestCircleDistance = distance;
            }
        }

        return closestCircle is null ? null : new SketchHit(SketchHitKind.Circle, closestCircle.Id);
    }

    public static void Delete(Sketch sketch, SketchHit hit)
    {
        switch (hit.Kind)
        {
            case SketchHitKind.Point:
                DeletePoint(sketch, hit.Id);
                break;
            case SketchHitKind.Line:
                sketch.Lines.RemoveAll(line => line.Id == hit.Id);
                sketch.Constraints.RemoveAll(constraint => constraint.LineId == hit.Id);
                RemoveOrphanPoints(sketch);
                break;
            case SketchHitKind.Circle:
                sketch.Circles.RemoveAll(circle => circle.Id == hit.Id);
                sketch.Constraints.RemoveAll(constraint => constraint.CircleId == hit.Id);
                RemoveOrphanPoints(sketch);
                break;
        }
    }

    public static void DeleteConstraint(Sketch sketch, Guid constraintId)
        => sketch.Constraints.RemoveAll(constraint => constraint.Id == constraintId);

    public static double LineLength(Sketch sketch, SketchLine line)
    {
        var start = sketch.Points.First(point => point.Id == line.StartId);
        var end = sketch.Points.First(point => point.Id == line.EndId);
        return Hypot(end.X - start.X, end.Y - start.Y);
    }

    private static void DeletePoint(Sketch sketch, Guid pointId)
    {
        var lineIds = sketch.Lines.Where(line => line.StartId == pointId || line.EndId == pointId).Select(line => line.Id).ToHashSet();
        var circleIds = sketch.Circles.Where(circle => circle.CenterId == pointId).Select(circle => circle.Id).ToHashSet();
        sketch.Points.RemoveAll(point => point.Id == pointId);
        sketch.Lines.RemoveAll(line => lineIds.Contains(line.Id));
        sketch.Circles.RemoveAll(circle => circleIds.Contains(circle.Id));
        sketch.Constraints.RemoveAll(constraint =>
            constraint.PointAId == pointId
            || constraint.PointBId == pointId
            || (constraint.LineId is Guid lineId && lineIds.Contains(lineId))
            || (constraint.CircleId is Guid circleId && circleIds.Contains(circleId)));
        RemoveOrphanPoints(sketch);
    }

    private static void RemoveOrphanPoints(Sketch sketch)
    {
        var used = new HashSet<Guid>();
        foreach (var line in sketch.Lines)
        {
            used.Add(line.StartId);
            used.Add(line.EndId);
        }

        foreach (var circle in sketch.Circles)
        {
            used.Add(circle.CenterId);
        }

        foreach (var constraint in sketch.Constraints)
        {
            if (constraint.PointAId is Guid a)
            {
                used.Add(a);
            }

            if (constraint.PointBId is Guid b)
            {
                used.Add(b);
            }
        }

        sketch.Points.RemoveAll(point => !used.Contains(point.Id));
    }

    private static SketchPoint AddPoint(Sketch sketch, double x, double y)
    {
        var point = new SketchPoint { X = x, Y = y };
        sketch.Points.Add(point);
        return point;
    }

    private static SketchLine AddLineBetween(Sketch sketch, SketchPoint start, SketchPoint end)
    {
        var line = new SketchLine { StartId = start.Id, EndId = end.Id };
        sketch.Lines.Add(line);
        return line;
    }

    private static bool AddUnique(Sketch sketch, ConstraintKind kind, Guid lineId)
    {
        if (sketch.Constraints.Any(constraint => constraint.Kind == kind && constraint.LineId == lineId))
        {
            return false;
        }

        sketch.Constraints.Add(new SketchConstraint { Kind = kind, LineId = lineId });
        return true;
    }

    private static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);

    private static double DistanceToSegment(double px, double py, double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < 1e-12)
        {
            return Hypot(px - x1, py - y1);
        }

        var t = Math.Clamp(((px - x1) * dx + (py - y1) * dy) / lengthSquared, 0, 1);
        return Hypot(px - (x1 + t * dx), py - (y1 + t * dy));
    }
}
