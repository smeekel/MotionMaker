using MotionMaker.Core;

namespace MotionMaker.Core.Tests;

public class ProjectTests
{
    [Fact]
    public void JsonRoundTripKeepsAssembliesPartsAndSketch()
    {
        var project = new ProjectDocument { Name = "Walker" };
        var assembly = ProjectEdits.AddAssembly(project);
        ProjectEdits.SetPlateSize(assembly, 180, 120);
        var other = ProjectEdits.AddAssembly(project);
        ProjectEdits.SetPlateSize(other, 300, 300);
        var part = ProjectEdits.AddPart(project);
        part.Name = "Cam";
        var sketch = part.Sketches[0];
        sketch.Points.Add(new SketchPoint { X = 1, Y = 2 });
        sketch.Points.Add(new SketchPoint { X = 4, Y = 2 });
        sketch.Lines.Add(new SketchLine { StartId = sketch.Points[0].Id, EndId = sketch.Points[1].Id });
        sketch.Constraints.Add(new SketchConstraint
        {
            Kind = ConstraintKind.Horizontal,
            LineId = sketch.Lines[0].Id
        });
        assembly.Placements.Add(new Placement { PartId = part.Id, X = 12, Y = 8, RotationDegrees = 15 });

        var restored = ProjectSerializer.FromJson(ProjectSerializer.ToJson(project));

        Assert.Equal(1, restored.SchemaVersion);
        Assert.Equal("mm", restored.Units);
        Assert.Equal("Walker", restored.Name);
        Assert.Equal(2, restored.Assemblies.Count);
        Assert.Equal(180, restored.Assemblies[0].Plate.WidthMm);
        Assert.Equal(120, restored.Assemblies[0].Plate.DepthMm);
        Assert.Equal(300, restored.Assemblies[1].Plate.WidthMm);
        Assert.Equal(part.Id, restored.Assemblies[0].Placements[0].PartId);
        Assert.Equal(15, restored.Assemblies[0].Placements[0].RotationDegrees);
        Assert.Equal(45, restored.Assemblies[0].Camera.YawDegrees);
        var restoredSketch = restored.Parts.Single().Sketches.Single();
        Assert.Equal(ConstraintKind.Horizontal, restoredSketch.Constraints.Single().Kind);
        Assert.Equal(4, restoredSketch.Points[1].X);
        Assert.False(restored.Parts.Single().Body.HasSolid);
    }

    [Fact]
    public void SerializerRejectsANewerSchema()
    {
        var json = """
            {
              "schemaVersion": 9,
              "units": "mm",
              "name": "Future",
              "assemblies": [],
              "parts": []
            }
            """;

        var error = Assert.Throws<InvalidDataException>(() => ProjectSerializer.FromJson(json));
        Assert.Contains("9", error.Message);
    }

    [Fact]
    public void ReserializingIsStable()
    {
        var project = new ProjectDocument();
        ProjectEdits.AddAssembly(project);
        ProjectEdits.AddPart(project);
        var json = ProjectSerializer.ToJson(project);

        Assert.Equal(json, ProjectSerializer.ToJson(ProjectSerializer.FromJson(json)));
    }

    [Fact]
    public void AssembliesStayAbovePartsEvenWhenPartsWereCreatedFirst()
    {
        var project = new ProjectDocument();
        ProjectEdits.AddPart(project);
        ProjectEdits.AddPart(project);
        ProjectEdits.AddAssembly(project);

        var tree = ProjectTreeBuilder.Build(project);

        Assert.Equal(ProjectNodeKind.Assembly, tree[0].Kind);
        Assert.Equal(ProjectNodeKind.Part, tree[1].Kind);
        Assert.Equal(ProjectNodeKind.Part, tree[2].Kind);
        Assert.DoesNotContain(tree[0].Children, node => node.Kind == ProjectNodeKind.Part);
    }

    [Fact]
    public void BillOfMaterialsCountsPlacements()
    {
        var project = new ProjectDocument();
        var pin = ProjectEdits.AddPart(project);
        pin.Name = "Pin";
        var cam = ProjectEdits.AddPart(project);
        cam.Name = "Cam";
        var assembly = ProjectEdits.AddAssembly(project);
        assembly.Placements.Add(new Placement { PartId = pin.Id });
        assembly.Placements.Add(new Placement { PartId = pin.Id });
        assembly.Placements.Add(new Placement { PartId = cam.Id });

        var lines = BillOfMaterials.For(project, assembly);

        Assert.Equal(("Cam", 1), (lines[0].PartName, lines[0].Quantity));
        Assert.Equal(("Pin", 2), (lines[1].PartName, lines[1].Quantity));
        var bom = ProjectTreeBuilder.Build(project).First(node => node.Kind == ProjectNodeKind.Assembly)
            .Children.Single(node => node.Kind == ProjectNodeKind.Bom);
        Assert.Equal(2, bom.Children.Count);
    }

    [Fact]
    public void DeletingAPlacedPartIsBlockedAndDeletingAnAssemblyKeepsParts()
    {
        var project = new ProjectDocument();
        var part = ProjectEdits.AddPart(project);
        var assembly = ProjectEdits.AddAssembly(project);
        assembly.Placements.Add(new Placement { PartId = part.Id });

        var removed = ProjectEdits.TryRemovePart(project, part.Id, out var usedBy);

        Assert.False(removed);
        Assert.Equal(assembly.Name, usedBy);
        var spare = ProjectEdits.AddAssembly(project);
        Assert.True(ProjectEdits.RemoveAssembly(project, assembly.Id));
        Assert.Single(project.Parts);
        Assert.Equal(spare.Id, project.Assemblies.Single().Id);
        Assert.False(ProjectEdits.RemoveAssembly(project, spare.Id));
        Assert.Single(project.Assemblies);
    }

    [Fact]
    public void NewProjectStartsWithOneAssembly()
    {
        var project = ProjectEdits.CreateNew();

        Assert.Single(project.Assemblies);
        Assert.Equal(250, project.Assemblies[0].Plate.WidthMm);
        Assert.False(ProjectEdits.RemoveAssembly(project, project.Assemblies[0].Id));
    }

    [Fact]
    public void UndoRestoresPlateSize()
    {
        var history = new EditHistory(new ProjectDocument());
        var before = history.Capture();
        var assembly = ProjectEdits.AddAssembly(history.Document);
        history.Commit(before);
        before = history.Capture();
        ProjectEdits.SetPlateSize(assembly, 100, 80);
        history.Commit(before);

        history.Undo();

        Assert.Equal(250, history.Document.Assemblies.Single().Plate.WidthMm);
        Assert.Equal(210, history.Document.Assemblies.Single().Plate.DepthMm);
        history.Redo();
        Assert.Equal(100, history.Document.Assemblies.Single().Plate.WidthMm);
        Assert.True(history.IsDirty);
        history.MarkSaved();
        Assert.False(history.IsDirty);
    }

    [Fact]
    public void CameraMovementCountsAsUnsaved()
    {
        var history = new EditHistory(ProjectEdits.CreateNew());
        Assert.False(history.IsDirty);

        history.Document.Assemblies[0].Camera.YawDegrees += 15;

        Assert.True(history.IsDirty);
    }

    [Fact]
    public void SettingsRoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "motion-maker-tests", Guid.NewGuid().ToString("N"), "settings.json");
        var settings = new UserSettings
        {
            InvertWheel = true,
            OrbitSensitivity = 1.5,
            PanSensitivity = 0.8,
            SnapSpacingMm = 2,
            SnapEnabled = false,
            ShowGrid = false,
            WindowWidth = 1400,
            WindowHeight = 900
        };
        settings.RememberFile(@"C:\projects\one.mmproj");
        settings.RememberFile(@"C:\projects\two.mmproj");

        SettingsStore.Save(settings, path);
        var loaded = SettingsStore.Load(path);

        Assert.True(loaded.InvertWheel);
        Assert.Equal(1.5, loaded.OrbitSensitivity);
        Assert.Equal(2, loaded.SnapSpacingMm);
        Assert.False(loaded.SnapEnabled);
        Assert.Equal(@"C:\projects\two.mmproj", loaded.RecentFiles[0]);
        Assert.Equal(@"C:\projects\one.mmproj", loaded.RecentFiles[1]);
    }

    [Fact]
    public void SnapRoundsToTheSpacing()
    {
        var (x, y) = GridSnap.SnapPoint(10.4, -3.6, 1, enabled: true);
        Assert.Equal(10, x);
        Assert.Equal(-4, y);
        var raw = GridSnap.SnapPoint(10.4, -3.6, 1, enabled: false);
        Assert.Equal(10.4, raw.X);
    }
}
