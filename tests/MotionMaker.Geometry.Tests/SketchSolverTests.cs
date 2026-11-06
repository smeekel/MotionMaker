using MotionMaker.Core;
using MotionMaker.Geometry;

namespace MotionMaker.Geometry.Tests;

public class SketchSolverTests
{
    [Fact]
    public void HorizontalConstraintMeetsAtTheMidpoint()
    {
        var sketch = new Sketch();
        var line = SketchEditor.AddLine(sketch, 0, 0, 10, 3);
        Assert.NotNull(line);
        Assert.True(SketchEditor.AddHorizontal(sketch, line.Id));

        var result = SketchSolver.Solve(sketch);

        Assert.True(result.Success);
        Assert.Equal(0, sketch.Points[0].X, 3);
        Assert.Equal(10, sketch.Points[1].X, 3);
        Assert.Equal(1.5, sketch.Points[0].Y, 3);
        Assert.Equal(1.5, sketch.Points[1].Y, 3);
    }

    [Fact]
    public void VerticalConstraintMeetsAtTheMidpoint()
    {
        var sketch = new Sketch();
        var line = SketchEditor.AddLine(sketch, 0, 0, 3, 10)!;
        SketchEditor.AddVertical(sketch, line.Id);

        Assert.True(SketchSolver.Solve(sketch).Success);
        Assert.Equal(1.5, sketch.Points[0].X, 3);
        Assert.Equal(1.5, sketch.Points[1].X, 3);
        Assert.Equal(0, sketch.Points[0].Y, 3);
        Assert.Equal(10, sketch.Points[1].Y, 3);
    }

    [Fact]
    public void CoincidentConstraintMeetsAtTheMidpoint()
    {
        var sketch = new Sketch();
        SketchEditor.AddLine(sketch, 0, 0, 4, 6);
        var a = sketch.Points[0].Id;
        var b = sketch.Points[1].Id;
        Assert.True(SketchEditor.AddCoincident(sketch, a, b));

        Assert.True(SketchSolver.Solve(sketch).Success);
        Assert.Equal(2, sketch.Points[0].X, 3);
        Assert.Equal(3, sketch.Points[0].Y, 3);
        Assert.Equal(2, sketch.Points[1].X, 3);
        Assert.Equal(3, sketch.Points[1].Y, 3);
    }

    [Fact]
    public void LengthConstraintShortensTheLineSymmetrically()
    {
        var sketch = new Sketch();
        var line = SketchEditor.AddLine(sketch, 0, 0, 10, 0)!;
        Assert.True(SketchEditor.AddLength(sketch, line.Id, 5));

        Assert.True(SketchSolver.Solve(sketch).Success);
        Assert.Equal(5, SketchEditor.LineLength(sketch, line), 3);
        Assert.Equal(2.5, sketch.Points[0].X, 2);
        Assert.Equal(7.5, sketch.Points[1].X, 2);
        Assert.Equal(0, sketch.Points[0].Y, 3);
        Assert.Equal(0, sketch.Points[1].Y, 3);
    }

    [Fact]
    public void RadiusConstraintKeepsTheCenter()
    {
        var sketch = new Sketch();
        var circle = SketchEditor.AddCircle(sketch, 1, 2, 10)!;
        Assert.True(SketchEditor.AddRadius(sketch, circle.Id, 4));

        Assert.True(SketchSolver.Solve(sketch).Success);
        Assert.Equal(4, circle.Radius, 3);
        Assert.Equal(1, sketch.Points[0].X, 3);
        Assert.Equal(2, sketch.Points[0].Y, 3);
    }

    [Fact]
    public void ConflictingConstraintsLeaveTheSketchUnchanged()
    {
        var sketch = new Sketch();
        var line = SketchEditor.AddLine(sketch, 0, 0, 10, 0)!;
        SketchEditor.AddCoincident(sketch, line.StartId, line.EndId);
        SketchEditor.AddLength(sketch, line.Id, 10);

        var result = SketchSolver.Solve(sketch);

        Assert.False(result.Success);
        Assert.Equal(0, sketch.Points[0].X);
        Assert.Equal(10, sketch.Points[1].X);
        Assert.Equal("Constraints could not be solved.", result.Message);
    }

    [Fact]
    public void UnconstrainedSketchStaysWhereItWasDrawn()
    {
        var sketch = new Sketch();
        SketchEditor.AddLine(sketch, 3, 4, 8, 1);

        Assert.True(SketchSolver.Solve(sketch).Success);
        Assert.Equal(3, sketch.Points[0].X);
        Assert.Equal(4, sketch.Points[0].Y);
        Assert.Equal(8, sketch.Points[1].X);
        Assert.Equal(1, sketch.Points[1].Y);
    }

    [Fact]
    public void DraggingARectangleCornerKeepsRightAngles()
    {
        var sketch = new Sketch();
        Assert.True(SketchEditor.AddRectangle(sketch, 0, 0, 10, 20));
        var corner = sketch.Points.Single(point => point.X == 10 && point.Y == 20);

        var result = SketchSolver.Solve(sketch, corner.Id, 15, 25);

        Assert.True(result.Success);
        AssertPoint(sketch, 0, 0);
        AssertPoint(sketch, 15, 0);
        AssertPoint(sketch, 15, 25);
        AssertPoint(sketch, 0, 25);
        Assert.Equal(4, sketch.Constraints.Count);
    }

    [Fact]
    public void RectangleConstraintsSurviveAPlainSolve()
    {
        var sketch = new Sketch();
        SketchEditor.AddRectangle(sketch, 1, 2, 11, 9);

        Assert.True(SketchSolver.Solve(sketch).Success);
        AssertPoint(sketch, 1, 2);
        AssertPoint(sketch, 11, 2);
        AssertPoint(sketch, 11, 9);
        AssertPoint(sketch, 1, 9);
    }

    private static void AssertPoint(Sketch sketch, double x, double y)
    {
        Assert.Contains(sketch.Points, point => Math.Abs(point.X - x) < 1e-3 && Math.Abs(point.Y - y) < 1e-3);
    }
}
