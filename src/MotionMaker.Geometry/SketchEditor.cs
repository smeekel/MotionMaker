using MotionMaker.Core;

namespace MotionMaker.Geometry;

public enum SketchHitKind
{
    Point,
    Line,
    Circle,
    Slot
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

    public static SketchSlot? AddSlot(Sketch sketch, double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        double length;
        double width;
        double angle;
        if (Math.Abs(dx) >= Math.Abs(dy))
        {
            length = Math.Abs(dx);
            width = Math.Abs(dy);
            angle = dx >= 0 ? 0 : 180;
        }
        else
        {
            length = Math.Abs(dy);
            width = Math.Abs(dx);
            angle = dy >= 0 ? 90 : 270;
        }

        if (width < 0.5 || length < width + 0.5)
        {
            width = Math.Max(width, Math.Min(length * 0.45, 4));
        }

        if (length < width + 0.5)
        {
            return null;
        }

        return AddSlot(sketch, (x1 + x2) / 2, (y1 + y2) / 2, length, width, angle);
    }

    public static SketchSlot? AddSlot(Sketch sketch, double centerX, double centerY, double length, double width, double angleDegrees)
    {
        if (width < 0.5 || length < width + 0.5)
        {
            return null;
        }

        var slot = new SketchSlot
        {
            CenterX = centerX,
            CenterY = centerY,
            Length = length,
            Width = width,
            AngleDegrees = angleDegrees
        };
        var centerA = AddPoint(sketch, centerX, centerY);
        var centerB = AddPoint(sketch, centerX, centerY);
        var junctionA = AddPoint(sketch, centerX, centerY);
        var junctionB = AddPoint(sketch, centerX, centerY);
        var junctionC = AddPoint(sketch, centerX, centerY);
        var junctionD = AddPoint(sketch, centerX, centerY);
        centerA.SlotId = centerB.SlotId = slot.Id;
        junctionA.SlotId = junctionB.SlotId = junctionC.SlotId = junctionD.SlotId = slot.Id;
        centerA.Construction = centerB.Construction = true;
        var lineA = AddLineBetween(sketch, junctionA, junctionC);
        var lineB = AddLineBetween(sketch, junctionB, junctionD);
        lineA.SlotId = lineB.SlotId = slot.Id;
        var arcA = new SketchArc { CenterId = centerA.Id, StartId = junctionA.Id, EndId = junctionB.Id, SlotId = slot.Id };
        var arcB = new SketchArc { CenterId = centerB.Id, StartId = junctionC.Id, EndId = junctionD.Id, SlotId = slot.Id };
        sketch.Arcs.Add(arcA);
        sketch.Arcs.Add(arcB);
        slot.CenterAId = centerA.Id;
        slot.CenterBId = centerB.Id;
        slot.JunctionAId = junctionA.Id;
        slot.JunctionBId = junctionB.Id;
        slot.JunctionCId = junctionC.Id;
        slot.JunctionDId = junctionD.Id;
        slot.LineAId = lineA.Id;
        slot.LineBId = lineB.Id;
        slot.ArcAId = arcA.Id;
        slot.ArcBId = arcB.Id;
        sketch.Slots.Add(slot);
        RebuildSlot(sketch, slot);
        return slot;
    }

    public static void RebuildSlot(Sketch sketch, SketchSlot slot)
    {
        var radians = slot.AngleDegrees * Math.PI / 180;
        var dirX = Math.Cos(radians);
        var dirY = Math.Sin(radians);
        var normalX = -dirY;
        var normalY = dirX;
        var radius = slot.Width / 2;
        var span = Math.Max(0, slot.Length - slot.Width);
        var centerAX = slot.CenterX - dirX * span / 2;
        var centerAY = slot.CenterY - dirY * span / 2;
        var centerBX = slot.CenterX + dirX * span / 2;
        var centerBY = slot.CenterY + dirY * span / 2;
        Move(sketch, slot.CenterAId, centerAX, centerAY);
        Move(sketch, slot.CenterBId, centerBX, centerBY);
        Move(sketch, slot.JunctionAId, centerAX + normalX * radius, centerAY + normalY * radius);
        Move(sketch, slot.JunctionBId, centerAX - normalX * radius, centerAY - normalY * radius);
        Move(sketch, slot.JunctionCId, centerBX + normalX * radius, centerBY + normalY * radius);
        Move(sketch, slot.JunctionDId, centerBX - normalX * radius, centerBY - normalY * radius);
        var outwardX = centerAX - dirX * radius;
        var outwardY = centerAY - dirY * radius;
        var arcA = sketch.Arcs.First(arc => arc.Id == slot.ArcAId);
        var arcB = sketch.Arcs.First(arc => arc.Id == slot.ArcBId);
        arcA.Clockwise = BulgeIsClockwise(sketch, arcA, outwardX, outwardY);
        arcB.Clockwise = BulgeIsClockwise(sketch, arcB, centerBX + dirX * radius, centerBY + dirY * radius);
    }

    public static int AddCoincidentChain(Sketch sketch, IReadOnlyList<Guid> pointIds)
    {
        var added = 0;
        for (var index = 1; index < pointIds.Count; index++)
        {
            if (AddCoincident(sketch, pointIds[index - 1], pointIds[index]))
            {
                added++;
            }
        }

        return added;
    }

    public static List<(double X, double Y)> TessellateArc(Sketch sketch, SketchArc arc, double stepDegrees = 2)
    {
        var center = sketch.Points.First(point => point.Id == arc.CenterId);
        var start = sketch.Points.First(point => point.Id == arc.StartId);
        var end = sketch.Points.First(point => point.Id == arc.EndId);
        var radius = Hypot(start.X - center.X, start.Y - center.Y);
        var startAngle = Math.Atan2(start.Y - center.Y, start.X - center.X);
        var endAngle = Math.Atan2(end.Y - center.Y, end.X - center.X);
        var sweep = Sweep(startAngle, endAngle, arc.Clockwise);
        var steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweep) * 180 / Math.PI / Math.Max(stepDegrees, 0.5)));
        var points = new List<(double X, double Y)>(steps + 1);
        for (var step = 0; step <= steps; step++)
        {
            var angle = startAngle + sweep * step / steps;
            points.Add((center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius));
        }

        return points;
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
            if (point.Construction)
            {
                continue;
            }

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
            return closestLine.SlotId is Guid slotId
                ? new SketchHit(SketchHitKind.Slot, slotId)
                : new SketchHit(SketchHitKind.Line, closestLine.Id);
        }

        SketchArc? closestArc = null;
        var closestArcDistance = tolerance;
        foreach (var arc in sketch.Arcs)
        {
            var samples = TessellateArc(sketch, arc);
            for (var index = 1; index < samples.Count; index++)
            {
                var distance = DistanceToSegment(
                    x,
                    y,
                    samples[index - 1].X,
                    samples[index - 1].Y,
                    samples[index].X,
                    samples[index].Y);
                if (distance <= closestArcDistance)
                {
                    closestArc = arc;
                    closestArcDistance = distance;
                }
            }
        }

        if (closestArc is not null)
        {
            return closestArc.SlotId is Guid slotId
                ? new SketchHit(SketchHitKind.Slot, slotId)
                : new SketchHit(SketchHitKind.Line, closestArc.Id);
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
                var slotLine = sketch.Lines.FirstOrDefault(line => line.Id == hit.Id);
                if (slotLine?.SlotId is Guid owningSlot)
                {
                    DeleteSlot(sketch, owningSlot);
                    break;
                }

                sketch.Lines.RemoveAll(line => line.Id == hit.Id);
                sketch.Constraints.RemoveAll(constraint => constraint.LineId == hit.Id);
                RemoveOrphanPoints(sketch);
                break;
            case SketchHitKind.Circle:
                sketch.Circles.RemoveAll(circle => circle.Id == hit.Id);
                sketch.Constraints.RemoveAll(constraint => constraint.CircleId == hit.Id);
                RemoveOrphanPoints(sketch);
                break;
            case SketchHitKind.Slot:
                DeleteSlot(sketch, hit.Id);
                break;
        }
    }

    public static void DeleteSlot(Sketch sketch, Guid slotId)
    {
        var lineIds = sketch.Lines.Where(line => line.SlotId == slotId).Select(line => line.Id).ToHashSet();
        var pointIds = sketch.Points.Where(point => point.SlotId == slotId).Select(point => point.Id).ToHashSet();
        sketch.Slots.RemoveAll(slot => slot.Id == slotId);
        sketch.Arcs.RemoveAll(arc => arc.SlotId == slotId);
        sketch.Lines.RemoveAll(line => line.SlotId == slotId);
        sketch.Points.RemoveAll(point => point.SlotId == slotId);
        sketch.Constraints.RemoveAll(constraint =>
            (constraint.LineId is Guid lineId && lineIds.Contains(lineId))
            || (constraint.PointAId is Guid pointA && pointIds.Contains(pointA))
            || (constraint.PointBId is Guid pointB && pointIds.Contains(pointB)));
        RemoveOrphanPoints(sketch);
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
        var point = sketch.Points.FirstOrDefault(item => item.Id == pointId);
        if (point?.SlotId is Guid slotId)
        {
            DeleteSlot(sketch, slotId);
            return;
        }

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

        foreach (var arc in sketch.Arcs)
        {
            used.Add(arc.CenterId);
            used.Add(arc.StartId);
            used.Add(arc.EndId);
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

    private static void Move(Sketch sketch, Guid pointId, double x, double y)
    {
        var point = sketch.Points.First(item => item.Id == pointId);
        point.X = x;
        point.Y = y;
    }

    private static bool BulgeIsClockwise(Sketch sketch, SketchArc arc, double targetX, double targetY)
    {
        arc.Clockwise = false;
        var counter = TessellateArc(sketch, arc, 8);
        arc.Clockwise = true;
        var clock = TessellateArc(sketch, arc, 8);
        var midCounter = counter[counter.Count / 2];
        var midClock = clock[clock.Count / 2];
        var counterDistance = Hypot(midCounter.X - targetX, midCounter.Y - targetY);
        var clockDistance = Hypot(midClock.X - targetX, midClock.Y - targetY);
        return clockDistance <= counterDistance;
    }

    private static double Sweep(double from, double to, bool clockwise)
    {
        var sweep = to - from;
        if (clockwise)
        {
            if (sweep > 0)
            {
                sweep -= Math.PI * 2;
            }
        }
        else if (sweep < 0)
        {
            sweep += Math.PI * 2;
        }

        return sweep;
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
