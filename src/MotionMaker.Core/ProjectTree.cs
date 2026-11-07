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
    Slot,
    Pad,
    Hole,
    Gear,
    Body
}

public sealed class ProjectNode
{
    public required string Title { get; init; }

    public required ProjectNodeKind Kind { get; init; }

    public Guid OwnerId { get; init; }

    public Guid ItemId { get; init; }

    public string Icon { get; init; } = "";

    public bool Frozen { get; init; }

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
            ItemId = assembly.Id,
            Icon = "placements"
        };
        foreach (var placement in assembly.Placements)
        {
            var partName = project.Parts.FirstOrDefault(part => part.Id == placement.PartId)?.Name ?? "(missing part)";
            placements.Children.Add(new ProjectNode
            {
                Title = partName,
                Kind = ProjectNodeKind.Placement,
                OwnerId = assembly.Id,
                ItemId = placement.Id,
                Icon = "part"
            });
        }

        var bom = new ProjectNode
        {
            Title = "Bill of materials",
            Kind = ProjectNodeKind.Bom,
            OwnerId = assembly.Id,
            ItemId = assembly.Id,
            Icon = "bom"
        };
        foreach (var line in BillOfMaterials.For(project, assembly))
        {
            bom.Children.Add(new ProjectNode
            {
                Title = $"{line.PartName} × {line.Quantity}",
                Kind = ProjectNodeKind.BomLine,
                OwnerId = assembly.Id,
                ItemId = line.PartId,
                Icon = "bom"
            });
        }

        return new ProjectNode
        {
            Title = assembly.Name,
            Kind = ProjectNodeKind.Assembly,
            OwnerId = assembly.Id,
            ItemId = assembly.Id,
            Icon = "assembly",
            Children =
            [
                new ProjectNode
                {
                    Title = $"Plate {assembly.Plate.WidthMm:0.##} × {assembly.Plate.DepthMm:0.##} mm",
                    Kind = ProjectNodeKind.Plate,
                    OwnerId = assembly.Id,
                    ItemId = assembly.Id,
                    Icon = "plate"
                },
                placements,
                bom
            ]
        };
    }

    private static ProjectNode BuildPart(PartDesign part)
    {
        var children = new List<ProjectNode>();
        var consumed = new HashSet<Guid>();
        foreach (var feature in part.Features.Where(feature => feature.Parents.Count == 0))
        {
            if (consumed.Add(feature.Id))
            {
                children.Add(BuildFeature(part, feature, consumed));
            }
        }

        foreach (var feature in part.Features)
        {
            if (consumed.Add(feature.Id))
            {
                children.Add(BuildFeature(part, feature, consumed));
            }
        }

        children.Add(new ProjectNode
        {
            Title = part.Body.HasSolid ? "Body" : "Body (empty)",
            Kind = ProjectNodeKind.Body,
            OwnerId = part.Id,
            ItemId = part.Id,
            Icon = "body"
        });

        return new ProjectNode
        {
            Title = part.Name,
            Kind = ProjectNodeKind.Part,
            OwnerId = part.Id,
            ItemId = part.Id,
            Icon = "part",
            Children = children
        };
    }

    private static ProjectNode BuildFeature(PartDesign part, PartFeature feature, HashSet<Guid> consumed)
    {
        var node = new ProjectNode
        {
            Title = feature.Name,
            Kind = KindOf(feature.Kind),
            OwnerId = part.Id,
            ItemId = feature.Id,
            Icon = IconOf(feature.Kind),
            Frozen = feature.State == FeatureState.Frozen
        };
        if (feature.Kind == FeatureKind.Sketch && feature.SketchId is Guid sketchId)
        {
            var sketch = part.Sketches.FirstOrDefault(item => item.Id == sketchId);
            if (sketch is not null)
            {
                AddSketchEntities(node, part.Id, sketch);
            }
        }

        foreach (var child in part.Features.Where(item => item.Parents.Contains(feature.Id)))
        {
            if (consumed.Add(child.Id))
            {
                node.Children.Add(BuildFeature(part, child, consumed));
            }
        }

        return node;
    }

    private static void AddSketchEntities(ProjectNode sketchNode, Guid partId, Sketch sketch)
    {
        var lineIndex = 1;
        foreach (var line in sketch.Lines.Where(line => line.SlotId is null))
        {
            sketchNode.Children.Add(new ProjectNode
            {
                Title = $"Line {lineIndex}",
                Kind = ProjectNodeKind.Line,
                OwnerId = partId,
                ItemId = line.Id,
                Icon = "line"
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
                OwnerId = partId,
                ItemId = circle.Id,
                Icon = "circle"
            });
            circleIndex++;
        }

        var slotIndex = 1;
        foreach (var slot in sketch.Slots)
        {
            sketchNode.Children.Add(new ProjectNode
            {
                Title = $"Slot {slotIndex}",
                Kind = ProjectNodeKind.Slot,
                OwnerId = partId,
                ItemId = slot.Id,
                Icon = "slot"
            });
            slotIndex++;
        }

        foreach (var constraint in sketch.Constraints)
        {
            sketchNode.Children.Add(new ProjectNode
            {
                Title = Describe(constraint),
                Kind = ProjectNodeKind.Constraint,
                OwnerId = partId,
                ItemId = constraint.Id,
                Icon = "constraint"
            });
        }
    }

    private static ProjectNodeKind KindOf(FeatureKind kind) => kind switch
    {
        FeatureKind.Pad => ProjectNodeKind.Pad,
        FeatureKind.Hole => ProjectNodeKind.Hole,
        FeatureKind.Gear => ProjectNodeKind.Gear,
        _ => ProjectNodeKind.Sketch
    };

    private static string IconOf(FeatureKind kind) => kind switch
    {
        FeatureKind.Pad => "pad",
        FeatureKind.Hole => "hole",
        FeatureKind.Gear => "gear",
        _ => "sketch"
    };

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
