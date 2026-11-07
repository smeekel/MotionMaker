using MotionMaker.Core;
using MotionMaker.Geometry;

namespace MotionMaker.Geometry.Tests;

public class SolidTests
{
    [Fact]
    public void MarqueeWindowKeepsOnlyWhatIsInside()
    {
        var sketch = new Sketch();
        sketch.Points.Add(new SketchPoint { X = 1, Y = 1 });
        sketch.Points.Add(new SketchPoint { X = 5, Y = 5 });
        var line = SketchEditor.AddLine(sketch, 0, 0, 10, 0);
        Assert.NotNull(line);

        var inside = MarqueeSelection.Select(sketch, 0, 0, 2, 2);
        var crossing = MarqueeSelection.Select(sketch, 3, 1, 1, -1);

        Assert.Contains(sketch.Points[0].Id, inside.Points);
        Assert.DoesNotContain(sketch.Points[1].Id, inside.Points);
        Assert.DoesNotContain(line.Id, inside.Lines);
        Assert.Contains(line.Id, crossing.Lines);
    }

    [Fact]
    public void ThreePointsMadeCoincidentMeet()
    {
        var sketch = new Sketch();
        sketch.Points.Add(new SketchPoint { X = 0, Y = 0 });
        sketch.Points.Add(new SketchPoint { X = 10, Y = 0 });
        sketch.Points.Add(new SketchPoint { X = 4, Y = 8 });

        Assert.Equal(2, SketchEditor.AddCoincidentChain(sketch, sketch.Points.Select(point => point.Id).ToList()));
        Assert.True(SketchSolver.Solve(sketch).Success);
        Assert.Equal(sketch.Points[0].X, sketch.Points[1].X, 3);
        Assert.Equal(sketch.Points[0].Y, sketch.Points[1].Y, 3);
        Assert.Equal(sketch.Points[0].X, sketch.Points[2].X, 3);
        Assert.Equal(sketch.Points[0].Y, sketch.Points[2].Y, 3);
    }

    [Fact]
    public void SlotOutlineMatchesLengthAndWidth()
    {
        var sketch = new Sketch();
        var slot = SketchEditor.AddSlot(sketch, 0, 0, 20, 8, 0);
        Assert.NotNull(slot);
        Assert.True(ProfileBuilder.TryBuild(sketch, out var loops, out var error), error);
        var outline = loops.Single(loop => !loop.Hole);
        var length = ProfileBuilder.OutlineLength(loops);
        var expected = 2 * (20 - 8) + Math.PI * 8;

        Assert.InRange(length, expected - 0.4, expected + 0.4);
        var minX = outline.Points.Min(point => point.X);
        var maxX = outline.Points.Max(point => point.X);
        var minY = outline.Points.Min(point => point.Y);
        var maxY = outline.Points.Max(point => point.Y);
        Assert.InRange(maxX - minX, 19.5, 20.5);
        Assert.InRange(maxY - minY, 7.7, 8.3);
    }

    [Fact]
    public void RectanglePadAndHoleHaveTheExpectedVolume()
    {
        var part = ProjectEdits.AddPart(new ProjectDocument());
        var sketch = part.Sketches[0];
        Assert.True(SketchEditor.AddRectangle(sketch, 0, 0, 10, 20));
        var pad = FeatureGraph.AddPad(part, sketch, 5);
        FeatureGraph.Recompute(part);

        Assert.Equal(FeatureState.Current, pad.State);
        Assert.InRange(MeshQuery.SignedVolume(part.Body.Mesh!), 999, 1001);
        var top = pad.Mesh!.Faces.Single(face => face.Id.EndsWith(":top", StringComparison.Ordinal));
        var faceSketch = FeatureGraph.AddFaceSketch(part, pad, top.Id);
        var holeSketch = part.Sketches.Single(item => item.Id == faceSketch.SketchId);
        Assert.True(SketchEditor.AddRectangle(holeSketch, 2, 2, 6, 6));
        var hole = FeatureGraph.AddHole(part, holeSketch, depth: 5, throughAll: true);
        FeatureGraph.Recompute(part);

        Assert.Equal(FeatureState.Current, hole.State);
        Assert.InRange(MeshQuery.SignedVolume(part.Body.Mesh!), 910, 930);
    }

    [Fact]
    public void ChildRecomputesOnlyWhenItsParentChanges()
    {
        var part = ProjectEdits.AddPart(new ProjectDocument());
        var sketch = part.Sketches[0];
        SketchEditor.AddRectangle(sketch, 0, 0, 10, 10);
        var pad = FeatureGraph.AddPad(part, sketch, 4);
        FeatureGraph.Recompute(part);
        var top = pad.Mesh!.Faces.Single(face => face.Id.EndsWith(":top", StringComparison.Ordinal));
        var faceSketch = FeatureGraph.AddFaceSketch(part, pad, top.Id);
        var holeSketch = part.Sketches.Single(item => item.Id == faceSketch.SketchId);
        SketchEditor.AddRectangle(holeSketch, 2, 2, 5, 5);
        var hole = FeatureGraph.AddHole(part, holeSketch, throughAll: true);
        var gear = FeatureGraph.AddGear(part, GearKind.Spur);
        FeatureGraph.Recompute(part);
        var padCount = pad.ComputeCount;
        var holeCount = hole.ComputeCount;

        gear.Teeth = 30;
        FeatureGraph.Recompute(part);

        Assert.Equal(padCount, pad.ComputeCount);
        Assert.Equal(holeCount, hole.ComputeCount);

        pad.DistanceMm = 8;
        FeatureGraph.Recompute(part);

        Assert.True(hole.ComputeCount > holeCount);
        Assert.Equal(FeatureState.Current, pad.State);
    }

    [Fact]
    public void MissingSupportFaceFreezesTheChildAndLeavesTheParent()
    {
        var part = ProjectEdits.AddPart(new ProjectDocument());
        var sketch = part.Sketches[0];
        SketchEditor.AddRectangle(sketch, 0, 0, 8, 8);
        var pad = FeatureGraph.AddPad(part, sketch, 3);
        FeatureGraph.Recompute(part);
        var child = FeatureGraph.AddFaceSketch(part, pad, "missing-face");
        FeatureGraph.Recompute(part);

        Assert.Equal(FeatureState.Current, pad.State);
        Assert.Equal(FeatureState.Frozen, child.State);
        Assert.Equal(FeatureGraph.MissingFaceMessage, child.Error);
    }

    [Fact]
    public void SpurMeshIsClosedNearTheTipDiameter()
    {
        var part = ProjectEdits.AddPart(new ProjectDocument());
        var gear = FeatureGraph.AddGear(part, GearKind.Spur, module: 1, teeth: 20, faceWidth: 10, bore: 6);
        FeatureGraph.Recompute(part);

        Assert.Equal(FeatureState.Current, gear.State);
        var open = EdgeReport(gear.Mesh!);
        Assert.True(MeshQuery.IsClosed(gear.Mesh!), open);
        var (min, max) = MeshQuery.Bounds(gear.Mesh!);
        Assert.InRange(max.X - min.X, 21, 23);
        Assert.InRange(max.Y - min.Y, 21, 23);
    }

    [Fact]
    public void BevelGearBuildsAMesh()
    {
        var part = ProjectEdits.AddPart(new ProjectDocument());
        var gear = FeatureGraph.AddGear(part, GearKind.Bevel, module: 1, teeth: 16, faceWidth: 5, bore: 4, pitchAngle: 45);
        FeatureGraph.Recompute(part);

        Assert.Equal(FeatureState.Current, gear.State);
        Assert.True(gear.Mesh!.Indices.Count > 30);
    }

    [Fact]
    public void StlRoundTripsTheTriangleCount()
    {
        var part = ProjectEdits.AddPart(new ProjectDocument());
        SketchEditor.AddRectangle(part.Sketches[0], 0, 0, 4, 4);
        FeatureGraph.AddPad(part, part.Sketches[0], 2);
        FeatureGraph.Recompute(part);
        using var stream = new MemoryStream();
        StlWriter.Write(part.Body.Mesh!, stream);
        stream.Position = 0;

        Assert.Equal(part.Body.Mesh!.Indices.Count / 3, StlWriter.ReadTriangleCount(stream));
    }

    [Fact]
    public void BooleanRemovesAPrismAndACylinder()
    {
        var box = Box(10, 10, 10);
        var prism = SolidBuilder.Extrude(Rectangle(-1, 3, 11, 7), new SketchPlane { OriginZ = 3 }, 4, reversed: false, "prism");
        var cut = MeshBoolean.Subtract(box, prism);
        Assert.InRange(Math.Abs(MeshQuery.SignedVolume(cut)), 700, 900);

        var circle = new ProfileLoop();
        for (var step = 0; step < 24; step++)
        {
            var angle = step * Math.PI * 2 / 24;
            circle.Points.Add((5 + Math.Cos(angle) * 2, 5 + Math.Sin(angle) * 2));
        }

        var tool = SolidBuilder.Extrude([circle], new SketchPlane { OriginZ = -1 }, 12, reversed: false, "cyl");
        var bored = MeshBoolean.Subtract(box, tool);
        Assert.InRange(Math.Abs(MeshQuery.SignedVolume(bored)), 700, 950);
        Assert.True(bored.Indices.Count > 12);
    }

    private static string EdgeReport(MeshData mesh)
    {
        var counts = new Dictionary<string, int>();
        for (var index = 0; index + 2 < mesh.Indices.Count; index += 3)
        {
            var a = MeshQuery.At(mesh, mesh.Indices[index]);
            var b = MeshQuery.At(mesh, mesh.Indices[index + 1]);
            var c = MeshQuery.At(mesh, mesh.Indices[index + 2]);
            Count(counts, a, b);
            Count(counts, b, c);
            Count(counts, c, a);
        }

        var bad = counts.Where(pair => pair.Value != 2).Take(8).Select(pair => pair.Value.ToString());
        return $"tris {mesh.Indices.Count / 3} bad {string.Join(",", bad)} volume {MeshQuery.SignedVolume(mesh)}";
    }

    private static void Count(Dictionary<string, int> counts, Vec3 a, Vec3 b)
    {
        string Key(Vec3 p) => $"{Math.Round(p.X, 4)},{Math.Round(p.Y, 4)},{Math.Round(p.Z, 4)}";
        var left = Key(a);
        var right = Key(b);
        var key = string.CompareOrdinal(left, right) < 0 ? left + "|" + right : right + "|" + left;
        counts[key] = counts.GetValueOrDefault(key) + 1;
    }

    private static MeshData Box(double x, double y, double z)
        => SolidBuilder.Extrude(Rectangle(0, 0, x, y), SketchPlane.Base(), z, reversed: false, "box");

    private static List<ProfileLoop> Rectangle(double x1, double y1, double x2, double y2)
    {
        var loop = new ProfileLoop();
        loop.Points.Add((x1, y1));
        loop.Points.Add((x2, y1));
        loop.Points.Add((x2, y2));
        loop.Points.Add((x1, y2));
        return [loop];
    }
}
