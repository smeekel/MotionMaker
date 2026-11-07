using MotionMaker.Core;

namespace MotionMaker.Geometry;

public static class GearBuilder
{
    public static bool TryBuild(PartFeature feature, out MeshData mesh, out string? error)
    {
        mesh = new MeshData();
        error = null;
        if (feature.ModuleMm <= 0.05 || feature.Teeth < 6 || feature.FaceWidthMm <= 0.1 || feature.BoreMm <= 0.1)
        {
            error = "Gear parameters need a module, at least 6 teeth, a face width, and a bore.";
            return false;
        }

        var module = feature.ModuleMm;
        var teeth = feature.Teeth;
        var pressure = feature.PressureAngleDegrees * Math.PI / 180;
        var pitchRadius = module * teeth / 2;
        var baseRadius = pitchRadius * Math.Cos(pressure);
        var tipRadius = pitchRadius + module;
        var rootRadius = Math.Max(feature.BoreMm / 2 + 0.3, pitchRadius - 1.25 * module);
        if (rootRadius <= feature.BoreMm / 2 + 0.2 || tipRadius <= rootRadius)
        {
            error = "The bore does not fit inside this gear.";
            return false;
        }

        var outer = ToothLoop(teeth, baseRadius, tipRadius, rootRadius, pressure);
        var bore = Circle(feature.BoreMm / 2, 32, clockwise: true);
        if (feature.GearType == GearKind.Spur)
        {
            mesh = SolidBuilder.Extrude(
                [outer, bore],
                SketchPlane.Base(),
                feature.FaceWidthMm,
                reversed: false,
                feature.Id.ToString("N"));
            return mesh.Indices.Count > 0;
        }

        var cone = pitchRadius / Math.Sin(Math.Clamp(feature.PitchAngleDegrees, 5, 85) * Math.PI / 180);
        if (feature.FaceWidthMm >= cone * 0.9)
        {
            error = "The bevel face width or bore does not fit this gear.";
            return false;
        }

        var scale = (cone - feature.FaceWidthMm) / cone;
        var axial = feature.FaceWidthMm * Math.Cos(feature.PitchAngleDegrees * Math.PI / 180);
        mesh = Loft(outer, bore, scale, axial, feature.Id.ToString("N"));
        return mesh.Indices.Count > 0;
    }

    private static MeshData Loft(ProfileLoop outer, ProfileLoop bore, double scale, double axial, string key)
    {
        var builder = new MeshBuilder();
        var heel = new List<ProfileLoop> { outer, bore };
        var toeOuter = Scale(outer, scale);
        var toeBore = Scale(bore, scale);
        var toe = new List<ProfileLoop> { toeOuter, toeBore };
        AddCap(builder, heel, 0, flip: true, $"{key}:bottom");
        AddCap(builder, toe, axial, flip: false, $"{key}:top");
        Stitch(builder, outer.Points, toeOuter.Points, axial, scale, $"{key}:tooth");
        Stitch(builder, bore.Points, toeBore.Points, axial, scale, $"{key}:bore");
        return builder.Build();
    }

    private static void Stitch(MeshBuilder builder, List<(double X, double Y)> heel, List<(double X, double Y)> toe, double axial, double scale, string face)
    {
        var count = Math.Min(heel.Count, toe.Count);
        for (var index = 0; index < count; index++)
        {
            var next = (index + 1) % count;
            var a = new Vec3(heel[index].X, heel[index].Y, 0);
            var b = new Vec3(heel[next].X, heel[next].Y, 0);
            var c = new Vec3(toe[next].X, toe[next].Y, axial);
            var d = new Vec3(toe[index].X, toe[index].Y, axial);
            var outward = Vec3.Cross(b - a, d - a);
            SolidBuilder.AddOriented(builder, a, b, c, outward, face);
            SolidBuilder.AddOriented(builder, a, c, d, outward, face);
        }

        _ = scale;
    }

    private static void AddCap(MeshBuilder builder, IReadOnlyList<ProfileLoop> loops, double z, bool flip, string face)
    {
        var plane = new SketchPlane { OriginZ = z };
        var extruded = SolidBuilder.Extrude(loops, plane, 0.001, reversed: false, face);
        foreach (var triangle in MeshQuery.Triangles(extruded))
        {
            if (!triangle.Face.EndsWith(":bottom", StringComparison.Ordinal) && z > 0)
            {
                continue;
            }

            if (z == 0 && !triangle.Face.EndsWith(":bottom", StringComparison.Ordinal))
            {
                continue;
            }

            if (flip)
            {
                builder.Add(triangle.A, triangle.C, triangle.B, face);
            }
            else
            {
                builder.Add(triangle.A, triangle.B, triangle.C, face);
            }
        }
    }

    private static ProfileLoop Scale(ProfileLoop loop, double scale)
    {
        var copy = new ProfileLoop { Hole = loop.Hole };
        foreach (var point in loop.Points)
        {
            copy.Points.Add((point.X * scale, point.Y * scale));
        }

        return copy;
    }

    private static ProfileLoop ToothLoop(int teeth, double baseRadius, double tipRadius, double rootRadius, double pressure)
    {
        var loop = new ProfileLoop();
        var step = Math.PI * 2 / teeth;
        var invPitch = Math.Tan(pressure) - pressure;
        double Half(double radius)
        {
            var r = Math.Max(radius, baseRadius);
            var alpha = Math.Acos(Math.Clamp(baseRadius / r, -1, 1));
            return Math.PI / (2 * teeth) + invPitch - (Math.Tan(alpha) - alpha);
        }

        for (var tooth = 0; tooth < teeth; tooth++)
        {
            var center = tooth * step;
            AppendRadius(loop, center, rootRadius, tipRadius, Half, ascending: true);
            var tipStart = center - Half(tipRadius);
            var tipEnd = center + Half(tipRadius);
            for (var sample = 1; sample <= 2; sample++)
            {
                var angle = tipStart + (tipEnd - tipStart) * sample / 3;
                loop.Points.Add((Math.Cos(angle) * tipRadius, Math.Sin(angle) * tipRadius));
            }

            AppendRadius(loop, center, tipRadius, rootRadius, Half, ascending: false);
            var gapStart = center + Half(rootRadius);
            var gapEnd = center + step - Half(rootRadius);
            for (var sample = 1; sample <= 2; sample++)
            {
                var angle = gapStart + (gapEnd - gapStart) * sample / 3;
                loop.Points.Add((Math.Cos(angle) * rootRadius, Math.Sin(angle) * rootRadius));
            }
        }

        return loop;
    }

    private static void AppendRadius(
        ProfileLoop loop,
        double center,
        double fromRadius,
        double toRadius,
        Func<double, double> half,
        bool ascending)
    {
        const int steps = 6;
        for (var step = 0; step <= steps; step++)
        {
            if (!ascending && step == 0)
            {
                continue;
            }

            var t = step / (double)steps;
            var radius = fromRadius + (toRadius - fromRadius) * t;
            var angle = ascending ? center - half(radius) : center + half(radius);
            loop.Points.Add((Math.Cos(angle) * radius, Math.Sin(angle) * radius));
        }
    }

    private static ProfileLoop Circle(double radius, int steps, bool clockwise)
    {
        var loop = new ProfileLoop { Hole = true };
        for (var step = 0; step < steps; step++)
        {
            var angle = (clockwise ? -1 : 1) * step * Math.PI * 2 / steps;
            loop.Points.Add((Math.Cos(angle) * radius, Math.Sin(angle) * radius));
        }

        return loop;
    }
}
