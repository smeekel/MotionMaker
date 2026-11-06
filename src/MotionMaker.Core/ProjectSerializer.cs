using System.Text.Json;
using System.Text.Json.Serialization;

namespace MotionMaker.Core;

public static class ProjectSerializer
{
    public const int CurrentVersion = 1;

    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string ToJson(ProjectDocument document)
        => JsonSerializer.Serialize(document, Options);

    public static ProjectDocument FromJson(string json)
    {
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
            part.Body ??= new PartBody();
            part.Name = string.IsNullOrWhiteSpace(part.Name) ? "Part" : part.Name;
            foreach (var sketch in part.Sketches)
            {
                sketch.Points ??= [];
                sketch.Lines ??= [];
                sketch.Circles ??= [];
                sketch.Constraints ??= [];
                sketch.Name = string.IsNullOrWhiteSpace(sketch.Name) ? "Sketch" : sketch.Name;
            }
        }

        return document;
    }
}
