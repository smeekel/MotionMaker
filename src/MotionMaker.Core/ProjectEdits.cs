namespace MotionMaker.Core;

public static class GridSnap
{
    public static double Snap(double value, double spacing)
    {
        if (spacing <= 1e-9)
        {
            return value;
        }

        return Math.Round(value / spacing) * spacing;
    }

    public static (double X, double Y) SnapPoint(double x, double y, double spacing, bool enabled)
    {
        if (!enabled)
        {
            return (x, y);
        }

        return (Snap(x, spacing), Snap(y, spacing));
    }
}

public static class BillOfMaterials
{
    public static IReadOnlyList<BomLine> For(ProjectDocument project, AssemblyDesign assembly)
    {
        return assembly.Placements
            .GroupBy(placement => placement.PartId)
            .Select(group =>
            {
                var name = project.Parts.FirstOrDefault(part => part.Id == group.Key)?.Name ?? "(missing part)";
                return new BomLine(group.Key, name, group.Count());
            })
            .OrderBy(line => line.PartName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public sealed record BomLine(Guid PartId, string PartName, int Quantity);

public static class ProjectEdits
{
    public static ProjectDocument CreateNew()
    {
        var document = new ProjectDocument();
        AddAssembly(document);
        return document;
    }

    public static AssemblyDesign AddAssembly(ProjectDocument document)
    {
        var assembly = new AssemblyDesign
        {
            Id = Guid.NewGuid(),
            Name = NextName(document.Assemblies.Select(item => item.Name), "Assembly"),
            Plate = new PlateSettings()
        };
        assembly.Camera = CameraState.CreateAssemblyView(assembly.Plate);
        document.Assemblies.Add(assembly);
        return assembly;
    }

    public static PartDesign AddPart(ProjectDocument document)
    {
        var part = new PartDesign
        {
            Id = Guid.NewGuid(),
            Name = NextName(document.Parts.Select(item => item.Name), "Part"),
            Camera = CameraState.CreatePartView(),
            Body = new PartBody()
        };
        var sketch = new Sketch
        {
            Id = Guid.NewGuid(),
            Name = "Sketch"
        };
        part.Sketches.Add(sketch);
        part.Features.Add(new PartFeature
        {
            Name = "Sketch",
            Kind = FeatureKind.Sketch,
            SketchId = sketch.Id
        });
        document.Parts.Add(part);
        return part;
    }

    public static bool RemoveAssembly(ProjectDocument document, Guid assemblyId)
    {
        if (document.Assemblies.Count <= 1)
        {
            return false;
        }

        var assembly = document.Assemblies.FirstOrDefault(item => item.Id == assemblyId);
        if (assembly is null)
        {
            return false;
        }

        document.Assemblies.Remove(assembly);
        return true;
    }

    public static bool TryRemovePart(ProjectDocument document, Guid partId, out string? usedByAssembly)
    {
        usedByAssembly = document.Assemblies
            .FirstOrDefault(assembly => assembly.Placements.Any(placement => placement.PartId == partId))
            ?.Name;
        if (usedByAssembly is not null)
        {
            return false;
        }

        var part = document.Parts.FirstOrDefault(item => item.Id == partId);
        if (part is null)
        {
            return false;
        }

        document.Parts.Remove(part);
        return true;
    }

    public static bool Rename(ProjectDocument document, Guid id, string name)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            return false;
        }

        var assembly = document.Assemblies.FirstOrDefault(item => item.Id == id);
        if (assembly is not null)
        {
            assembly.Name = name;
            return true;
        }

        var part = document.Parts.FirstOrDefault(item => item.Id == id);
        if (part is not null)
        {
            part.Name = name;
            return true;
        }

        return false;
    }

    public static void SetPlateSize(AssemblyDesign assembly, double widthMm, double depthMm)
    {
        assembly.Plate.WidthMm = Math.Clamp(widthMm, 10, 5000);
        assembly.Plate.DepthMm = Math.Clamp(depthMm, 10, 5000);
    }

    private static string NextName(IEnumerable<string> existing, string prefix)
    {
        var taken = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var index = 1;
        string name;
        do
        {
            name = $"{prefix} {index}";
            index++;
        }
        while (taken.Contains(name));

        return name;
    }
}
