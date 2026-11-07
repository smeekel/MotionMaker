using MotionMaker.Core;

namespace MotionMaker.Geometry;

public static class SketchSolver
{
    public const double Tolerance = 1e-4;

    public readonly record struct PinnedPoint(Guid Id, double X, double Y);

    public static SolveResult Solve(
        Sketch sketch,
        Guid? fixedPointId = null,
        double? fixedX = null,
        double? fixedY = null)
    {
        if (fixedPointId is Guid fixedId && fixedX is double x && fixedY is double y)
        {
            return SolvePinned(sketch, [new PinnedPoint(fixedId, x, y)]);
        }

        return SolvePinned(sketch, null);
    }

    public static SolveResult SolvePinned(Sketch sketch, IReadOnlyList<PinnedPoint>? pinned)
    {
        var work = sketch.Clone();
        if (pinned is not null)
        {
            foreach (var pin in pinned)
            {
                var point = work.Points.FirstOrDefault(item => item.Id == pin.Id);
                if (point is not null)
                {
                    point.X = pin.X;
                    point.Y = pin.Y;
                }
            }
        }

        if (work.Constraints.Count == 0)
        {
            CopyGeometry(work, sketch);
            return SolveResult.Ok();
        }

        var variables = CollectVariables(work, pinned);
        if (!TryConverge(work, variables))
        {
            return SolveResult.Failed("Constraints could not be solved.");
        }

        CopyGeometry(work, sketch);
        return SolveResult.Ok();
    }

    private static bool TryConverge(Sketch sketch, List<Variable> variables)
    {
        if (MaxResidual(sketch) < Tolerance)
        {
            return true;
        }

        if (variables.Count == 0)
        {
            return false;
        }

        var lambda = 1e-3;
        var values = variables.Select(variable => variable.Read()).ToArray();
        var error = MaxResidual(sketch);
        for (var iteration = 0; iteration < 40; iteration++)
        {
            if (error < Tolerance)
            {
                return true;
            }

            var residuals = Evaluate(sketch);
            var jacobian = BuildJacobian(sketch, variables, residuals);
            var count = variables.Count;
            var normal = new double[count, count];
            var right = new double[count];
            for (var row = 0; row < residuals.Length; row++)
            {
                for (var column = 0; column < count; column++)
                {
                    right[column] -= jacobian[row, column] * residuals[row];
                    for (var other = 0; other < count; other++)
                    {
                        normal[column, other] += jacobian[row, column] * jacobian[row, other];
                    }
                }
            }

            for (var index = 0; index < count; index++)
            {
                var diagonal = normal[index, index];
                normal[index, index] += lambda * (diagonal > 1e-8 ? diagonal : 1);
            }

            var step = new double[count];
            if (!SolveLinear(normal, right, step))
            {
                lambda = Math.Min(lambda * 8, 1e12);
                continue;
            }

            for (var index = 0; index < count; index++)
            {
                variables[index].Write(values[index] + step[index]);
            }

            var next = MaxResidual(sketch);
            if (next < error)
            {
                for (var index = 0; index < count; index++)
                {
                    values[index] += step[index];
                }

                error = next;
                lambda = Math.Max(lambda * 0.3, 1e-8);
            }
            else
            {
                for (var index = 0; index < count; index++)
                {
                    variables[index].Write(values[index]);
                }

                lambda *= 8;
                if (lambda > 1e10)
                {
                    break;
                }
            }
        }

        return error < Tolerance;
    }

    private static double[,] BuildJacobian(Sketch sketch, List<Variable> variables, double[] residuals)
    {
        const double step = 1e-6;
        var jacobian = new double[residuals.Length, variables.Count];
        for (var column = 0; column < variables.Count; column++)
        {
            var original = variables[column].Read();
            variables[column].Write(original + step);
            var shifted = Evaluate(sketch);
            variables[column].Write(original);
            for (var row = 0; row < residuals.Length; row++)
            {
                jacobian[row, column] = (shifted[row] - residuals[row]) / step;
            }
        }

        return jacobian;
    }

    private static double MaxResidual(Sketch sketch)
    {
        var residuals = Evaluate(sketch);
        return residuals.Length == 0 ? 0 : residuals.Max(Math.Abs);
    }

    private static double[] Evaluate(Sketch sketch)
    {
        var residuals = new List<double>();
        foreach (var constraint in sketch.Constraints)
        {
            switch (constraint.Kind)
            {
                case ConstraintKind.Horizontal:
                    AddLineAxis(sketch, constraint.LineId, vertical: false, residuals);
                    break;
                case ConstraintKind.Vertical:
                    AddLineAxis(sketch, constraint.LineId, vertical: true, residuals);
                    break;
                case ConstraintKind.Coincident:
                    var a = FindPoint(sketch, constraint.PointAId);
                    var b = FindPoint(sketch, constraint.PointBId);
                    if (a is not null && b is not null)
                    {
                        residuals.Add(a.X - b.X);
                        residuals.Add(a.Y - b.Y);
                    }

                    break;
                case ConstraintKind.Length:
                    var line = FindLine(sketch, constraint.LineId);
                    if (line is null)
                    {
                        break;
                    }

                    var start = FindPoint(sketch, line.StartId);
                    var end = FindPoint(sketch, line.EndId);
                    if (start is not null && end is not null)
                    {
                        residuals.Add(Distance(start, end) - constraint.Value);
                    }

                    break;
                case ConstraintKind.Radius:
                    var circle = sketch.Circles.FirstOrDefault(item => item.Id == constraint.CircleId);
                    if (circle is not null)
                    {
                        residuals.Add(circle.Radius - constraint.Value);
                    }

                    break;
            }
        }

        return residuals.ToArray();
    }

    private static void AddLineAxis(Sketch sketch, Guid? lineId, bool vertical, List<double> residuals)
    {
        var line = FindLine(sketch, lineId);
        if (line is null)
        {
            return;
        }

        var start = FindPoint(sketch, line.StartId);
        var end = FindPoint(sketch, line.EndId);
        if (start is null || end is null)
        {
            return;
        }

        residuals.Add(vertical ? end.X - start.X : end.Y - start.Y);
    }

    private static List<Variable> CollectVariables(Sketch sketch, IReadOnlyList<PinnedPoint>? pinned)
    {
        var fixedIds = pinned?.Select(pin => pin.Id).ToHashSet() ?? [];
        var variables = new List<Variable>();
        foreach (var point in sketch.Points)
        {
            if (point.SlotId is not null || fixedIds.Contains(point.Id))
            {
                continue;
            }

            var captured = point;
            variables.Add(new Variable(() => captured.X, value => captured.X = value));
            variables.Add(new Variable(() => captured.Y, value => captured.Y = value));
        }

        foreach (var circle in sketch.Circles)
        {
            var captured = circle;
            variables.Add(new Variable(() => captured.Radius, value => captured.Radius = Math.Max(0, value)));
        }

        return variables;
    }

    private static void CopyGeometry(Sketch from, Sketch to)
    {
        foreach (var point in to.Points)
        {
            var source = from.Points.First(item => item.Id == point.Id);
            point.X = source.X;
            point.Y = source.Y;
        }

        foreach (var circle in to.Circles)
        {
            var source = from.Circles.First(item => item.Id == circle.Id);
            circle.Radius = source.Radius;
        }
    }

    private static SketchPoint? FindPoint(Sketch sketch, Guid? id)
        => id is null ? null : sketch.Points.FirstOrDefault(point => point.Id == id);

    private static SketchLine? FindLine(Sketch sketch, Guid? id)
        => id is null ? null : sketch.Lines.FirstOrDefault(line => line.Id == id);

    private static double Distance(SketchPoint a, SketchPoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static bool SolveLinear(double[,] matrix, double[] right, double[] solution)
    {
        var size = right.Length;
        var a = new double[size, size + 1];
        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
            {
                a[row, column] = matrix[row, column];
            }

            a[row, size] = right[row];
        }

        for (var column = 0; column < size; column++)
        {
            var pivot = column;
            var max = Math.Abs(a[column, column]);
            for (var row = column + 1; row < size; row++)
            {
                var value = Math.Abs(a[row, column]);
                if (value > max)
                {
                    max = value;
                    pivot = row;
                }
            }

            if (max < 1e-14)
            {
                return false;
            }

            if (pivot != column)
            {
                for (var k = column; k <= size; k++)
                {
                    (a[column, k], a[pivot, k]) = (a[pivot, k], a[column, k]);
                }
            }

            var divisor = a[column, column];
            for (var k = column; k <= size; k++)
            {
                a[column, k] /= divisor;
            }

            for (var row = 0; row < size; row++)
            {
                if (row == column)
                {
                    continue;
                }

                var factor = a[row, column];
                for (var k = column; k <= size; k++)
                {
                    a[row, k] -= factor * a[column, k];
                }
            }
        }

        for (var index = 0; index < size; index++)
        {
            solution[index] = a[index, size];
        }

        return true;
    }

    private sealed class Variable(Func<double> read, Action<double> write)
    {
        public double Read() => read();

        public void Write(double value) => write(value);
    }
}
