using MotionMaker.Core;
using MotionMaker.Geometry;
using MotionMaker.Viewport;

namespace MotionMaker.App;

public enum SketchTool
{
    Select,
    Line,
    Rectangle,
    Circle,
    Slot
}

public readonly record struct MarkerAnchor(string Text, double X, double Y, double Z = 0);

public readonly record struct ErrorBubble(Guid FeatureId, string Message, double X, double Y, double Z);

public sealed class Session
{
    private readonly HashSet<Guid> _highlight = [];
    private readonly List<Guid> _selectedPoints = [];
    private readonly Dictionary<Guid, string> _dismissed = [];
    private string? _gestureBefore;
    private Guid? _dragPointId;
    private Dictionary<Guid, (double X, double Y)>? _dragPoints;
    private Dictionary<Guid, (double Cx, double Cy, double Length, double Width, double Angle)>? _dragSlots;
    private double _dragOriginX;
    private double _dragOriginY;
    private bool _marquee;
    private bool _marqueeShift;
    private double _marqueeX1;
    private double _marqueeY1;
    private double _marqueeX2;
    private double _marqueeY2;
    private bool _preview;
    private double _x1;
    private double _y1;
    private double _x2;
    private double _y2;
    private SketchTool _previewTool;
    private Guid? _coincidentAnchor;
    private string? _pickedFaceId;

    public Session(UserSettings settings)
    {
        Settings = settings;
        History = new EditHistory(ProjectEdits.CreateNew());
        SelectFirst();
    }

    public EditHistory History { get; private set; }

    public UserSettings Settings { get; }

    public ProjectDocument Document => History.Document;

    public string? FilePath { get; private set; }

    public bool HasSelection { get; private set; }

    public ProjectNodeKind SelectedKind { get; private set; }

    public Guid OwnerId { get; private set; }

    public Guid ItemId { get; private set; }

    public IReadOnlyList<Guid> SelectedPointIds => _selectedPoints;

    public Guid? SelectedPointId => _selectedPoints.Count > 0 ? _selectedPoints[^1] : null;

    public SketchTool Tool { get; private set; } = SketchTool.Select;

    public string? StatusMessage { get; private set; }

    public AssemblyDesign? SelectedAssembly
        => IsAssemblyKind(SelectedKind) ? Document.Assemblies.FirstOrDefault(item => item.Id == OwnerId) : null;

    public PartDesign? SelectedPart
        => IsPartKind(SelectedKind) ? Document.Parts.FirstOrDefault(item => item.Id == OwnerId) : null;

    public bool EditingSketch => ActiveSketch is not null;

    public bool HasMarquee => _marquee;

    public (double X1, double Y1, double X2, double Y2) Marquee => (_marqueeX1, _marqueeY1, _marqueeX2, _marqueeY2);

    public bool HasPickedFace => _pickedFaceId is not null;

    public string? PickedFaceId => _pickedFaceId;

    public bool CanPad => CommandSketch is Sketch profile && FeatureGraph.HasClosedProfile(profile);

    public Sketch? ActiveSketch
    {
        get
        {
            var part = SelectedPart;
            if (part is null)
            {
                return null;
            }

            var selected = SketchFromSelection(part);
            if (selected is not null)
            {
                return selected;
            }

            return Tool == SketchTool.Select ? null : part.Sketches.FirstOrDefault();
        }
    }

    public Sketch? CommandSketch
    {
        get
        {
            if (ActiveSketch is Sketch active)
            {
                return active;
            }

            var part = SelectedPart;
            if (part is null)
            {
                return null;
            }

            if (SelectedKind is ProjectNodeKind.Pad or ProjectNodeKind.Hole)
            {
                var feature = part.Features.FirstOrDefault(item => item.Id == ItemId);
                if (feature?.SketchId is Guid sketchId)
                {
                    return part.Sketches.FirstOrDefault(sketch => sketch.Id == sketchId);
                }
            }

            return part.Sketches.FirstOrDefault();
        }
    }

    public PartFeature? SelectedFeature
        => SelectedPart?.Features.FirstOrDefault(feature => feature.Id == ItemId);

    public CameraState? ActiveCamera => SelectedPart?.Camera ?? SelectedAssembly?.Camera;

    public string DisplayName => string.IsNullOrWhiteSpace(Document.Name) ? "Untitled" : Document.Name;

    public event Action? ModelChanged;

    public event Action? ViewChanged;

    public void NewProject()
    {
        History.Reset(ProjectEdits.CreateNew());
        FilePath = null;
        _dismissed.Clear();
        _pickedFaceId = null;
        SelectFirst();
        NotifyModel();
    }

    public void LoadFile(string path)
    {
        var document = ProjectSerializer.FromJson(File.ReadAllText(path));
        if (document.Assemblies.Count == 0)
        {
            ProjectEdits.AddAssembly(document);
        }

        History.Reset(document);
        FilePath = path;
        _dismissed.Clear();
        _pickedFaceId = null;
        Settings.RememberFile(path);
        SettingsStore.Save(Settings);
        SelectFirst();
        NotifyModel();
    }

    public void SaveFile(string path)
    {
        if (Document.Name == "Untitled")
        {
            Document.Name = Path.GetFileNameWithoutExtension(path);
        }

        File.WriteAllText(path, History.Capture());
        FilePath = path;
        History.MarkSaved();
        Settings.RememberFile(path);
        SettingsStore.Save(Settings);
        NotifyModel();
    }

    public void AddAssembly()
    {
        var before = History.Capture();
        var assembly = ProjectEdits.AddAssembly(Document);
        History.Commit(before);
        Select(ProjectNodeKind.Assembly, assembly.Id, assembly.Id);
    }

    public void AddPart()
    {
        var before = History.Capture();
        var part = ProjectEdits.AddPart(Document);
        History.Commit(before);
        Select(ProjectNodeKind.Part, part.Id, part.Id);
    }

    public bool TryDelete(out string? error)
    {
        error = null;
        if (!HasSelection)
        {
            return false;
        }

        var before = History.Capture();
        switch (SelectedKind)
        {
            case ProjectNodeKind.Assembly:
                if (!ProjectEdits.RemoveAssembly(Document, OwnerId))
                {
                    error = "A project must keep at least one assembly.";
                    return false;
                }

                History.Commit(before);
                SelectFirst();
                NotifyModel();
                return true;
            case ProjectNodeKind.Part:
                if (!ProjectEdits.TryRemovePart(Document, OwnerId, out var usedBy))
                {
                    error = $"\"{SelectedPart?.Name}\" is placed in {usedBy}. Remove that placement before deleting the part.";
                    return false;
                }

                History.Commit(before);
                SelectFirst();
                NotifyModel();
                return true;
            case ProjectNodeKind.Line:
            case ProjectNodeKind.Circle:
            case ProjectNodeKind.Slot:
                if (SelectedPart is PartDesign geometryPart && ActiveSketch is Sketch sketch)
                {
                    if (SelectedKind == ProjectNodeKind.Slot)
                    {
                        SketchEditor.DeleteSlot(sketch, ItemId);
                    }
                    else
                    {
                        var kind = SelectedKind == ProjectNodeKind.Line ? SketchHitKind.Line : SketchHitKind.Circle;
                        SketchEditor.Delete(sketch, new SketchHit(kind, ItemId));
                    }

                    if (!CommitSolved(geometryPart, sketch, before))
                    {
                        return true;
                    }

                    SelectSketch(geometryPart, sketch, keepPoints: false);
                }

                return true;
            case ProjectNodeKind.Constraint:
                if (SelectedPart is PartDesign constraintPart && ActiveSketch is Sketch ownerSketch)
                {
                    ownerSketch.Constraints.RemoveAll(constraint => constraint.Id == ItemId);
                    if (!CommitSolved(constraintPart, ownerSketch, before))
                    {
                        return true;
                    }

                    SelectSketch(constraintPart, ownerSketch, keepPoints: false);
                }

                return true;
            case ProjectNodeKind.Sketch:
                if (SelectedPart is PartDesign sketchPart && SelectedFeature is PartFeature sketchFeature)
                {
                    if (sketchFeature.Parents.Count > 0)
                    {
                        RemoveFeatureTree(sketchPart, sketchFeature.Id, removeOwnSketch: true);
                        FeatureGraph.Recompute(sketchPart);
                        History.Commit(before);
                        Select(ProjectNodeKind.Part, sketchPart.Id, sketchPart.Id);
                    }
                    else if (ActiveSketch is Sketch clear)
                    {
                        clear.Points.Clear();
                        clear.Lines.Clear();
                        clear.Circles.Clear();
                        clear.Arcs.Clear();
                        clear.Slots.Clear();
                        clear.Constraints.Clear();
                        FeatureGraph.Recompute(sketchPart);
                        History.Commit(before);
                        NotifyModel();
                    }
                }

                return true;
            case ProjectNodeKind.Pad:
            case ProjectNodeKind.Hole:
            case ProjectNodeKind.Gear:
                if (SelectedPart is PartDesign solidPart && SelectedFeature is not null)
                {
                    RemoveFeatureTree(solidPart, ItemId, removeOwnSketch: false);
                    FeatureGraph.Recompute(solidPart);
                    History.Commit(before);
                    Select(ProjectNodeKind.Part, solidPart.Id, solidPart.Id);
                }

                return true;
            default:
                return false;
        }
    }

    public bool RenameSelection(string name)
    {
        if (!HasSelection || SelectedKind is not (ProjectNodeKind.Assembly or ProjectNodeKind.Part))
        {
            return false;
        }

        var before = History.Capture();
        if (!ProjectEdits.Rename(Document, OwnerId, name))
        {
            return false;
        }

        History.Commit(before);
        NotifyModel();
        return true;
    }

    public void SetPlateSize(double width, double depth)
    {
        if (SelectedAssembly is not AssemblyDesign assembly)
        {
            return;
        }

        var before = History.Capture();
        ProjectEdits.SetPlateSize(assembly, width, depth);
        History.Commit(before);
        NotifyModel();
    }

    public void Select(ProjectNode node)
    {
        if (HasSelection && SelectedKind == node.Kind && OwnerId == node.OwnerId && ItemId == node.ItemId)
        {
            return;
        }

        _selectedPoints.Clear();
        Select(node.Kind, node.OwnerId, node.ItemId);
    }

    public void Undo()
    {
        History.Undo();
        EnsureSelection();
        NotifyModel();
    }

    public void Redo()
    {
        History.Redo();
        EnsureSelection();
        NotifyModel();
    }

    public void TogglePerspective()
    {
        if (ActiveCamera is not CameraState camera)
        {
            return;
        }

        camera.Perspective = !camera.Perspective;
        ViewChanged?.Invoke();
    }

    public void ApplyPreset(ViewPreset preset)
    {
        if (ActiveCamera is not CameraState camera)
        {
            return;
        }

        CameraMath.SetPreset(camera, preset);
        if (SelectedPart is not null && preset == ViewPreset.Top)
        {
            camera.Perspective = false;
        }

        ViewChanged?.Invoke();
    }

    public void Frame(double viewWidth, double viewHeight)
    {
        if (SelectedAssembly is AssemblyDesign assembly)
        {
            CameraMath.FrameBounds(
                assembly.Camera,
                0,
                0,
                0,
                assembly.Plate.WidthMm,
                assembly.Plate.DepthMm,
                0,
                viewWidth,
                viewHeight);
        }
        else if (SelectedPart is PartDesign part)
        {
            if (part.Body.Mesh is MeshData mesh)
            {
                var (min, max) = MeshQuery.Bounds(mesh);
                CameraMath.FrameBounds(part.Camera, min.X, min.Y, min.Z, max.X, max.Y, max.Z, viewWidth, viewHeight);
            }
            else
            {
                part.Camera.Perspective = false;
                CameraMath.SetPreset(part.Camera, ViewPreset.Top);
                var bounds = SketchBounds(part.Sketches.FirstOrDefault());
                CameraMath.FrameBounds(part.Camera, bounds.MinX, bounds.MinY, 0, bounds.MaxX, bounds.MaxY, 0, viewWidth, viewHeight);
            }
        }
        else
        {
            return;
        }

        ViewChanged?.Invoke();
    }

    public void SetTool(SketchTool tool)
    {
        Tool = tool;
        _preview = false;
        _coincidentAnchor = null;
        if (tool != SketchTool.Select)
        {
            _dragPointId = null;
        }

        StatusMessage = ToolHint();
        ViewChanged?.Invoke();
    }

    public void CancelGesture()
    {
        _preview = false;
        _coincidentAnchor = null;
        _dragPointId = null;
        _marquee = false;
        _gestureBefore = null;
        StatusMessage = null;
        ViewChanged?.Invoke();
    }

    public void BeginCoincident()
    {
        if (ActiveSketch is not Sketch sketch || SelectedPart is not PartDesign part)
        {
            StatusMessage = "Select a point first.";
            NotifyModel();
            return;
        }

        if (_selectedPoints.Count >= 2)
        {
            var before = History.Capture();
            if (SketchEditor.AddCoincidentChain(sketch, _selectedPoints) == 0)
            {
                StatusMessage = "Those points are already coincident.";
                NotifyModel();
                return;
            }

            if (CommitSolved(part, sketch, before))
            {
                NotifyModel();
            }

            return;
        }

        if (SelectedPointId is not Guid pointId)
        {
            StatusMessage = "Select a point first.";
            NotifyModel();
            return;
        }

        _coincidentAnchor = pointId;
        StatusMessage = "Click the point to join.";
        NotifyModel();
    }

    public void AddPad()
    {
        var part = SelectedPart;
        var sketch = CommandSketch;
        if (part is null || sketch is null)
        {
            return;
        }

        if (!FeatureGraph.HasClosedProfile(sketch))
        {
            StatusMessage = ProfileBuilder.OpenProfileMessage;
            NotifyModel();
            return;
        }

        var before = History.Capture();
        var feature = FeatureGraph.AddPad(part, sketch);
        FeatureGraph.Recompute(part);
        History.Commit(before);
        StatusMessage = feature.Error;
        Select(ProjectNodeKind.Pad, part.Id, feature.Id);
    }

    public void AddHole()
    {
        var part = SelectedPart;
        var sketch = CommandSketch;
        if (part is null || sketch is null)
        {
            return;
        }

        if (!FeatureGraph.HasClosedProfile(sketch))
        {
            StatusMessage = ProfileBuilder.OpenProfileMessage;
            NotifyModel();
            return;
        }

        var before = History.Capture();
        var feature = FeatureGraph.AddHole(part, sketch);
        FeatureGraph.Recompute(part);
        History.Commit(before);
        StatusMessage = feature.Error;
        Select(ProjectNodeKind.Hole, part.Id, feature.Id);
    }

    public void AddFaceSketch()
    {
        var part = SelectedPart;
        if (part is null || _pickedFaceId is null)
        {
            StatusMessage = "Select a planar face first.";
            NotifyModel();
            return;
        }

        var support = part.Features.LastOrDefault(feature => feature.Mesh?.Faces.Any(face => face.Id == _pickedFaceId) == true);
        if (support is null)
        {
            StatusMessage = "Select a planar face first.";
            NotifyModel();
            return;
        }

        var before = History.Capture();
        var feature = FeatureGraph.AddFaceSketch(part, support, _pickedFaceId);
        FeatureGraph.Recompute(part);
        History.Commit(before);
        Select(ProjectNodeKind.Sketch, part.Id, feature.Id);
        if (ActiveCamera is CameraState camera)
        {
            var sketch = part.Sketches.FirstOrDefault(item => item.Id == feature.SketchId);
            if (sketch is not null)
            {
                CameraMath.LookAtPlane(camera, sketch.Plane);
            }
        }

        SetTool(SketchTool.Select);
    }

    public void AddGear(GearKind kind, double module, int teeth, double pressure, double faceWidth, double bore, double pitchAngle)
    {
        if (SelectedPart is null)
        {
            return;
        }

        var before = History.Capture();
        var part = SelectedPart;
        if (part.Features.Any(feature => feature.Kind is FeatureKind.Pad or FeatureKind.Hole or FeatureKind.Gear))
        {
            part = ProjectEdits.AddPart(Document);
        }

        var feature = FeatureGraph.AddGear(part, kind, module, teeth, pressure, faceWidth, bore, pitchAngle);
        FeatureGraph.Recompute(part);
        History.Commit(before);
        StatusMessage = feature.Error;
        Select(ProjectNodeKind.Gear, part.Id, feature.Id);
    }

    public void AddDefaultGear() => AddGear(GearKind.Spur, 1, 20, 20, 10, 6, 45);

    public void SetPoint(double x, double y)
    {
        if (ActiveSketch is not Sketch sketch || SelectedPart is not PartDesign part || _selectedPoints.Count == 0)
        {
            return;
        }

        var points = sketch.Points.Where(point => _selectedPoints.Contains(point.Id)).ToList();
        if (points.Count == 0 || points.Any(point => Math.Abs(point.X - x) > 1e-6 || Math.Abs(point.Y - y) > 1e-6) == false)
        {
            return;
        }

        var before = History.Capture();
        var pins = points.Select(point => new SketchSolver.PinnedPoint(point.Id, x, y)).ToList();
        if (!ApplyPins(part, sketch, before, pins))
        {
            return;
        }

        NotifyModel();
    }

    public void SetSlot(double length, double width, double angle)
    {
        if (ActiveSketch is not Sketch sketch || SelectedPart is not PartDesign part)
        {
            return;
        }

        var slot = sketch.Slots.FirstOrDefault(item => item.Id == ItemId);
        if (slot is null || (Math.Abs(slot.Length - length) < 1e-6 && Math.Abs(slot.Width - width) < 1e-6 && Math.Abs(slot.AngleDegrees - angle) < 1e-6))
        {
            return;
        }

        if (width < 0.5 || length < width + 0.5)
        {
            StatusMessage = "The slot length has to clear its width.";
            NotifyModel();
            return;
        }

        var before = History.Capture();
        slot.Length = length;
        slot.Width = width;
        slot.AngleDegrees = angle;
        SketchEditor.RebuildSlot(sketch, slot);
        CommitSolved(part, sketch, before);
    }

    public void SetPad(double distance, bool reversed)
    {
        if (SelectedPart is not PartDesign part || SelectedFeature is not { Kind: FeatureKind.Pad } feature)
        {
            return;
        }

        if (distance <= 0 || (Math.Abs(feature.DistanceMm - distance) < 1e-6 && feature.Reversed == reversed))
        {
            return;
        }

        var before = History.Capture();
        feature.DistanceMm = distance;
        feature.Reversed = reversed;
        FeatureGraph.Recompute(part);
        History.Commit(before);
        StatusMessage = feature.Error;
        NotifyModel();
    }

    public void SetHole(double depth, bool throughAll)
    {
        if (SelectedPart is not PartDesign part || SelectedFeature is not { Kind: FeatureKind.Hole } feature)
        {
            return;
        }

        if (depth <= 0 || (Math.Abs(feature.DepthMm - depth) < 1e-6 && feature.ThroughAll == throughAll))
        {
            return;
        }

        var before = History.Capture();
        feature.DepthMm = depth;
        feature.ThroughAll = throughAll;
        FeatureGraph.Recompute(part);
        History.Commit(before);
        StatusMessage = feature.Error;
        NotifyModel();
    }

    public void SetGear(double module, int teeth, double pressure, double faceWidth, double bore, double pitchAngle)
    {
        if (SelectedPart is not PartDesign part || SelectedFeature is not { Kind: FeatureKind.Gear } feature)
        {
            return;
        }

        if (Math.Abs(feature.ModuleMm - module) < 1e-6
            && feature.Teeth == teeth
            && Math.Abs(feature.PressureAngleDegrees - pressure) < 1e-6
            && Math.Abs(feature.FaceWidthMm - faceWidth) < 1e-6
            && Math.Abs(feature.BoreMm - bore) < 1e-6
            && Math.Abs(feature.PitchAngleDegrees - pitchAngle) < 1e-6)
        {
            return;
        }

        var before = History.Capture();
        feature.ModuleMm = module;
        feature.Teeth = teeth;
        feature.PressureAngleDegrees = pressure;
        feature.FaceWidthMm = faceWidth;
        feature.BoreMm = bore;
        feature.PitchAngleDegrees = pitchAngle;
        FeatureGraph.Recompute(part);
        History.Commit(before);
        StatusMessage = feature.Error;
        NotifyModel();
    }

    public void SetConstraintValue(double value)
    {
        if (ActiveSketch is not Sketch sketch || SelectedPart is not PartDesign part)
        {
            return;
        }

        var constraint = sketch.Constraints.FirstOrDefault(item => item.Id == ItemId);
        if (constraint is null || constraint.Kind is not (ConstraintKind.Length or ConstraintKind.Radius) || Math.Abs(constraint.Value - value) < 1e-6)
        {
            return;
        }

        var before = History.Capture();
        constraint.Value = value;
        CommitSolved(part, sketch, before);
    }

    public void PickFace(SketchPointer pointer)
    {
        var part = SelectedPart;
        if (part?.Body.Mesh is not MeshData mesh)
        {
            _pickedFaceId = null;
            return;
        }

        if (!MeshQuery.TryHit(mesh, pointer.RayOrigin, pointer.RayDirection, out var faceId))
        {
            _pickedFaceId = null;
            ViewChanged?.Invoke();
            return;
        }

        _pickedFaceId = faceId;
        StatusMessage = "Face selected. Choose New sketch to draw on it.";
        ViewChanged?.Invoke();
    }

    public void DismissError(Guid featureId)
    {
        var feature = SelectedPart?.Features.FirstOrDefault(item => item.Id == featureId);
        if (feature?.Error is string error)
        {
            _dismissed[featureId] = error;
            ViewChanged?.Invoke();
        }
    }

    public bool TryExportStl(string path, out string? error)
    {
        var part = SelectedPart ?? Document.Parts.FirstOrDefault(item => item.Body.HasSolid);
        if (part?.Body.Mesh is not { Indices.Count: > 0 } mesh)
        {
            error = "The part has no solid to export.";
            return false;
        }

        var source = part.Features.FirstOrDefault(feature => feature.Id == part.Body.SourceFeatureId);
        if (source is { State: FeatureState.Frozen })
        {
            error = source.Error ?? "The solid has an error and cannot be exported.";
            return false;
        }

        using var stream = File.Create(path);
        StlWriter.Write(mesh, stream);
        error = null;
        StatusMessage = "Exported STL.";
        NotifyModel();
        return true;
    }

    public string SketchPlaneName(Sketch sketch)
    {
        if (sketch.SupportFeatureId is Guid supportId)
        {
            var support = SelectedPart?.Features.FirstOrDefault(feature => feature.Id == supportId);
            return support is null ? "Face" : $"Face of {support.Name}";
        }

        return "Base plane";
    }

    public IReadOnlyList<ErrorBubble> GetBubbles()
    {
        if (SelectedPart is not PartDesign part)
        {
            return [];
        }

        var bubbles = new List<ErrorBubble>();
        foreach (var feature in part.Features)
        {
            if (feature.State != FeatureState.Frozen || string.IsNullOrWhiteSpace(feature.Error))
            {
                continue;
            }

            if (_dismissed.TryGetValue(feature.Id, out var dismissed) && dismissed == feature.Error)
            {
                continue;
            }

            var anchor = FeatureGraph.Anchor(part, feature);
            bubbles.Add(new ErrorBubble(feature.Id, feature.Error, anchor.X, anchor.Y, anchor.Z));
        }

        return bubbles;
    }

    public void ApplyLineConstraint(ConstraintKind kind)
    {
        if (ActiveSketch is not Sketch sketch || SelectedKind != ProjectNodeKind.Line)
        {
            StatusMessage = "Select a line first.";
            NotifyModel();
            return;
        }

        var before = History.Capture();
        var added = kind == ConstraintKind.Horizontal
            ? SketchEditor.AddHorizontal(sketch, ItemId)
            : SketchEditor.AddVertical(sketch, ItemId);
        if (!added)
        {
            StatusMessage = "That constraint is already on the line.";
            NotifyModel();
            return;
        }

        FinishConstraintEdit(sketch, before);
    }

    public void ApplyLength(double length)
    {
        if (ActiveSketch is not Sketch sketch || SelectedKind != ProjectNodeKind.Line)
        {
            return;
        }

        var before = History.Capture();
        if (!SketchEditor.AddLength(sketch, ItemId, length))
        {
            StatusMessage = "Length must be greater than zero.";
            NotifyModel();
            return;
        }

        FinishConstraintEdit(sketch, before);
    }

    public void ApplyRadius(double radius)
    {
        if (ActiveSketch is not Sketch sketch || SelectedKind != ProjectNodeKind.Circle)
        {
            return;
        }

        var before = History.Capture();
        if (!SketchEditor.AddRadius(sketch, ItemId, radius))
        {
            StatusMessage = "Radius must be greater than zero.";
            NotifyModel();
            return;
        }

        FinishConstraintEdit(sketch, before);
    }

    public void OnSketchPointer(SketchPointer pointer)
    {
        if (ActiveSketch is not Sketch sketch || SelectedPart is null)
        {
            return;
        }

        if (!pointer.HitPlane && pointer.Phase != SketchPointerPhase.Released)
        {
            return;
        }

        var (x, y) = GridSnap.SnapPoint(pointer.X, pointer.Y, Settings.SnapSpacingMm, Settings.SnapEnabled);
        if (Tool == SketchTool.Select)
        {
            OnSelect(pointer, sketch, x, y);
            return;
        }

        OnDraw(pointer, sketch, x, y);
    }

    public ViewportScene BuildScene()
    {
        var scene = new ViewportScene();
        var camera = ActiveCamera;
        if (camera is null)
        {
            return scene;
        }

        scene.HasContent = true;
        scene.Camera = camera;
        scene.ShowPlateGrid = Settings.ShowGrid && SelectedAssembly is not null;
        if (SelectedAssembly is AssemblyDesign assembly)
        {
            scene.Plate = assembly.Plate;
        }

        var part = SelectedPart;
        if (part?.Body.Mesh is MeshData mesh)
        {
            scene.Solid = mesh;
            var source = part.Features.FirstOrDefault(feature => feature.Id == part.Body.SourceFeatureId);
            scene.SolidFrozen = source?.State == FeatureState.Frozen;
        }

        scene.HighlightFaceId = _pickedFaceId;
        var sketch = ActiveSketch;
        var plane = sketch?.Plane ?? new SketchPlane();
        scene.WorkPlane = plane;
        scene.ShowSketchGrid = Settings.ShowGrid
            && sketch is not null
            && Math.Abs(plane.NormalZ) > 0.99
            && Math.Abs(plane.OriginZ) < 1e-3;
        if (sketch is null)
        {
            return scene;
        }

        RefreshHighlight();
        var sketchFeature = part?.Features.FirstOrDefault(feature => feature.SketchId == sketch.Id);
        var sketchColor = sketchFeature?.State == FeatureState.Frozen ? SceneColor.Error : SceneColor.Sketch;
        foreach (var line in sketch.Lines)
        {
            var start = sketch.Points.FirstOrDefault(point => point.Id == line.StartId);
            var end = sketch.Points.FirstOrDefault(point => point.Id == line.EndId);
            if (start is null || end is null)
            {
                continue;
            }

            AddSketchSegment(scene, plane, start.X, start.Y, end.X, end.Y, _highlight.Contains(line.Id) ? SceneColor.Selected : sketchColor);
        }

        foreach (var arc in sketch.Arcs)
        {
            var samples = SketchEditor.TessellateArc(sketch, arc);
            var color = _highlight.Contains(arc.Id) ? SceneColor.Selected : sketchColor;
            for (var index = 1; index < samples.Count; index++)
            {
                AddSketchSegment(scene, plane, samples[index - 1].X, samples[index - 1].Y, samples[index].X, samples[index].Y, color);
            }
        }

        foreach (var circle in sketch.Circles)
        {
            var center = sketch.Points.FirstOrDefault(point => point.Id == circle.CenterId);
            if (center is null)
            {
                continue;
            }

            var color = _highlight.Contains(circle.Id) ? SceneColor.Selected : sketchColor;
            const int steps = 48;
            for (var step = 0; step < steps; step++)
            {
                var a0 = step * Math.PI * 2 / steps;
                var a1 = (step + 1) * Math.PI * 2 / steps;
                AddSketchSegment(
                    scene,
                    plane,
                    center.X + Math.Cos(a0) * circle.Radius,
                    center.Y + Math.Sin(a0) * circle.Radius,
                    center.X + Math.Cos(a1) * circle.Radius,
                    center.Y + Math.Sin(a1) * circle.Radius,
                    color);
            }
        }

        foreach (var point in sketch.Points)
        {
            if (point.Construction)
            {
                continue;
            }

            var world = plane.ToWorld(point.X, point.Y, 0.25);
            scene.Points.Add(new ScenePoint(world.X, world.Y, _highlight.Contains(point.Id) || point.Id == _dragPointId, world.Z));
        }

        if (_preview)
        {
            if (_previewTool == SketchTool.Circle)
            {
                var radius = Math.Sqrt((_x2 - _x1) * (_x2 - _x1) + (_y2 - _y1) * (_y2 - _y1));
                const int steps = 48;
                for (var step = 0; step < steps; step++)
                {
                    var a0 = step * Math.PI * 2 / steps;
                    var a1 = (step + 1) * Math.PI * 2 / steps;
                    AddSketchSegment(scene, plane, _x1 + Math.Cos(a0) * radius, _y1 + Math.Sin(a0) * radius, _x1 + Math.Cos(a1) * radius, _y1 + Math.Sin(a1) * radius, SceneColor.Preview);
                }
            }
            else if (_previewTool == SketchTool.Slot)
            {
                AddSlotPreview(scene, plane);
            }
            else if (_previewTool == SketchTool.Rectangle)
            {
                AddSketchSegment(scene, plane, _x1, _y1, _x2, _y1, SceneColor.Preview);
                AddSketchSegment(scene, plane, _x2, _y1, _x2, _y2, SceneColor.Preview);
                AddSketchSegment(scene, plane, _x2, _y2, _x1, _y2, SceneColor.Preview);
                AddSketchSegment(scene, plane, _x1, _y2, _x1, _y1, SceneColor.Preview);
            }
            else
            {
                AddSketchSegment(scene, plane, _x1, _y1, _x2, _y2, SceneColor.Preview);
            }
        }

        return scene;
    }

    private static void AddSketchSegment(ViewportScene scene, SketchPlane plane, double u1, double v1, double u2, double v2, SceneColor color)
    {
        var a = plane.ToWorld(u1, v1, 0.2);
        var b = plane.ToWorld(u2, v2, 0.2);
        scene.Segments.Add(new SceneSegment(a.X, a.Y, b.X, b.Y, color, a.Z, b.Z));
    }

    private void AddSlotPreview(ViewportScene scene, SketchPlane plane)
    {
        var dx = _x2 - _x1;
        var dy = _y2 - _y1;
        double length;
        double width;
        double angle;
        if (Math.Abs(dx) >= Math.Abs(dy))
        {
            length = Math.Abs(dx);
            width = Math.Abs(dy);
            angle = dx >= 0 ? 0 : 180;
        }
        else
        {
            length = Math.Abs(dy);
            width = Math.Abs(dx);
            angle = dy >= 0 ? 90 : 270;
        }

        width = Math.Max(width, 0.5);
        length = Math.Max(length, width + 0.5);
        var radians = angle * Math.PI / 180;
        var dirX = Math.Cos(radians);
        var dirY = Math.Sin(radians);
        var normalX = -dirY;
        var normalY = dirX;
        var radius = width / 2;
        var span = Math.Max(0, length - width);
        var centerX = (_x1 + _x2) / 2;
        var centerY = (_y1 + _y2) / 2;
        var centerAX = centerX - dirX * span / 2;
        var centerAY = centerY - dirY * span / 2;
        var centerBX = centerX + dirX * span / 2;
        var centerBY = centerY + dirY * span / 2;
        AddSketchSegment(scene, plane, centerAX + normalX * radius, centerAY + normalY * radius, centerBX + normalX * radius, centerBY + normalY * radius, SceneColor.Preview);
        AddSketchSegment(scene, plane, centerAX - normalX * radius, centerAY - normalY * radius, centerBX - normalX * radius, centerBY - normalY * radius, SceneColor.Preview);
        AddPreviewArc(scene, plane, centerAX, centerAY, radius, Math.Atan2(normalY, normalX), Math.PI);
        AddPreviewArc(scene, plane, centerBX, centerBY, radius, Math.Atan2(-normalY, -normalX), Math.PI);
    }

    private static void AddPreviewArc(ViewportScene scene, SketchPlane plane, double centerX, double centerY, double radius, double start, double sweep)
    {
        const int steps = 16;
        for (var step = 0; step < steps; step++)
        {
            var a0 = start + sweep * step / steps;
            var a1 = start + sweep * (step + 1) / steps;
            AddSketchSegment(scene, plane, centerX + Math.Cos(a0) * radius, centerY + Math.Sin(a0) * radius, centerX + Math.Cos(a1) * radius, centerY + Math.Sin(a1) * radius, SceneColor.Preview);
        }
    }

    public IReadOnlyList<MarkerAnchor> GetMarkers()
    {
        if (ActiveSketch is not Sketch sketch)
        {
            return [];
        }

        var markers = new List<MarkerAnchor>();
        foreach (var constraint in sketch.Constraints)
        {
            switch (constraint.Kind)
            {
                case ConstraintKind.Horizontal:
                case ConstraintKind.Vertical:
                case ConstraintKind.Length:
                    var line = sketch.Lines.FirstOrDefault(item => item.Id == constraint.LineId);
                    if (line is null)
                    {
                        break;
                    }

                    var start = sketch.Points.FirstOrDefault(point => point.Id == line.StartId);
                    var end = sketch.Points.FirstOrDefault(point => point.Id == line.EndId);
                    if (start is null || end is null)
                    {
                        break;
                    }

                    var text = constraint.Kind switch
                    {
                        ConstraintKind.Horizontal => "H",
                        ConstraintKind.Vertical => "V",
                        _ => $"L {constraint.Value:0.##}"
                    };
                    var middle = sketch.Plane.ToWorld((start.X + end.X) / 2, (start.Y + end.Y) / 2);
                    markers.Add(new MarkerAnchor(text, middle.X, middle.Y, middle.Z));
                    break;
                case ConstraintKind.Coincident:
                    var point = sketch.Points.FirstOrDefault(item => item.Id == constraint.PointAId);
                    if (point is not null)
                    {
                        var coincident = sketch.Plane.ToWorld(point.X, point.Y);
                        markers.Add(new MarkerAnchor("C", coincident.X, coincident.Y, coincident.Z));
                    }

                    break;
                case ConstraintKind.Radius:
                    var circle = sketch.Circles.FirstOrDefault(item => item.Id == constraint.CircleId);
                    var center = circle is null ? null : sketch.Points.FirstOrDefault(item => item.Id == circle.CenterId);
                    if (circle is not null && center is not null)
                    {
                        var radiusPoint = sketch.Plane.ToWorld(center.X + circle.Radius, center.Y);
                        markers.Add(new MarkerAnchor($"R {constraint.Value:0.##}", radiusPoint.X, radiusPoint.Y, radiusPoint.Z));
                    }

                    break;
            }
        }

        return markers;
    }

    public string ActiveLabel
    {
        get
        {
            if (SelectedAssembly is AssemblyDesign assembly)
            {
                return assembly.Name;
            }

            if (SelectedPart is PartDesign part)
            {
                return part.Name;
            }

            return "Nothing selected";
        }
    }

    public string ToolHint()
    {
        if (SelectedPart is null)
        {
            return "Left drag orbits, right drag pans, wheel zooms";
        }

        if (_coincidentAnchor is not null)
        {
            return "Click the point to join";
        }

        return Tool switch
        {
            SketchTool.Line => _preview ? "Click the end point" : "Click the start point",
            SketchTool.Rectangle => _preview ? "Click the opposite corner" : "Click the first corner",
            SketchTool.Circle => _preview ? "Click to set the radius" : "Click the center",
            SketchTool.Slot => _preview ? "Click the opposite end" : "Click the first end",
            _ => EditingSketch
                ? "Drag a point, or drag a box to select. Left to right selects inside; right to left selects crossing."
                : "Click a face, or drag to orbit. Right drag pans, wheel zooms."
        };
    }

    private void OnSelect(SketchPointer pointer, Sketch sketch, double snappedX, double snappedY)
    {
        if (pointer.Phase == SketchPointerPhase.Moved && _dragPointId is not null)
        {
            DragSelection(sketch, snappedX, snappedY);
            return;
        }

        if (pointer.Phase == SketchPointerPhase.Moved && _marquee)
        {
            _marqueeX2 = pointer.ScreenX;
            _marqueeY2 = pointer.ScreenY;
            ViewChanged?.Invoke();
            return;
        }

        if (pointer.Phase == SketchPointerPhase.Released)
        {
            if (_marquee)
            {
                FinishMarquee(sketch, pointer);
            }

            FinishDrag();
            return;
        }

        if (pointer.Phase != SketchPointerPhase.Pressed)
        {
            return;
        }

        var hit = SketchEditor.HitTest(sketch, pointer.X, pointer.Y, pointer.ToleranceMm);
        if (_coincidentAnchor is Guid anchor && hit?.Kind == SketchHitKind.Point && hit.Value.Id != anchor)
        {
            CompleteCoincident(sketch, anchor, hit.Value.Id);
            return;
        }

        if (hit?.Kind == SketchHitKind.Point)
        {
            if (pointer.Shift)
            {
                if (!_selectedPoints.Remove(hit.Value.Id))
                {
                    _selectedPoints.Add(hit.Value.Id);
                }
            }
            else if (!_selectedPoints.Contains(hit.Value.Id))
            {
                _selectedPoints.Clear();
                _selectedPoints.Add(hit.Value.Id);
            }

            _dragPointId = hit.Value.Id;
            _dragOriginX = snappedX;
            _dragOriginY = snappedY;
            _dragPoints = sketch.Points
                .Where(point => _selectedPoints.Contains(point.Id) && point.SlotId is null)
                .ToDictionary(point => point.Id, point => (point.X, point.Y));
            var slotIds = sketch.Points
                .Where(point => _selectedPoints.Contains(point.Id) && point.SlotId is Guid)
                .Select(point => point.SlotId!.Value)
                .ToHashSet();
            _dragSlots = sketch.Slots
                .Where(slot => slotIds.Contains(slot.Id))
                .ToDictionary(slot => slot.Id, slot => (slot.CenterX, slot.CenterY, slot.Length, slot.Width, slot.AngleDegrees));
            _gestureBefore = History.Capture();
            SelectSketch(SelectedPart!, sketch, keepPoints: true);
            return;
        }

        _selectedPoints.Clear();
        _dragPointId = null;
        if (hit?.Kind == SketchHitKind.Line)
        {
            Select(ProjectNodeKind.Line, SelectedPart!.Id, hit.Value.Id);
        }
        else if (hit?.Kind == SketchHitKind.Circle)
        {
            Select(ProjectNodeKind.Circle, SelectedPart!.Id, hit.Value.Id);
        }
        else if (hit?.Kind == SketchHitKind.Slot)
        {
            Select(ProjectNodeKind.Slot, SelectedPart!.Id, hit.Value.Id);
        }
        else
        {
            _marquee = true;
            _marqueeShift = pointer.Shift;
            _marqueeX1 = _marqueeX2 = pointer.ScreenX;
            _marqueeY1 = _marqueeY2 = pointer.ScreenY;
            ViewChanged?.Invoke();
        }
    }

    private void OnDraw(SketchPointer pointer, Sketch sketch, double x, double y)
    {
        if (pointer.Phase == SketchPointerPhase.Moved && _preview)
        {
            _x2 = x;
            _y2 = y;
            ViewChanged?.Invoke();
            return;
        }

        if (pointer.Phase != SketchPointerPhase.Pressed)
        {
            return;
        }

        if (!_preview)
        {
            _preview = true;
            _previewTool = Tool;
            _x1 = _x2 = x;
            _y1 = _y2 = y;
            StatusMessage = ToolHint();
            ViewChanged?.Invoke();
            return;
        }

        var before = History.Capture();
        var created = Tool switch
        {
            SketchTool.Line => SketchEditor.AddLine(sketch, _x1, _y1, x, y) is not null,
            SketchTool.Rectangle => SketchEditor.AddRectangle(sketch, _x1, _y1, x, y),
            SketchTool.Circle => SketchEditor.AddCircle(sketch, _x1, _y1, Math.Sqrt((x - _x1) * (x - _x1) + (y - _y1) * (y - _y1))) is not null,
            SketchTool.Slot => SketchEditor.AddSlot(sketch, _x1, _y1, x, y) is not null,
            _ => false
        };
        if (!created)
        {
            StatusMessage = "Pick a different point.";
            ViewChanged?.Invoke();
            return;
        }

        _preview = false;
        if (SelectedPart is not PartDesign part || !CommitSolved(part, sketch, before))
        {
            return;
        }

        StatusMessage = null;
        if (Tool == SketchTool.Circle && sketch.Circles.Count > 0)
        {
            Select(ProjectNodeKind.Circle, part.Id, sketch.Circles[^1].Id);
        }
        else if (Tool == SketchTool.Slot && sketch.Slots.Count > 0)
        {
            Select(ProjectNodeKind.Slot, part.Id, sketch.Slots[^1].Id);
        }
        else if (sketch.Lines.Count > 0)
        {
            Select(ProjectNodeKind.Line, part.Id, sketch.Lines[^1].Id);
        }
        else
        {
            NotifyModel();
        }
    }

    private void DragSelection(Sketch sketch, double x, double y)
    {
        if (_gestureBefore is null || _dragPointId is not Guid grabbedId)
        {
            return;
        }

        var grabbed = sketch.Points.FirstOrDefault(point => point.Id == grabbedId);
        if (grabbed is null)
        {
            return;
        }

        if (_selectedPoints.Count == 1 && grabbed.SlotId is Guid slotId)
        {
            var slot = sketch.Slots.FirstOrDefault(item => item.Id == slotId);
            if (slot is null)
            {
                return;
            }

            ResizeSlot(slot, x, y);
            SketchEditor.RebuildSlot(sketch, slot);
            var solved = SketchSolver.Solve(sketch);
            if (!solved.Success)
            {
                FailGesture(solved.Message);
                return;
            }

            ViewChanged?.Invoke();
            return;
        }

        var dx = x - _dragOriginX;
        var dy = y - _dragOriginY;
        if (_dragSlots is not null)
        {
            foreach (var pair in _dragSlots)
            {
                var slot = sketch.Slots.FirstOrDefault(item => item.Id == pair.Key);
                if (slot is null)
                {
                    continue;
                }

                slot.CenterX = pair.Value.Cx + dx;
                slot.CenterY = pair.Value.Cy + dy;
                slot.Length = pair.Value.Length;
                slot.Width = pair.Value.Width;
                slot.AngleDegrees = pair.Value.Angle;
                SketchEditor.RebuildSlot(sketch, slot);
            }
        }

        var pins = _dragPoints?.Select(pair => new SketchSolver.PinnedPoint(pair.Key, pair.Value.X + dx, pair.Value.Y + dy)).ToList();
        var result = SketchSolver.SolvePinned(sketch, pins);
        if (!result.Success)
        {
            FailGesture(result.Message);
            return;
        }

        StatusMessage = null;
        ViewChanged?.Invoke();
    }

    private void FinishMarquee(Sketch sketch, SketchPointer pointer)
    {
        _marquee = false;
        var part = SelectedPart;
        if (part is null)
        {
            return;
        }

        if (Math.Abs(_marqueeX2 - _marqueeX1) < 4 && Math.Abs(_marqueeY2 - _marqueeY1) < 4)
        {
            if (!_marqueeShift)
            {
                _selectedPoints.Clear();
            }

            SelectSketch(part, sketch, keepPoints: true);
            return;
        }

        var hit = MarqueeSelection.Select(sketch, _marqueeX1, _marqueeY1, _marqueeX2, _marqueeY2, ScreenMap(sketch, pointer));
        if (!_marqueeShift)
        {
            _selectedPoints.Clear();
        }

        foreach (var id in hit.Points)
        {
            if (!_selectedPoints.Contains(id))
            {
                _selectedPoints.Add(id);
            }
        }

        if (_selectedPoints.Count == 0 && hit.Lines.Count == 1 && hit.Circles.Count == 0 && hit.Slots.Count == 0)
        {
            Select(ProjectNodeKind.Line, part.Id, hit.Lines[0]);
        }
        else if (_selectedPoints.Count == 0 && hit.Circles.Count == 1 && hit.Lines.Count == 0 && hit.Slots.Count == 0)
        {
            Select(ProjectNodeKind.Circle, part.Id, hit.Circles[0]);
        }
        else if (_selectedPoints.Count == 0 && hit.Slots.Count == 1 && hit.Lines.Count == 0 && hit.Circles.Count == 0)
        {
            Select(ProjectNodeKind.Slot, part.Id, hit.Slots[0]);
        }
        else
        {
            SelectSketch(part, sketch, keepPoints: true);
        }
    }

    private void FinishDrag()
    {
        var changed = _gestureBefore is not null;
        if (_gestureBefore is not null && SelectedPart is PartDesign part)
        {
            FeatureGraph.Recompute(part);
            History.Commit(_gestureBefore);
        }

        _gestureBefore = null;
        _dragPointId = null;
        _dragPoints = null;
        _dragSlots = null;
        _marquee = false;
        if (StatusMessage == "Constraints could not be solved.")
        {
            StatusMessage = null;
        }

        if (changed)
        {
            NotifyModel();
        }
        else
        {
            ViewChanged?.Invoke();
        }
    }

    private void FailGesture(string? message)
    {
        if (_gestureBefore is not null)
        {
            History.RevertUncommitted(_gestureBefore);
        }

        _gestureBefore = null;
        _dragPointId = null;
        _dragPoints = null;
        _dragSlots = null;
        _marquee = false;
        StatusMessage = message;
        NotifyModel();
    }

    private void CompleteCoincident(Sketch sketch, Guid anchor, Guid other)
    {
        var before = History.Capture();
        if (!SketchEditor.AddCoincident(sketch, anchor, other))
        {
            StatusMessage = "Those points are already coincident.";
            _coincidentAnchor = null;
            NotifyModel();
            return;
        }

        _coincidentAnchor = null;
        FinishConstraintEdit(sketch, before);
    }

    private void FinishConstraintEdit(Sketch sketch, string before)
    {
        var result = SketchSolver.Solve(sketch);
        if (!result.Success)
        {
            History.RevertUncommitted(before);
            StatusMessage = result.Message;
            NotifyModel();
            return;
        }

        if (SelectedPart is PartDesign part)
        {
            FeatureGraph.Recompute(part);
        }

        History.Commit(before);
        StatusMessage = null;
        NotifyModel();
    }

    private void Select(ProjectNodeKind kind, Guid ownerId, Guid itemId)
    {
        if (ownerId != OwnerId || !IsPartKind(kind))
        {
            _preview = false;
            _coincidentAnchor = null;
            _dragPointId = null;
            _pickedFaceId = null;
        }

        HasSelection = true;
        SelectedKind = kind;
        OwnerId = ownerId;
        ItemId = itemId;
        if (kind != ProjectNodeKind.Sketch)
        {
            _selectedPoints.Clear();
        }

        NotifyModel();
    }

    private void SelectSketch(PartDesign part, Sketch sketch, bool keepPoints)
    {
        if (!keepPoints)
        {
            _selectedPoints.Clear();
        }

        var feature = part.Features.FirstOrDefault(item => item.Kind == FeatureKind.Sketch && item.SketchId == sketch.Id);
        var itemId = feature?.Id ?? sketch.Id;
        if (HasSelection && SelectedKind == ProjectNodeKind.Sketch && OwnerId == part.Id && ItemId == itemId)
        {
            ViewChanged?.Invoke();
            return;
        }

        Select(ProjectNodeKind.Sketch, part.Id, itemId);
    }

    private void SelectFirst()
    {
        if (Document.Assemblies.Count > 0)
        {
            var assembly = Document.Assemblies[0];
            HasSelection = true;
            SelectedKind = ProjectNodeKind.Assembly;
            OwnerId = assembly.Id;
            ItemId = assembly.Id;
            return;
        }

        if (Document.Parts.Count > 0)
        {
            var part = Document.Parts[0];
            HasSelection = true;
            SelectedKind = ProjectNodeKind.Part;
            OwnerId = part.Id;
            ItemId = part.Id;
            return;
        }

        ClearSelection();
    }

    private void ClearSelection()
    {
        HasSelection = false;
        SelectedKind = default;
        OwnerId = Guid.Empty;
        ItemId = Guid.Empty;
        _selectedPoints.Clear();
        _pickedFaceId = null;
        _preview = false;
        _highlight.Clear();
    }

    private void EnsureSelection()
    {
        if (!HasSelection)
        {
            return;
        }

        var ownerExists = IsPartKind(SelectedKind)
            ? Document.Parts.Any(part => part.Id == OwnerId)
            : Document.Assemblies.Any(assembly => assembly.Id == OwnerId);
        if (!ownerExists)
        {
            ClearSelection();
            SelectFirst();
        }
    }

    private void RefreshHighlight()
    {
        _highlight.Clear();
        if (ActiveSketch is not Sketch sketch)
        {
            return;
        }

        if (SelectedKind is ProjectNodeKind.Line or ProjectNodeKind.Circle or ProjectNodeKind.Slot)
        {
            _highlight.Add(ItemId);
            if (SelectedKind == ProjectNodeKind.Slot)
            {
                var slot = sketch.Slots.FirstOrDefault(item => item.Id == ItemId);
                if (slot is not null)
                {
                    _highlight.Add(slot.LineAId);
                    _highlight.Add(slot.LineBId);
                    _highlight.Add(slot.ArcAId);
                    _highlight.Add(slot.ArcBId);
                    _highlight.Add(slot.JunctionAId);
                    _highlight.Add(slot.JunctionBId);
                    _highlight.Add(slot.JunctionCId);
                    _highlight.Add(slot.JunctionDId);
                }
            }
        }

        if (_dragPointId is Guid dragging)
        {
            _highlight.Add(dragging);
        }

        foreach (var pointId in _selectedPoints)
        {
            _highlight.Add(pointId);
        }

        if (SelectedKind != ProjectNodeKind.Constraint)
        {
            return;
        }

        var constraint = sketch.Constraints.FirstOrDefault(item => item.Id == ItemId);
        if (constraint is null)
        {
            return;
        }

        if (constraint.LineId is Guid lineId)
        {
            _highlight.Add(lineId);
        }

        if (constraint.CircleId is Guid circleId)
        {
            _highlight.Add(circleId);
        }

        if (constraint.PointAId is Guid pointA)
        {
            _highlight.Add(pointA);
        }

        if (constraint.PointBId is Guid pointB)
        {
            _highlight.Add(pointB);
        }
    }

    private static (double MinX, double MinY, double MaxX, double MaxY) SketchBounds(Sketch? sketch)
    {
        if (sketch is null || (sketch.Points.Count == 0 && sketch.Circles.Count == 0))
        {
            return (-50, -50, 50, 50);
        }

        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        foreach (var point in sketch.Points)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        foreach (var circle in sketch.Circles)
        {
            var center = sketch.Points.FirstOrDefault(point => point.Id == circle.CenterId);
            if (center is null)
            {
                continue;
            }

            minX = Math.Min(minX, center.X - circle.Radius);
            minY = Math.Min(minY, center.Y - circle.Radius);
            maxX = Math.Max(maxX, center.X + circle.Radius);
            maxY = Math.Max(maxY, center.Y + circle.Radius);
        }

        if (double.IsInfinity(minX))
        {
            return (-50, -50, 50, 50);
        }

        var pad = Math.Max(5, Math.Max(maxX - minX, maxY - minY) * 0.1);
        return (minX - pad, minY - pad, maxX + pad, maxY + pad);
    }

    private void NotifyModel() => ModelChanged?.Invoke();

    private static bool IsAssemblyKind(ProjectNodeKind kind) => kind is
        ProjectNodeKind.Assembly or ProjectNodeKind.Plate or ProjectNodeKind.Placements
        or ProjectNodeKind.Placement or ProjectNodeKind.Bom or ProjectNodeKind.BomLine;

    private static bool IsPartKind(ProjectNodeKind kind) => kind is
        ProjectNodeKind.Part or ProjectNodeKind.Sketch or ProjectNodeKind.Line
        or ProjectNodeKind.Circle or ProjectNodeKind.Slot or ProjectNodeKind.Constraint
        or ProjectNodeKind.Pad or ProjectNodeKind.Hole or ProjectNodeKind.Gear
        or ProjectNodeKind.Body;

    private bool CommitSolved(PartDesign part, Sketch sketch, string before)
    {
        var solved = SketchSolver.Solve(sketch);
        if (!solved.Success)
        {
            History.RevertUncommitted(before);
            StatusMessage = solved.Message;
            NotifyModel();
            return false;
        }

        FeatureGraph.Recompute(part);
        History.Commit(before);
        StatusMessage = null;
        return true;
    }

    private bool ApplyPins(PartDesign part, Sketch sketch, string before, IReadOnlyList<SketchSolver.PinnedPoint> pins)
    {
        var solved = SketchSolver.SolvePinned(sketch, pins);
        if (!solved.Success)
        {
            History.RevertUncommitted(before);
            StatusMessage = solved.Message;
            NotifyModel();
            return false;
        }

        FeatureGraph.Recompute(part);
        History.Commit(before);
        StatusMessage = null;
        return true;
    }

    private Sketch? SketchFromSelection(PartDesign part)
    {
        if (SelectedKind == ProjectNodeKind.Sketch)
        {
            var feature = part.Features.FirstOrDefault(item => item.Id == ItemId && item.Kind == FeatureKind.Sketch);
            if (feature?.SketchId is Guid featureSketch)
            {
                return part.Sketches.FirstOrDefault(sketch => sketch.Id == featureSketch);
            }

            return part.Sketches.FirstOrDefault(sketch => sketch.Id == ItemId);
        }

        if (SelectedKind is not (ProjectNodeKind.Line or ProjectNodeKind.Circle or ProjectNodeKind.Slot or ProjectNodeKind.Constraint))
        {
            return null;
        }

        return part.Sketches.FirstOrDefault(sketch => Owns(sketch, SelectedKind, ItemId));
    }

    private static bool Owns(Sketch sketch, ProjectNodeKind kind, Guid id) => kind switch
    {
        ProjectNodeKind.Line => sketch.Lines.Any(line => line.Id == id),
        ProjectNodeKind.Circle => sketch.Circles.Any(circle => circle.Id == id),
        ProjectNodeKind.Slot => sketch.Slots.Any(slot => slot.Id == id),
        ProjectNodeKind.Constraint => sketch.Constraints.Any(constraint => constraint.Id == id),
        _ => false
    };

    private static void RemoveFeatureTree(PartDesign part, Guid featureId, bool removeOwnSketch)
    {
        var remove = new HashSet<Guid>();
        void Collect(Guid id)
        {
            if (!remove.Add(id))
            {
                return;
            }

            foreach (var child in part.Features.Where(feature => feature.Parents.Contains(id)).ToList())
            {
                Collect(child.Id);
            }
        }

        Collect(featureId);
        var sketches = part.Features
            .Where(feature => remove.Contains(feature.Id) && feature.SketchId is Guid && (removeOwnSketch || feature.Id != featureId))
            .Select(feature => feature.SketchId!.Value)
            .ToHashSet();
        part.Features.RemoveAll(feature => remove.Contains(feature.Id));
        part.Sketches.RemoveAll(sketch => sketches.Contains(sketch.Id));
    }

    private static void ResizeSlot(SketchSlot slot, double x, double y)
    {
        var radians = slot.AngleDegrees * Math.PI / 180;
        var dirX = Math.Cos(radians);
        var dirY = Math.Sin(radians);
        var relX = x - slot.CenterX;
        var relY = y - slot.CenterY;
        var axial = Math.Abs(relX * dirX + relY * dirY);
        var perp = Math.Abs(relX * -dirY + relY * dirX);
        var width = Math.Max(0.5, perp * 2);
        slot.Width = width;
        slot.Length = Math.Max(width + 0.5, axial * 2 + width);
    }

    private Func<double, double, (double X, double Y)> ScreenMap(Sketch sketch, SketchPointer pointer)
    {
        var camera = ActiveCamera;
        return (u, v) =>
        {
            if (camera is null)
            {
                return (u, v);
            }

            var world = sketch.Plane.ToWorld(u, v);
            if (!CameraMath.TryProject(camera, pointer.ViewWidth, pointer.ViewHeight, world, out var projected))
            {
                return (double.NaN, double.NaN);
            }

            return (
                (projected.NdcX * 0.5 + 0.5) * pointer.ViewWidth,
                (1 - (projected.NdcY * 0.5 + 0.5)) * pointer.ViewHeight);
        };
    }
}
