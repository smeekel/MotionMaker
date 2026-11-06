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
}

public sealed class SketchLine
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid StartId { get; set; }

    public Guid EndId { get; set; }
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

    public List<SketchPoint> Points { get; set; } = [];

    public List<SketchLine> Lines { get; set; } = [];

    public List<SketchCircle> Circles { get; set; } = [];

    public List<SketchConstraint> Constraints { get; set; } = [];

    public Sketch Clone()
    {
        return new Sketch
        {
            Id = Id,
            Name = Name,
            Points = Points.Select(point => new SketchPoint { Id = point.Id, X = point.X, Y = point.Y }).ToList(),
            Lines = Lines.Select(line => new SketchLine { Id = line.Id, StartId = line.StartId, EndId = line.EndId }).ToList(),
            Circles = Circles.Select(circle => new SketchCircle { Id = circle.Id, CenterId = circle.CenterId, Radius = circle.Radius }).ToList(),
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

public sealed class PartBody
{
    public bool HasSolid { get; set; }
}

public sealed class PartDesign
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "Part";

    public CameraState Camera { get; set; } = new();

    public List<Sketch> Sketches { get; set; } = [];

    public PartBody Body { get; set; } = new();
}

public sealed class ProjectDocument
{
    public int SchemaVersion { get; set; } = ProjectSerializer.CurrentVersion;

    public string Units { get; set; } = "mm";

    public string Name { get; set; } = "Untitled";

    public List<AssemblyDesign> Assemblies { get; set; } = [];

    public List<PartDesign> Parts { get; set; } = [];
}
