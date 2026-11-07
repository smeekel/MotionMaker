namespace MotionMaker.Core;

public sealed class PlateSettings
{
    public double WidthMm { get; set; } = 250;

    public double DepthMm { get; set; } = 210;

    public double MinorSpacingMm { get; set; } = 10;

    public double MajorSpacingMm { get; set; } = 50;
}

public sealed class Placement
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PartId { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Z { get; set; }

    public double RotationDegrees { get; set; }
}

public sealed class AssemblyDesign
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "Assembly";

    public PlateSettings Plate { get; set; } = new();

    public CameraState Camera { get; set; } = new();

    public List<Placement> Placements { get; set; } = [];
}

public enum ConstraintKind
{
    Horizontal,
    Vertical,
    Coincident,
    Length,
    Radius
}

public sealed class SketchPoint
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public double X { get; set; }

    public double Y { get; set; }

    public Guid? SlotId { get; set; }

    public bool Construction { get; set; }
}

public sealed class SketchLine
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid StartId { get; set; }

    public Guid EndId { get; set; }

    public Guid? SlotId { get; set; }
}

public sealed class SketchArc
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CenterId { get; set; }

    public Guid StartId { get; set; }

    public Guid EndId { get; set; }

    public bool Clockwise { get; set; }

    public Guid? SlotId { get; set; }
}

public sealed class SketchSlot
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public double CenterX { get; set; }

    public double CenterY { get; set; }

    public double Length { get; set; }

    public double Width { get; set; }

    public double AngleDegrees { get; set; }

    public Guid CenterAId { get; set; }

    public Guid CenterBId { get; set; }

    public Guid JunctionAId { get; set; }

    public Guid JunctionBId { get; set; }

    public Guid JunctionCId { get; set; }

    public Guid JunctionDId { get; set; }

    public Guid LineAId { get; set; }

    public Guid LineBId { get; set; }

    public Guid ArcAId { get; set; }

    public Guid ArcBId { get; set; }
}

public sealed class SketchPlane
{
    public double OriginX { get; set; }

    public double OriginY { get; set; }

    public double OriginZ { get; set; }

    public double NormalX { get; set; }

    public double NormalY { get; set; }

    public double NormalZ { get; set; } = 1;

    public double AxisX { get; set; } = 1;

    public double AxisY { get; set; }

    public double AxisZ { get; set; }

    public Vec3 Origin => new(OriginX, OriginY, OriginZ);

    public Vec3 Normal => new(NormalX, NormalY, NormalZ);

    public Vec3 AxisU => new(AxisX, AxisY, AxisZ);

    public Vec3 AxisV => Vec3.Cross(Normal, AxisU).Normalized();

    public static SketchPlane Base() => new();

    public SketchPlane Clone() => new()
    {
        OriginX = OriginX,
        OriginY = OriginY,
        OriginZ = OriginZ,
        NormalX = NormalX,
        NormalY = NormalY,
        NormalZ = NormalZ,
        AxisX = AxisX,
        AxisY = AxisY,
        AxisZ = AxisZ
    };

    public Vec3 ToWorld(double u, double v, double w = 0)
        => Origin + AxisU * u + AxisV * v + Normal * w;

    public void ToLocal(Vec3 world, out double u, out double v)
    {
        var delta = world - Origin;
        u = Vec3.Dot(delta, AxisU);
        v = Vec3.Dot(delta, AxisV);
    }

    public static SketchPlane FromAxes(Vec3 origin, Vec3 normal, Vec3 axisU)
    {
        var unitNormal = normal.Normalized();
        var axis = axisU - unitNormal * Vec3.Dot(axisU, unitNormal);
        if (axis.Length < 1e-8)
        {
            axis = Math.Abs(unitNormal.Z) < 0.9
                ? Vec3.Cross(unitNormal, new Vec3(0, 0, 1))
                : Vec3.Cross(unitNormal, new Vec3(1, 0, 0));
        }

        axis = axis.Normalized();
        return new SketchPlane
        {
            OriginX = origin.X,
            OriginY = origin.Y,
            OriginZ = origin.Z,
            NormalX = unitNormal.X,
            NormalY = unitNormal.Y,
            NormalZ = unitNormal.Z,
            AxisX = axis.X,
            AxisY = axis.Y,
            AxisZ = axis.Z
        };
    }
}

public sealed class SketchCircle
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CenterId { get; set; }

    public double Radius { get; set; }
}

public sealed class SketchConstraint
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public ConstraintKind Kind { get; set; }

    public Guid? LineId { get; set; }

    public Guid? CircleId { get; set; }

    public Guid? PointAId { get; set; }

    public Guid? PointBId { get; set; }

    public double Value { get; set; }
}

public sealed class Sketch
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "Sketch";

    public SketchPlane Plane { get; set; } = new();

    public string? SupportFaceId { get; set; }

    public Guid? SupportFeatureId { get; set; }

    public List<SketchPoint> Points { get; set; } = [];

    public List<SketchLine> Lines { get; set; } = [];

    public List<SketchCircle> Circles { get; set; } = [];

    public List<SketchArc> Arcs { get; set; } = [];

    public List<SketchSlot> Slots { get; set; } = [];

    public List<SketchConstraint> Constraints { get; set; } = [];

    public Sketch Clone()
    {
        return new Sketch
        {
            Id = Id,
            Name = Name,
            Plane = Plane.Clone(),
            SupportFaceId = SupportFaceId,
            SupportFeatureId = SupportFeatureId,
            Points = Points.Select(point => new SketchPoint
            {
                Id = point.Id,
                X = point.X,
                Y = point.Y,
                SlotId = point.SlotId,
                Construction = point.Construction
            }).ToList(),
            Lines = Lines.Select(line => new SketchLine
            {
                Id = line.Id,
                StartId = line.StartId,
                EndId = line.EndId,
                SlotId = line.SlotId
            }).ToList(),
            Circles = Circles.Select(circle => new SketchCircle
            {
                Id = circle.Id,
                CenterId = circle.CenterId,
                Radius = circle.Radius
            }).ToList(),
            Arcs = Arcs.Select(arc => new SketchArc
            {
                Id = arc.Id,
                CenterId = arc.CenterId,
                StartId = arc.StartId,
                EndId = arc.EndId,
                Clockwise = arc.Clockwise,
                SlotId = arc.SlotId
            }).ToList(),
            Slots = Slots.Select(slot => new SketchSlot
            {
                Id = slot.Id,
                CenterX = slot.CenterX,
                CenterY = slot.CenterY,
                Length = slot.Length,
                Width = slot.Width,
                AngleDegrees = slot.AngleDegrees,
                CenterAId = slot.CenterAId,
                CenterBId = slot.CenterBId,
                JunctionAId = slot.JunctionAId,
                JunctionBId = slot.JunctionBId,
                JunctionCId = slot.JunctionCId,
                JunctionDId = slot.JunctionDId,
                LineAId = slot.LineAId,
                LineBId = slot.LineBId,
                ArcAId = slot.ArcAId,
                ArcBId = slot.ArcBId
            }).ToList(),
            Constraints = Constraints.Select(constraint => new SketchConstraint
            {
                Id = constraint.Id,
                Kind = constraint.Kind,
                LineId = constraint.LineId,
                CircleId = constraint.CircleId,
                PointAId = constraint.PointAId,
                PointBId = constraint.PointBId,
                Value = constraint.Value
            }).ToList()
        };
    }
}

public enum FeatureKind
{
    Sketch,
    Pad,
    Hole,
    Gear
}

public enum FeatureState
{
    Current,
    Frozen
}

public enum GearKind
{
    Spur,
    Bevel
}

public sealed class MeshFace
{
    public string Id { get; set; } = "";

    public double OriginX { get; set; }

    public double OriginY { get; set; }

    public double OriginZ { get; set; }

    public double NormalX { get; set; }

    public double NormalY { get; set; }

    public double NormalZ { get; set; } = 1;

    public double AxisX { get; set; } = 1;

    public double AxisY { get; set; }

    public double AxisZ { get; set; }
}

public sealed class MeshData
{
    public List<double> Positions { get; set; } = [];

    public List<int> Indices { get; set; } = [];

    public List<string> TriangleFaces { get; set; } = [];

    public List<MeshFace> Faces { get; set; } = [];
}

public sealed class PartFeature
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public FeatureKind Kind { get; set; }

    public FeatureState State { get; set; } = FeatureState.Current;

    public string? Error { get; set; }

    public List<Guid> Parents { get; set; } = [];

    public Guid? SketchId { get; set; }

    public double DistanceMm { get; set; } = 10;

    public bool Reversed { get; set; }

    public double DepthMm { get; set; } = 10;

    public bool ThroughAll { get; set; }

    public GearKind GearType { get; set; }

    public double ModuleMm { get; set; } = 1;

    public int Teeth { get; set; } = 20;

    public double PressureAngleDegrees { get; set; } = 20;

    public double FaceWidthMm { get; set; } = 10;

    public double BoreMm { get; set; } = 6;

    public double PitchAngleDegrees { get; set; } = 45;

    public string? ParameterStamp { get; set; }

    public string? ConsumedParentStamp { get; set; }

    public string? ConsumedHistoryStamp { get; set; }

    public string? ResultStamp { get; set; }

    public int ComputeCount { get; set; }

    public MeshData? Mesh { get; set; }
}

public sealed class PartBody
{
    public MeshData? Mesh { get; set; }

    public Guid? SourceFeatureId { get; set; }

    public bool HasSolid => Mesh is { Indices.Count: > 0 };
}

public sealed class PartDesign
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "Part";

    public CameraState Camera { get; set; } = new();

    public List<Sketch> Sketches { get; set; } = [];

    public List<PartFeature> Features { get; set; } = [];

    public PartBody Body { get; set; } = new();
}

public sealed class ProjectDocument
{
    public Guid Magic { get; set; } = ProjectSerializer.Magic;

    public int SchemaVersion { get; set; } = ProjectSerializer.CurrentVersion;

    public string Units { get; set; } = "mm";

    public string Name { get; set; } = "Untitled";

    public List<AssemblyDesign> Assemblies { get; set; } = [];

    public List<PartDesign> Parts { get; set; } = [];
}
