using MotionMaker.Core;
using MotionMaker.Geometry;
using MotionMaker.Viewport;

namespace MotionMaker.App;

public enum SketchTool
{
    Select,
    Line,
    Rectangle,
    Circle
}

public readonly record struct MarkerAnchor(string Text, double X, double Y);

public sealed class Session
{
    private readonly HashSet<Guid> _highlight = [];
    private string? _gestureBefore;
    private Guid? _dragPointId;
    private bool _preview;
    private double _x1;
    private double _y1;
    private double _x2;
    private double _y2;
    private SketchTool _previewTool;
    private Guid? _coincidentAnchor;

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

    public Guid? SelectedPointId { get; private set; }

    public SketchTool Tool { get; private set; } = SketchTool.Select;

    public string? StatusMessage { get; private set; }

    public AssemblyDesign? SelectedAssembly
        => IsAssemblyKind(SelectedKind) ? Document.Assemblies.FirstOrDefault(item => item.Id == OwnerId) : null;

    public PartDesign? SelectedPart
        => IsPartKind(SelectedKind) ? Document.Parts.FirstOrDefault(item => item.Id == OwnerId) : null;

    public Sketch? ActiveSketch => SelectedPart?.Sketches.FirstOrDefault();

    public CameraState? ActiveCamera => SelectedPart?.Camera ?? SelectedAssembly?.Camera;

    public string DisplayName => string.IsNullOrWhiteSpace(Document.Name) ? "Untitled" : Document.Name;

    public event Action? ModelChanged;

    public event Action? ViewChanged;

    public void NewProject()
    {
        History.Reset(ProjectEdits.CreateNew());
        FilePath = null;
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
                if (ActiveSketch is Sketch sketch)
                {
                    var kind = SelectedKind == ProjectNodeKind.Line ? SketchHitKind.Line : SketchHitKind.Circle;
                    SketchEditor.Delete(sketch, new SketchHit(kind, ItemId));
                    History.Commit(before);
                    Select(ProjectNodeKind.Sketch, OwnerId, sketch.Id);
                }

                return true;
            case ProjectNodeKind.Constraint:
                ActiveSketch?.Constraints.RemoveAll(constraint => constraint.Id == ItemId);
                if (ActiveSketch is Sketch ownerSketch)
                {
                    History.Commit(before);
                    Select(ProjectNodeKind.Sketch, OwnerId, ownerSketch.Id);
                }

                return true;
            case ProjectNodeKind.Sketch:
                if (ActiveSketch is Sketch clear)
                {
                    clear.Points.Clear();
                    clear.Lines.Clear();
                    clear.Circles.Clear();
                    clear.Constraints.Clear();
                    History.Commit(before);
                    NotifyModel();
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
            part.Camera.Perspective = false;
            CameraMath.SetPreset(part.Camera, ViewPreset.Top);
            var bounds = SketchBounds(part.Sketches.FirstOrDefault());
            CameraMath.FrameBounds(part.Camera, bounds.MinX, bounds.MinY, 0, bounds.MaxX, bounds.MaxY, 0, viewWidth, viewHeight);
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
        _gestureBefore = null;
        StatusMessage = null;
        ViewChanged?.Invoke();
    }

    public void BeginCoincident()
    {
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
        scene.ShowSketchGrid = Settings.ShowGrid && SelectedPart is not null;
        if (SelectedAssembly is AssemblyDesign assembly)
        {
            scene.Plate = assembly.Plate;
        }

        var sketch = ActiveSketch;
        if (sketch is null)
        {
            return scene;
        }

        RefreshHighlight();
        foreach (var line in sketch.Lines)
        {
            var start = sketch.Points.FirstOrDefault(point => point.Id == line.StartId);
            var end = sketch.Points.FirstOrDefault(point => point.Id == line.EndId);
            if (start is null || end is null)
            {
                continue;
            }

            scene.Segments.Add(new SceneSegment(
                start.X,
                start.Y,
                end.X,
                end.Y,
                _highlight.Contains(line.Id) ? SceneColor.Selected : SceneColor.Sketch));
        }

        foreach (var circle in sketch.Circles)
        {
            var center = sketch.Points.FirstOrDefault(point => point.Id == circle.CenterId);
            if (center is null)
            {
                continue;
            }

            scene.Circles.Add(new SceneCircle(
                center.X,
                center.Y,
                circle.Radius,
                _highlight.Contains(circle.Id) ? SceneColor.Selected : SceneColor.Sketch));
        }

        foreach (var point in sketch.Points)
        {
            scene.Points.Add(new ScenePoint(point.X, point.Y, _highlight.Contains(point.Id) || point.Id == _dragPointId));
        }

        if (_preview)
        {
            if (_previewTool == SketchTool.Circle)
            {
                var radius = Math.Sqrt((_x2 - _x1) * (_x2 - _x1) + (_y2 - _y1) * (_y2 - _y1));
                scene.PreviewCircle = new SceneCircle(_x1, _y1, radius, SceneColor.Preview);
            }
            else if (_previewTool == SketchTool.Rectangle)
            {
                scene.Segments.Add(new SceneSegment(_x1, _y1, _x2, _y1, SceneColor.Preview));
                scene.Segments.Add(new SceneSegment(_x2, _y1, _x2, _y2, SceneColor.Preview));
                scene.Segments.Add(new SceneSegment(_x2, _y2, _x1, _y2, SceneColor.Preview));
                scene.Segments.Add(new SceneSegment(_x1, _y2, _x1, _y1, SceneColor.Preview));
            }
            else
            {
                scene.Segments.Add(new SceneSegment(_x1, _y1, _x2, _y2, SceneColor.Preview));
            }
        }

        return scene;
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
                    markers.Add(new MarkerAnchor(text, (start.X + end.X) / 2, (start.Y + end.Y) / 2));
                    break;
                case ConstraintKind.Coincident:
                    var point = sketch.Points.FirstOrDefault(item => item.Id == constraint.PointAId);
                    if (point is not null)
                    {
                        markers.Add(new MarkerAnchor("C", point.X, point.Y));
                    }

                    break;
                case ConstraintKind.Radius:
                    var circle = sketch.Circles.FirstOrDefault(item => item.Id == constraint.CircleId);
                    var center = circle is null ? null : sketch.Points.FirstOrDefault(item => item.Id == circle.CenterId);
                    if (circle is not null && center is not null)
                    {
                        markers.Add(new MarkerAnchor($"R {constraint.Value:0.##}", center.X + circle.Radius, center.Y));
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
            _ => "Drag a point. Constraint buttons use the current selection."
        };
    }

    private void OnSelect(SketchPointer pointer, Sketch sketch, double snappedX, double snappedY)
    {
        if (pointer.Phase == SketchPointerPhase.Moved && _dragPointId is Guid dragging)
        {
            var result = SketchSolver.Solve(sketch, dragging, snappedX, snappedY);
            StatusMessage = result.Success ? null : result.Message;
            ViewChanged?.Invoke();
            return;
        }

        if (pointer.Phase == SketchPointerPhase.Released)
        {
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
            SelectedPointId = hit.Value.Id;
            _dragPointId = hit.Value.Id;
            _gestureBefore = History.Capture();
            SelectedKind = ProjectNodeKind.Sketch;
            ItemId = sketch.Id;
            ViewChanged?.Invoke();
            return;
        }

        SelectedPointId = null;
        _dragPointId = null;
        if (hit?.Kind == SketchHitKind.Line)
        {
            Select(ProjectNodeKind.Line, SelectedPart!.Id, hit.Value.Id);
        }
        else if (hit?.Kind == SketchHitKind.Circle)
        {
            Select(ProjectNodeKind.Circle, SelectedPart!.Id, hit.Value.Id);
        }
        else
        {
            Select(ProjectNodeKind.Sketch, SelectedPart!.Id, sketch.Id);
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
            _ => false
        };
        if (!created)
        {
            StatusMessage = "Pick a different point.";
            ViewChanged?.Invoke();
            return;
        }

        _preview = false;
        var solved = SketchSolver.Solve(sketch);
        if (!solved.Success)
        {
            History.RevertUncommitted(before);
            StatusMessage = solved.Message;
            NotifyModel();
            return;
        }

        History.Commit(before);
        StatusMessage = null;
        if (Tool == SketchTool.Circle && sketch.Circles.Count > 0)
        {
            Select(ProjectNodeKind.Circle, SelectedPart!.Id, sketch.Circles[^1].Id);
        }
        else if (sketch.Lines.Count > 0)
        {
            Select(ProjectNodeKind.Line, SelectedPart!.Id, sketch.Lines[^1].Id);
        }
        else
        {
            NotifyModel();
        }
    }

    private void FinishDrag()
    {
        if (_gestureBefore is null)
        {
            return;
        }

        History.Commit(_gestureBefore);
        _gestureBefore = null;
        _dragPointId = null;
        if (StatusMessage == "Constraints could not be solved.")
        {
            StatusMessage = null;
        }

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
        }

        HasSelection = true;
        SelectedKind = kind;
        OwnerId = ownerId;
        ItemId = itemId;
        if (kind != ProjectNodeKind.Sketch)
        {
            SelectedPointId = null;
        }

        NotifyModel();
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
        SelectedPointId = null;
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

        if (SelectedKind is ProjectNodeKind.Line or ProjectNodeKind.Circle)
        {
            _highlight.Add(ItemId);
        }

        if (_dragPointId is Guid dragging)
        {
            _highlight.Add(dragging);
        }

        if (SelectedPointId is Guid pointId)
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
        or ProjectNodeKind.Circle or ProjectNodeKind.Constraint or ProjectNodeKind.Body;
}
