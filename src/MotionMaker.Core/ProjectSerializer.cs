using System.Text.Json;
using System.Text.Json.Serialization;

namespace MotionMaker.Core;

public static class ProjectSerializer
{
    public const int CurrentVersion = 2;

    public static readonly Guid Magic = new("813e785f-635b-42a2-a166-78cdcbbf4b3e");

    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string ToJson(ProjectDocument document)
    {
        document.Magic = Magic;
        document.SchemaVersion = CurrentVersion;
        return JsonSerializer.Serialize(document, Options);
    }

    public static ProjectDocument FromJson(string json)
    {
        RequireMagic(json);
        var document = JsonSerializer.Deserialize<ProjectDocument>(json, Options)
            ?? throw new InvalidDataException("Project file is empty.");
        if (document.SchemaVersion is < 1 or > CurrentVersion)
        {
            throw new InvalidDataException($"Unsupported project schema version {document.SchemaVersion}.");
        }

        document.Assemblies ??= [];
        document.Parts ??= [];
        document.Units = string.IsNullOrWhiteSpace(document.Units) ? "mm" : document.Units;
        document.Name = string.IsNullOrWhiteSpace(document.Name) ? "Untitled" : document.Name;

        foreach (var assembly in document.Assemblies)
        {
            assembly.Plate ??= new PlateSettings();
            assembly.Camera ??= CameraState.CreateAssemblyView(assembly.Plate);
            assembly.Placements ??= [];
            assembly.Name = string.IsNullOrWhiteSpace(assembly.Name) ? "Assembly" : assembly.Name;
        }

        foreach (var part in document.Parts)
        {
            part.Camera ??= CameraState.CreatePartView();
            part.Sketches ??= [];
            part.Features ??= [];
            part.Body ??= new PartBody();
            part.Name = string.IsNullOrWhiteSpace(part.Name) ? "Part" : part.Name;
            foreach (var sketch in part.Sketches)
            {
                sketch.Plane ??= SketchPlane.Base();
                sketch.Points ??= [];
                sketch.Lines ??= [];
                sketch.Circles ??= [];
                sketch.Arcs ??= [];
                sketch.Slots ??= [];
                sketch.Constraints ??= [];
                sketch.Name = string.IsNullOrWhiteSpace(sketch.Name) ? "Sketch" : sketch.Name;
            }

            foreach (var feature in part.Features)
            {
                feature.Parents ??= [];
                feature.Mesh?.Positions ??= [];
                if (feature.Mesh is not null)
                {
                    feature.Mesh.Indices ??= [];
                    feature.Mesh.TriangleFaces ??= [];
                    feature.Mesh.Faces ??= [];
                }
            }

            if (part.Features.Count == 0)
            {
                foreach (var sketch in part.Sketches)
                {
                    part.Features.Add(new PartFeature
                    {
                        Name = sketch.Name,
                        Kind = FeatureKind.Sketch,
                        SketchId = sketch.Id
                    });
                }
            }

            if (part.Body.Mesh is null)
            {
                var source = part.Features.LastOrDefault(feature =>
                    feature.Kind is FeatureKind.Pad or FeatureKind.Hole or FeatureKind.Gear
                    && feature.Mesh is { Indices.Count: > 0 });
                if (source?.Mesh is not null)
                {
                    part.Body.Mesh = source.Mesh;
                    part.Body.SourceFeatureId = source.Id;
                }
            }
        }

        document.SchemaVersion = CurrentVersion;

        return document;
    }

    private static void RequireMagic(string json)
    {
        using var parsed = JsonDocument.Parse(json);
        if (parsed.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("This file is not a Motion Maker project.");
        }

        foreach (var property in parsed.RootElement.EnumerateObject())
        {
            if (!property.Name.Equals("magic", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.String
                && Guid.TryParse(property.Value.GetString(), out var magic)
                && magic == Magic)
            {
                return;
            }

            throw new InvalidDataException("This file is not a Motion Maker project.");
        }

        throw new InvalidDataException("This file is not a Motion Maker project.");
    }
}
