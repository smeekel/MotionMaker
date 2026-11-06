namespace MotionMaker.Core;

public enum ProjectNodeKind
{
    Assembly,
    Plate,
    Placements,
    Placement,
    Bom,
    BomLine,
    Part,
    Sketch,
    Line,
    Circle,
    Constraint,
    Body
}

public sealed class ProjectNode
{
    public required string Title { get; init; }

    public required ProjectNodeKind Kind { get; init; }

    public Guid OwnerId { get; init; }

    public Guid ItemId { get; init; }

    public List<ProjectNode> Children { get; init; } = [];
}

public static class ProjectTreeBuilder
{
    public static List<ProjectNode> Build(ProjectDocument project)
    {
        var nodes = new List<ProjectNode>();
        foreach (var assembly in project.Assemblies)
        {
            nodes.Add(BuildAssembly(project, assembly));
        }

        foreach (var part in project.Parts)
        {
            nodes.Add(BuildPart(part));
        }

        return nodes;
    }

    private static ProjectNode BuildAssembly(ProjectDocument project, AssemblyDesign assembly)
    {
        var placements = new ProjectNode
        {
            Title = "Placements",
            Kind = ProjectNodeKind.Placements,
            OwnerId = assembly.Id,
            ItemId = assembly.Id
        };
        foreach (var placement in assembly.Placements)
        {
            var partName = project.Parts.FirstOrDefault(part => part.Id == placement.PartId)?.Name ?? "(missing part)";
            placements.Children.Add(new ProjectNode
            {
                Title = partName,
                Kind = ProjectNodeKind.Placement,
                OwnerId = assembly.Id,
                ItemId = placement.Id
            });
        }

        var bom = new ProjectNode
        {
            Title = "Bill of materials",
            Kind = ProjectNodeKind.Bom,
            OwnerId = assembly.Id,
            ItemId = assembly.Id
        };
        foreach (var line in BillOfMaterials.For(project, assembly))
        {
            bom.Children.Add(new ProjectNode
            {
                Title = $"{line.PartName} × {line.Quantity}",
                Kind = ProjectNodeKind.BomLine,
                OwnerId = assembly.Id,
                ItemId = line.PartId
            });
        }

        return new ProjectNode
        {
            Title = assembly.Name,
            Kind = ProjectNodeKind.Assembly,
            OwnerId = assembly.Id,
            ItemId = assembly.Id,
            Children =
            [
                new ProjectNode
                {
                    Title = $"Plate {assembly.Plate.WidthMm:0.##} × {assembly.Plate.DepthMm:0.##} mm",
                    Kind = ProjectNodeKind.Plate,
                    OwnerId = assembly.Id,
                    ItemId = assembly.Id
                },
                placements,
                bom
            ]
        };
    }

    private static ProjectNode BuildPart(PartDesign part)
    {
        var sketch = part.Sketches.FirstOrDefault();
        var sketchNode = new ProjectNode
        {
            Title = sketch?.Name ?? "Sketch",
            Kind = ProjectNodeKind.Sketch,
            OwnerId = part.Id,
            ItemId = sketch?.Id ?? part.Id
        };
        if (sketch is not null)
        {
            var lineIndex = 1;
            foreach (var line in sketch.Lines)
            {
                sketchNode.Children.Add(new ProjectNode
                {
                    Title = $"Line {lineIndex}",
                    Kind = ProjectNodeKind.Line,
                    OwnerId = part.Id,
                    ItemId = line.Id
                });
                lineIndex++;
            }

            var circleIndex = 1;
            foreach (var circle in sketch.Circles)
            {
                sketchNode.Children.Add(new ProjectNode
                {
                    Title = $"Circle {circleIndex}",
                    Kind = ProjectNodeKind.Circle,
                    OwnerId = part.Id,
                    ItemId = circle.Id
                });
                circleIndex++;
            }

            foreach (var constraint in sketch.Constraints)
            {
                sketchNode.Children.Add(new ProjectNode
                {
                    Title = Describe(constraint),
                    Kind = ProjectNodeKind.Constraint,
                    OwnerId = part.Id,
                    ItemId = constraint.Id
                });
            }
        }

        return new ProjectNode
        {
            Title = part.Name,
            Kind = ProjectNodeKind.Part,
            OwnerId = part.Id,
            ItemId = part.Id,
            Children =
            [
                sketchNode,
                new ProjectNode
                {
                    Title = part.Body.HasSolid ? "Body" : "Body (empty)",
                    Kind = ProjectNodeKind.Body,
                    OwnerId = part.Id,
                    ItemId = part.Id
                }
            ]
        };
    }

    public static string Describe(SketchConstraint constraint)
    {
        return constraint.Kind switch
        {
            ConstraintKind.Horizontal => "Horizontal",
            ConstraintKind.Vertical => "Vertical",
            ConstraintKind.Coincident => "Coincident",
            ConstraintKind.Length => $"Length {constraint.Value:0.##}",
            ConstraintKind.Radius => $"Radius {constraint.Value:0.##}",
            _ => constraint.Kind.ToString()
        };
    }
}
