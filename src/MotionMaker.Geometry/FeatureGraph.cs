using System.Globalization;
using System.Text;
using MotionMaker.Core;

namespace MotionMaker.Geometry;

public static class FeatureGraph
{
    public const string MissingFaceMessage = "The face this sketch was drawn on is gone.";

    public static PartFeature AddPad(PartDesign part, Sketch sketch, double distance = 10)
    {
        var feature = new PartFeature
        {
            Name = Next(part, "Pad"),
            Kind = FeatureKind.Pad,
            SketchId = sketch.Id,
            DistanceMm = distance,
            Parents = [SketchFeature(part, sketch).Id]
        };
        part.Features.Add(feature);
        return feature;
    }

    public static PartFeature AddHole(PartDesign part, Sketch sketch, double depth = 10, bool throughAll = true)
    {
        var feature = new PartFeature
        {
            Name = Next(part, "Hole"),
            Kind = FeatureKind.Hole,
            SketchId = sketch.Id,
            DepthMm = depth,
            ThroughAll = throughAll,
            Parents = [SketchFeature(part, sketch).Id]
        };
        part.Features.Add(feature);
        return feature;
    }

    public static PartFeature AddGear(PartDesign part, GearKind kind, double module = 1, int teeth = 20, double pressure = 20, double faceWidth = 10, double bore = 6, double pitchAngle = 45)
    {
        var feature = new PartFeature
        {
            Name = kind == GearKind.Bevel ? Next(part, "Bevel gear") : Next(part, "Spur gear"),
            Kind = FeatureKind.Gear,
            GearType = kind,
            ModuleMm = module,
            Teeth = teeth,
            PressureAngleDegrees = pressure,
            FaceWidthMm = faceWidth,
            BoreMm = bore,
            PitchAngleDegrees = pitchAngle
        };
        part.Features.Add(feature);
        return feature;
    }

    public static PartFeature AddFaceSketch(PartDesign part, PartFeature support, string faceId)
    {
        var sketch = new Sketch
        {
            Name = Next(part, "Sketch"),
            SupportFaceId = faceId,
            SupportFeatureId = support.Id
        };
        if (MeshQuery.TryGetFace(support.Mesh, faceId, out var plane))
        {
            var parentSketch = part.Sketches.FirstOrDefault(item => item.Id == support.SketchId);
            if (parentSketch is not null && Vec3.Dot(parentSketch.Plane.Normal, plane.Normal) > 0.99)
            {
                var kept = parentSketch.Plane.Clone();
                var along = Vec3.Dot(plane.Origin - kept.Origin, kept.Normal);
                kept.OriginX += kept.NormalX * along;
                kept.OriginY += kept.NormalY * along;
                kept.OriginZ += kept.NormalZ * along;
                sketch.Plane = kept;
            }
            else
            {
                sketch.Plane = plane;
            }
        }

        part.Sketches.Add(sketch);
        var feature = new PartFeature
        {
            Name = sketch.Name,
            Kind = FeatureKind.Sketch,
            SketchId = sketch.Id,
            Parents = [support.Id]
        };
        part.Features.Add(feature);
        return feature;
    }

    public static bool HasClosedProfile(Sketch sketch)
        => ProfileBuilder.TryBuild(sketch, out _, out _);

    public static void Recompute(PartDesign part)
    {
        MeshData? chain = null;
        string chainStamp = "";
        PartFeature? previousSolid = null;
        foreach (var feature in part.Features)
        {
            var parameter = ParameterStamp(part, feature);
            var parentStamp = ParentStamp(part, feature);
            var historyStamp = feature.Kind == FeatureKind.Gear ? "" : chainStamp;
            var blocked = BlockingParent(part, feature) ?? (feature.Kind is FeatureKind.Pad or FeatureKind.Hole ? previousSolid is { State: FeatureState.Frozen } ? previousSolid : null : null);
            if (blocked is not null)
            {
                var message = $"Parent {blocked.Name} failed. {blocked.Error}";
                NoteFailure(feature, message, parameter, parentStamp, historyStamp);
            }
            else
            {
                var needs = feature.ResultStamp is null
                    || feature.ParameterStamp != parameter
                    || feature.ConsumedParentStamp != parentStamp
                    || feature.ConsumedHistoryStamp != historyStamp;
                if (needs)
                {
                    var error = Compute(part, feature, chain);
                    if (error is null)
                    {
                        feature.State = FeatureState.Current;
                        feature.Error = null;
                        feature.ParameterStamp = parameter;
                        feature.ConsumedParentStamp = parentStamp;
                        feature.ConsumedHistoryStamp = historyStamp;
                        feature.ResultStamp = feature.Kind == FeatureKind.Sketch
                            ? ResultSketch(part, feature)
                            : parameter + "|" + parentStamp + "|" + historyStamp;
                        feature.ComputeCount++;
                    }
                    else
                    {
                        NoteFailure(feature, error, parameter, parentStamp, historyStamp);
                    }
                }
            }

            if (feature.Kind is FeatureKind.Pad or FeatureKind.Hole && feature.Mesh is { Indices.Count: > 0 })
            {
                chain = feature.Mesh;
                chainStamp = feature.ResultStamp ?? "";
                previousSolid = feature;
            }
            else if (feature.Kind == FeatureKind.Gear && feature.Mesh is { Indices.Count: > 0 } && chain is null)
            {
                chain = feature.Mesh;
                chainStamp = feature.ResultStamp ?? "";
                previousSolid = feature;
            }
        }

        PartFeature? current = null;
        PartFeature? any = null;
        foreach (var feature in part.Features)
        {
            if (feature.Kind is not (FeatureKind.Pad or FeatureKind.Hole or FeatureKind.Gear) || feature.Mesh is not { Indices.Count: > 0 })
            {
                continue;
            }

            any = feature;
            if (feature.State == FeatureState.Current)
            {
                current = feature;
            }
        }

        var chosen = current ?? any;
        part.Body.Mesh = chosen?.Mesh;
        part.Body.SourceFeatureId = chosen?.Id;
    }

    public static Vec3 Anchor(PartDesign part, PartFeature feature)
    {
        if (feature.Mesh is { Positions.Count: >= 3 } mesh)
        {
            return MeshQuery.Centroid(mesh);
        }

        var sketch = FindSketch(part, feature);
        if (sketch is null)
        {
            return default;
        }

        if (sketch.Points.Count == 0)
        {
            return sketch.Plane.Origin;
        }

        var u = sketch.Points.Average(point => point.X);
        var v = sketch.Points.Average(point => point.Y);
        return sketch.Plane.ToWorld(u, v);
    }

    private static void NoteFailure(PartFeature feature, string message, string parameter, string parentStamp, string historyStamp)
    {
        if (feature.State == FeatureState.Frozen
            && feature.Error == message
            && feature.ParameterStamp == parameter
            && feature.ConsumedParentStamp == parentStamp
            && feature.ConsumedHistoryStamp == historyStamp)
        {
            return;
        }

        feature.State = FeatureState.Frozen;
        feature.Error = message;
        feature.ParameterStamp = parameter;
        feature.ConsumedParentStamp = parentStamp;
        feature.ConsumedHistoryStamp = historyStamp;
        feature.ResultStamp = "failed:" + message;
        feature.ComputeCount++;
    }

    private static string? Compute(PartDesign part, PartFeature feature, MeshData? chain)
    {
        switch (feature.Kind)
        {
            case FeatureKind.Sketch:
                return ComputeSketch(part, feature);
            case FeatureKind.Gear:
                if (!GearBuilder.TryBuild(feature, out var gear, out var gearError))
                {
                    return gearError;
                }

                feature.Mesh = gear;
                return null;
            case FeatureKind.Pad:
            case FeatureKind.Hole:
                var sketch = FindSketch(part, feature);
                if (sketch is null)
                {
                    return ProfileBuilder.OpenProfileMessage;
                }

                if (!ProfileBuilder.TryBuild(sketch, out var loops, out var profileError))
                {
                    return profileError ?? ProfileBuilder.OpenProfileMessage;
                }

                var key = feature.Id.ToString("N");
                if (feature.Kind == FeatureKind.Pad && sketch.SupportFaceId is null)
                {
                    feature.Mesh = SolidBuilder.Extrude(loops, sketch.Plane, feature.DistanceMm, feature.Reversed, key);
                    return feature.Mesh.Indices.Count > 0 ? null : "The pad produced no solid.";
                }

                var target = chain;
                if (target is null)
                {
                    return feature.Kind == FeatureKind.Hole
                        ? "A hole needs a solid to cut."
                        : "The face this sketch was drawn on is gone.";
                }

                if (sketch.SupportFaceId is string faceId)
                {
                    if (feature.Kind == FeatureKind.Hole
                        && FaceCarve.TrySubtract(target, sketch.Plane, faceId, loops, feature.DepthMm, feature.ThroughAll, key, out var carved))
                    {
                        feature.Mesh = carved;
                        return null;
                    }

                    if (feature.Kind == FeatureKind.Pad
                        && FaceCarve.TryUnion(target, sketch.Plane, faceId, loops, feature.DistanceMm, key, out var boss))
                    {
                        feature.Mesh = boss;
                        return null;
                    }
                }

                var depth = feature.Kind == FeatureKind.Hole
                    ? feature.ThroughAll ? Span(target) + 1 : feature.DepthMm
                    : feature.DistanceMm;
                var toolPlane = sketch.Plane.Clone();
                if (feature.Kind == FeatureKind.Hole)
                {
                    toolPlane.OriginX += sketch.Plane.NormalX * 0.5;
                    toolPlane.OriginY += sketch.Plane.NormalY * 0.5;
                    toolPlane.OriginZ += sketch.Plane.NormalZ * 0.5;
                    depth += 1;
                }

                var tool = SolidBuilder.Extrude(loops, toolPlane, depth, feature.Kind == FeatureKind.Hole || feature.Reversed, key);
                feature.Mesh = feature.Kind == FeatureKind.Hole
                    ? MeshBoolean.Subtract(target, tool)
                    : MeshBoolean.Union(target, tool);
                return feature.Mesh.Indices.Count > 0 ? null : "The solid boolean failed.";
            default:
                return "Unknown feature.";
        }
    }

    private static string? ComputeSketch(PartDesign part, PartFeature feature)
    {
        var sketch = FindSketch(part, feature);
        if (sketch is null)
        {
            return "The sketch is missing.";
        }

            if (sketch.SupportFeatureId is Guid supportId)
            {
                var support = part.Features.FirstOrDefault(item => item.Id == supportId);
                if (sketch.SupportFaceId is not string faceId
                    || support?.Mesh is null
                    || !MeshQuery.TryGetFace(support.Mesh, faceId, out var plane))
                {
                    return MissingFaceMessage;
                }

                var previous = sketch.Plane;
                if (Vec3.Dot(previous.Normal, plane.Normal) > 0.99 && previous.Normal.Length > 0.5)
                {
                    var along = Vec3.Dot(plane.Origin - previous.Origin, previous.Normal);
                    sketch.Plane = previous.Clone();
                    sketch.Plane.OriginX += previous.NormalX * along;
                    sketch.Plane.OriginY += previous.NormalY * along;
                    sketch.Plane.OriginZ += previous.NormalZ * along;
                }
                else
                {
                    sketch.Plane = plane;
                }
            }

        return null;
    }

    private static PartFeature? BlockingParent(PartDesign part, PartFeature feature)
    {
        foreach (var parentId in feature.Parents)
        {
            var parent = part.Features.FirstOrDefault(item => item.Id == parentId);
            if (parent is { State: FeatureState.Frozen })
            {
                return parent;
            }
        }

        return null;
    }

    private static string ParameterStamp(PartDesign part, PartFeature feature)
    {
        var sketch = FindSketch(part, feature);
        return feature.Kind switch
        {
            FeatureKind.Sketch => SketchPoints(sketch),
            FeatureKind.Pad => FormattableString.Invariant($"{F(feature.DistanceMm)}|{feature.Reversed}|{SketchPoints(sketch)}"),
            FeatureKind.Hole => FormattableString.Invariant($"{F(feature.DepthMm)}|{feature.ThroughAll}|{SketchPoints(sketch)}"),
            _ => FormattableString.Invariant($"{feature.GearType}|{F(feature.ModuleMm)}|{feature.Teeth}|{F(feature.PressureAngleDegrees)}|{F(feature.FaceWidthMm)}|{F(feature.BoreMm)}|{F(feature.PitchAngleDegrees)}")
        };
    }

    private static string ResultSketch(PartDesign part, PartFeature feature)
    {
        var sketch = FindSketch(part, feature);
        return SketchPoints(sketch) + "|" + PlaneStamp(sketch?.Plane);
    }

    private static string SketchPoints(Sketch? sketch)
    {
        if (sketch is null)
        {
            return "";
        }

        var builder = new StringBuilder();
        foreach (var point in sketch.Points.OrderBy(point => point.Id))
        {
            builder.Append(point.Id.ToString("N")).Append(F(point.X)).Append(F(point.Y));
        }

        foreach (var circle in sketch.Circles.OrderBy(circle => circle.Id))
        {
            builder.Append(F(circle.Radius));
        }

        foreach (var slot in sketch.Slots.OrderBy(slot => slot.Id))
        {
            builder.Append(F(slot.CenterX)).Append(F(slot.CenterY)).Append(F(slot.Length)).Append(F(slot.Width)).Append(F(slot.AngleDegrees));
        }

        return builder.ToString();
    }

    private static string PlaneStamp(SketchPlane? plane)
    {
        if (plane is null)
        {
            return "";
        }

        return string.Join(",", F(plane.OriginX), F(plane.OriginY), F(plane.OriginZ), F(plane.NormalX), F(plane.NormalY), F(plane.NormalZ));
    }

    private static string ParentStamp(PartDesign part, PartFeature feature)
        => string.Join("|", feature.Parents.Select(id => part.Features.FirstOrDefault(item => item.Id == id)?.ResultStamp ?? ""));

    private static Sketch? FindSketch(PartDesign part, PartFeature feature)
        => feature.SketchId is Guid id ? part.Sketches.FirstOrDefault(sketch => sketch.Id == id) : null;

    private static PartFeature SketchFeature(PartDesign part, Sketch sketch)
        => part.Features.First(feature => feature.Kind == FeatureKind.Sketch && feature.SketchId == sketch.Id);

    private static double Span(MeshData mesh)
    {
        var (min, max) = MeshQuery.Bounds(mesh);
        return (max - min).Length + 2;
    }

    private static string Next(PartDesign part, string prefix)
    {
        var names = part.Features.Select(feature => feature.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(prefix))
        {
            return prefix;
        }

        var index = 2;
        string name;
        do
        {
            name = $"{prefix} {index}";
            index++;
        }
        while (names.Contains(name));

        return name;
    }

    private static string F(double value) => Math.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);
}
