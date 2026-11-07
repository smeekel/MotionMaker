using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MotionMaker.Core;
using MotionMaker.Geometry;
using MotionMaker.Viewport;

namespace MotionMaker.App;

public partial class MainWindow : Window
{
    private readonly Session _session;
    private readonly List<(string Key, NumericUpDown Box)> _propertyBoxes = [];
    private readonly List<(Rect Bounds, Guid FeatureId)> _bubbleCloses = [];
    private bool _updatingTree;
    private bool _syncingProperties;
    private bool _acceptClose;
    private bool _confirmingClose;
    private string? _propertyKey;
    private string _cursor = "—";

    public MainWindow()
    {
        InitializeComponent();
        _session = new Session(SettingsStore.Load());
        Width = _session.Settings.WindowWidth;
        Height = _session.Settings.WindowHeight;
        GridMenu.IsChecked = _session.Settings.ShowGrid;
        SnapMenu.IsChecked = _session.Settings.SnapEnabled;
        _session.ModelChanged += RefreshAll;
        _session.ViewChanged += RefreshView;
        Scene.ViewChanged += RefreshView;
        Scene.CursorMoved += OnCursorMoved;
        Scene.SketchGesture += pointer => _session.OnSketchPointer(pointer);
        Scene.FaceClicked += pointer => _session.PickFace(pointer);
        Scene.GlStateChanged += UpdateGlStatus;
        ViewportHost.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Arrow);
        RebuildRecentMenu();
        RefreshAll();
    }

    private async void OnNew(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardAsync())
        {
            return;
        }

        _session.NewProject();
    }

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardAsync())
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open project",
            AllowMultiple = false,
            FileTypeFilter = [ProjectFileType()]
        });
        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (path is null)
        {
            return;
        }

        try
        {
            _session.LoadFile(path);
            RebuildRecentMenu();
        }
        catch (Exception ex)
        {
            await Dialogs.ShowMessageAsync(this, "Open project", ex.Message);
        }
    }

    private async void OnSave(object? sender, RoutedEventArgs e) => await SaveAsync(saveAs: false);

    private async void OnSaveAs(object? sender, RoutedEventArgs e) => await SaveAsync(saveAs: true);

    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    private void OnUndo(object? sender, RoutedEventArgs e) => _session.Undo();

    private void OnRedo(object? sender, RoutedEventArgs e) => _session.Redo();

    private async void OnPreferences(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.EditPreferencesAsync(this, _session.Settings))
        {
            SettingsStore.Save(_session.Settings);
            RefreshView();
        }
    }

    private void OnAddAssembly(object? sender, RoutedEventArgs e) => _session.AddAssembly();

    private void OnAddPart(object? sender, RoutedEventArgs e) => _session.AddPart();

    private async void OnRename(object? sender, RoutedEventArgs e)
    {
        if (_session.SelectedKind is not (ProjectNodeKind.Assembly or ProjectNodeKind.Part))
        {
            return;
        }

        var current = _session.SelectedAssembly?.Name ?? _session.SelectedPart?.Name ?? "";
        var name = await Dialogs.PromptAsync(this, "Rename", "Name", current);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _session.RenameSelection(name);
    }

    private async void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (!_session.TryDelete(out var error) && error is not null)
        {
            await Dialogs.ShowMessageAsync(this, "Delete", error);
        }
    }

    private void OnTop(object? sender, RoutedEventArgs e) => _session.ApplyPreset(ViewPreset.Top);

    private void OnFront(object? sender, RoutedEventArgs e) => _session.ApplyPreset(ViewPreset.Front);

    private void OnRight(object? sender, RoutedEventArgs e) => _session.ApplyPreset(ViewPreset.Right);

    private void OnIso(object? sender, RoutedEventArgs e) => _session.ApplyPreset(ViewPreset.Iso);

    private void OnPerspective(object? sender, RoutedEventArgs e) => _session.TogglePerspective();

    private void OnFrame(object? sender, RoutedEventArgs e)
        => _session.Frame(Math.Max(Scene.Bounds.Width, 1), Math.Max(Scene.Bounds.Height, 1));

    private void OnGrid(object? sender, RoutedEventArgs e)
    {
        _session.Settings.ShowGrid = GridMenu.IsChecked == true;
        SettingsStore.Save(_session.Settings);
        RefreshView();
    }

    private void OnSnap(object? sender, RoutedEventArgs e)
    {
        _session.Settings.SnapEnabled = SnapMenu.IsChecked == true;
        SettingsStore.Save(_session.Settings);
        UpdateStatus();
    }

    private async void OnAbout(object? sender, RoutedEventArgs e)
        => await Dialogs.ShowMessageAsync(this, "About Motion Maker", "Motion Maker builds parts from sketches, pads, holes, and gears. Assemblies keep a plate for a later placement and motion study. Export writes the part body as binary STL.");

    private void OnSelectTool(object? sender, RoutedEventArgs e) => _session.SetTool(SketchTool.Select);

    private void OnLineTool(object? sender, RoutedEventArgs e) => _session.SetTool(SketchTool.Line);

    private void OnRectangleTool(object? sender, RoutedEventArgs e) => _session.SetTool(SketchTool.Rectangle);

    private void OnCircleTool(object? sender, RoutedEventArgs e) => _session.SetTool(SketchTool.Circle);

    private void OnSlotTool(object? sender, RoutedEventArgs e) => _session.SetTool(SketchTool.Slot);

    private void OnHomeTab(object? sender, RoutedEventArgs e) => ShowRibbon(0);

    private void OnSketchTab(object? sender, RoutedEventArgs e) => ShowRibbon(1);

    private void OnFeaturesTab(object? sender, RoutedEventArgs e) => ShowRibbon(2);

    private void OnPad(object? sender, RoutedEventArgs e) => _session.AddPad();

    private void OnHole(object? sender, RoutedEventArgs e) => _session.AddHole();

    private void OnNewSketch(object? sender, RoutedEventArgs e) => _session.AddFaceSketch();

    private void OnGear(object? sender, RoutedEventArgs e) => _session.AddDefaultGear();

    private async void OnGearWizard(object? sender, RoutedEventArgs e)
    {
        var request = await Dialogs.AskGearAsync(this);
        if (request is null)
        {
            return;
        }

        _session.AddGear(request.Kind, request.Module, request.Teeth, request.Pressure, request.FaceWidth, request.Bore, request.Pitch);
    }

    private async void OnExportStl(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export STL",
            SuggestedFileName = $"{_session.SelectedPart?.Name ?? "Part"}.stl",
            DefaultExtension = "stl",
            FileTypeChoices = [new FilePickerFileType("STL mesh") { Patterns = ["*.stl"] }]
        });
        var path = file?.TryGetLocalPath();
        if (path is null)
        {
            return;
        }

        if (!_session.TryExportStl(path, out var error) && error is not null)
        {
            await Dialogs.ShowMessageAsync(this, "Export STL", error);
        }
    }

    private void OnHorizontal(object? sender, RoutedEventArgs e) => _session.ApplyLineConstraint(ConstraintKind.Horizontal);

    private void OnVertical(object? sender, RoutedEventArgs e) => _session.ApplyLineConstraint(ConstraintKind.Vertical);

    private void OnCoincident(object? sender, RoutedEventArgs e) => _session.BeginCoincident();

    private async void OnLength(object? sender, RoutedEventArgs e)
    {
        var sketch = _session.ActiveSketch;
        var line = sketch?.Lines.FirstOrDefault(item => item.Id == _session.ItemId);
        if (sketch is null || line is null || _session.SelectedKind != ProjectNodeKind.Line)
        {
            return;
        }

        var length = MotionMaker.Geometry.SketchEditor.LineLength(sketch, line);
        var text = await Dialogs.PromptAsync(this, "Line length", "Length in millimeters", length.ToString("0.###"));
        if (text is null || !double.TryParse(text, out var value))
        {
            return;
        }

        _session.ApplyLength(value);
    }

    private async void OnRadius(object? sender, RoutedEventArgs e)
    {
        var circle = _session.ActiveSketch?.Circles.FirstOrDefault(item => item.Id == _session.ItemId);
        if (circle is null || _session.SelectedKind != ProjectNodeKind.Circle)
        {
            return;
        }

        var text = await Dialogs.PromptAsync(this, "Circle radius", "Radius in millimeters", circle.Radius.ToString("0.###"));
        if (text is null || !double.TryParse(text, out var value))
        {
            return;
        }

        _session.ApplyRadius(value);
    }

    private void OnPlateEdited(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_syncingProperties || _session.SelectedAssembly is not AssemblyDesign assembly)
        {
            return;
        }

        var width = (double)(PlateWidth.Value ?? 0);
        var depth = (double)(PlateDepth.Value ?? 0);
        if (Math.Abs(assembly.Plate.WidthMm - width) < 1e-6 && Math.Abs(assembly.Plate.DepthMm - depth) < 1e-6)
        {
            return;
        }

        _session.SetPlateSize(width, depth);
    }

    private void OnExplorerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingTree || Explorer.SelectedItem is not TreeViewItem { Tag: ProjectNode node })
        {
            return;
        }

        _session.Select(node);
    }

    private void OnViewportPressed(object? sender, PointerPressedEventArgs e)
    {
        var markerPoint = e.GetPosition(MarkerLayer);
        foreach (var (bounds, featureId) in _bubbleCloses)
        {
            if (bounds.Contains(markerPoint))
            {
                _session.DismissError(featureId);
                e.Handled = true;
                return;
            }
        }

        Scene.BeginPointer(e, e.GetPosition(Scene));
        if (e.Handled)
        {
            e.Pointer.Capture(ViewportHost);
        }
    }

    private void OnViewportMoved(object? sender, PointerEventArgs e)
        => Scene.MovePointer(e, e.GetPosition(Scene));

    private void OnViewportReleased(object? sender, PointerReleasedEventArgs e)
    {
        Scene.EndPointer(e.GetPosition(Scene));
        e.Pointer.Capture(null);
    }

    private void OnViewportCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        => Scene.EndPointer(null);

    private void OnViewportWheel(object? sender, PointerWheelEventArgs e)
        => Scene.WheelPointer(e, e.GetPosition(Scene));

    private void OnCursorMoved(double x, double y, double z, bool hit)
    {
        _cursor = hit ? $"X {x:0.00}    Y {y:0.00}    Z {z:0.00} mm" : "—";
        UpdateStatus();
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (FocusManager?.GetFocusedElement() is TextBox)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            _session.CancelGesture();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            OnDelete(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.B && e.KeyModifiers == KeyModifiers.None)
        {
            OnFrame(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Z && e.KeyModifiers == KeyModifiers.None)
        {
            OnFrame(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.K && e.KeyModifiers == KeyModifiers.None)
        {
            _session.TogglePerspective();
            e.Handled = true;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_acceptClose)
        {
            RememberWindowSize();
            return;
        }

        if (!_session.History.IsDirty)
        {
            RememberWindowSize();
            return;
        }

        e.Cancel = true;
        if (_confirmingClose)
        {
            return;
        }

        _confirmingClose = true;
        Dispatcher.UIThread.Post(ConfirmCloseAsync);
    }

    private async void ConfirmCloseAsync()
    {
        try
        {
            var choice = await Dialogs.AskSaveAsync(
                this,
                "This project has unsaved changes. Save them before closing?");
            if (choice == SaveChoice.Cancel)
            {
                return;
            }

            if (choice == SaveChoice.Save && !await SaveAsync(saveAs: false))
            {
                return;
            }

            _acceptClose = true;
            Close();
        }
        finally
        {
            _confirmingClose = false;
        }
    }

    private void RememberWindowSize()
    {
        _session.Settings.WindowWidth = Width;
        _session.Settings.WindowHeight = Height;
        SettingsStore.Save(_session.Settings);
    }

    private void RefreshAll()
    {
        Title = $"Motion Maker — {_session.DisplayName}{(_session.History.IsDirty ? "*" : "")}";
        UndoMenu.IsEnabled = _session.History.CanUndo;
        RedoMenu.IsEnabled = _session.History.CanRedo;
        RebuildTree();
        UpdateProperties();
        RefreshView();
    }

    private void RefreshView()
    {
        Title = $"Motion Maker — {_session.DisplayName}{(_session.History.IsDirty ? "*" : "")}";
        var partSelected = _session.SelectedPart is not null;
        SelectTool.IsEnabled = partSelected;
        LineTool.IsEnabled = partSelected;
        RectangleTool.IsEnabled = partSelected;
        CircleTool.IsEnabled = partSelected;
        SlotTool.IsEnabled = partSelected;
        HorizontalTool.IsEnabled = partSelected;
        VerticalTool.IsEnabled = partSelected;
        CoincidentTool.IsEnabled = partSelected;
        LengthTool.IsEnabled = partSelected;
        RadiusTool.IsEnabled = partSelected;
        PadButton.IsEnabled = _session.CanPad;
        HoleButton.IsEnabled = partSelected;
        NewSketchButton.IsEnabled = partSelected;
        GearButton.IsEnabled = partSelected;
        GearWizardButton.IsEnabled = partSelected;
        MarkTool(SelectTool, _session.Tool == SketchTool.Select);
        MarkTool(LineTool, _session.Tool == SketchTool.Line);
        MarkTool(RectangleTool, _session.Tool == SketchTool.Rectangle);
        MarkTool(CircleTool, _session.Tool == SketchTool.Circle);
        MarkTool(SlotTool, _session.Tool == SketchTool.Slot);
        Scene.LeftButtonOrbits = _session.SelectedPart is null;
        Scene.PickOrOrbit = partSelected && !_session.EditingSketch;
        Scene.InvertWheel = _session.Settings.InvertWheel;
        Scene.OrbitSensitivity = _session.Settings.OrbitSensitivity;
        Scene.PanSensitivity = _session.Settings.PanSensitivity;
        Scene.Scene = _session.BuildScene();
        Scene.InvalidateView();
        EmptyPrompt.IsVisible = _session.ActiveCamera is null;
        PerspectiveMenu.IsChecked = _session.ActiveCamera?.Perspective == true;
        UpdateMarkers();
        UpdateStatus();
        UpdateGlStatus();
    }

    private void UpdateGlStatus()
    {
        GlStatus.Text = Scene.GlError;
        GlStatus.IsVisible = !string.IsNullOrWhiteSpace(Scene.GlError);
    }

    private void UpdateStatus()
    {
        var camera = _session.ActiveCamera;
        var view = camera is null ? "" : camera.Perspective ? "Perspective" : "Orthographic";
        var snap = _session.Settings.SnapEnabled ? $"Snap {_session.Settings.SnapSpacingMm:0.##} mm" : "Snap off";
        CursorStatus.Text = _cursor;
        ActiveStatus.Text = $"{_session.ActiveLabel}    {view}    {snap}";
        MessageStatus.Text = _session.StatusMessage ?? _session.ToolHint();
    }

    private void UpdateProperties()
    {
        _syncingProperties = true;
        var assembly = _session.SelectedAssembly;
        PlateProperties.IsVisible = assembly is not null && _session.SelectedKind is ProjectNodeKind.Assembly or ProjectNodeKind.Plate;
        if (assembly is not null && PlateProperties.IsVisible)
        {
            PlateWidth.Value = (decimal)assembly.Plate.WidthMm;
            PlateDepth.Value = (decimal)assembly.Plate.DepthMm;
        }

        var key = PropertyKey();
        if (key != _propertyKey || !FeatureProperties.IsKeyboardFocusWithin)
        {
            _propertyKey = key;
            BuildFeatureProperties();
        }
        else
        {
            PushFeatureValues();
        }

        var solid = _session.SelectedPart?.Body.HasSolid == true;
        PropertyNote.Text = _session.SelectedKind switch
        {
            ProjectNodeKind.Assembly or ProjectNodeKind.Plate => "Each assembly has its own plate. Parts stay independent until a later stage places them here.",
            ProjectNodeKind.Body => solid ? "The body is the last successful solid on this part." : "This part has no solid yet. A closed sketch can be padded.",
            ProjectNodeKind.Bom or ProjectNodeKind.BomLine => "The bill of materials counts placed copies. Nothing is placed yet.",
            ProjectNodeKind.Placements or ProjectNodeKind.Placement => "Placements will list parts on this plate. The list is empty.",
            _ => ""
        };
        _syncingProperties = false;
    }

    private string PropertyKey()
    {
        var points = _session.SelectedPointIds.Count;
        var feature = _session.SelectedFeature;
        return $"{_session.SelectedKind}:{_session.OwnerId}:{_session.ItemId}:{points}:{feature?.Kind}:{feature?.GearType}:{feature?.Error}";
    }

    private void BuildFeatureProperties()
    {
        FeatureProperties.Children.Clear();
        _propertyBoxes.Clear();
        var feature = _session.SelectedFeature;
        if (feature is { State: FeatureState.Frozen } && !string.IsNullOrWhiteSpace(feature.Error))
        {
            FeatureProperties.Children.Add(new TextBlock { Text = feature.Error, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed });
        }

        switch (_session.SelectedKind)
        {
            case ProjectNodeKind.Sketch:
                BuildSketchProperties();
                break;
            case ProjectNodeKind.Line:
                BuildLineProperties();
                break;
            case ProjectNodeKind.Circle:
                BuildCircleProperties();
                break;
            case ProjectNodeKind.Slot:
                BuildSlotProperties();
                break;
            case ProjectNodeKind.Constraint:
                BuildConstraintProperties();
                break;
            case ProjectNodeKind.Pad when feature is not null:
                AddNumber("Distance (mm)", "distance", feature.DistanceMm, 0.1, 5000, 1, () => _session.SetPad(Read("distance"), ReadCheck("reversed")));
                AddCheck("Reversed", "reversed", feature.Reversed, () => _session.SetPad(Read("distance"), ReadCheck("reversed")));
                break;
            case ProjectNodeKind.Hole when feature is not null:
                AddNumber("Depth (mm)", "depth", feature.DepthMm, 0.1, 5000, 1, () => _session.SetHole(Read("depth"), ReadCheck("through")));
                AddCheck("Through all", "through", feature.ThroughAll, () => _session.SetHole(Read("depth"), ReadCheck("through")));
                break;
            case ProjectNodeKind.Gear when feature is not null:
                if (feature.GearType == GearKind.Bevel)
                {
                    FeatureProperties.Children.Add(new TextBlock { Text = "Straight bevel", TextWrapping = TextWrapping.Wrap });
                }

                AddNumber("Module (mm)", "module", feature.ModuleMm, 0.1, 100, 0.1, CommitGear);
                AddNumber("Teeth", "teeth", feature.Teeth, 6, 300, 1, CommitGear);
                AddNumber("Pressure angle (°)", "pressure", feature.PressureAngleDegrees, 1, 45, 1, CommitGear);
                AddNumber("Face width (mm)", "face", feature.FaceWidthMm, 0.1, 500, 1, CommitGear);
                AddNumber("Bore (mm)", "bore", feature.BoreMm, 0, 500, 0.5, CommitGear);
                if (feature.GearType == GearKind.Bevel)
                {
                    AddNumber("Pitch angle (°)", "pitch", feature.PitchAngleDegrees, 1, 89, 1, CommitGear);
                }

                break;
            default:
                BuildPointProperties();
                break;
        }
    }

    private void BuildSketchProperties()
    {
        var sketch = _session.ActiveSketch;
        if (sketch is null)
        {
            BuildPointProperties();
            return;
        }

        FeatureProperties.Children.Add(new TextBlock { Text = _session.SketchPlaneName(sketch), TextWrapping = TextWrapping.Wrap });
        if (_session.SelectedPointIds.Count > 0)
        {
            BuildPointProperties();
        }

        if (_session.CanPad)
        {
            var pad = new Button { Content = "Pad", HorizontalAlignment = HorizontalAlignment.Stretch };
            pad.Click += (_, _) => _session.AddPad();
            FeatureProperties.Children.Add(pad);
        }
    }

    private void BuildPointProperties()
    {
        var sketch = _session.ActiveSketch;
        if (sketch is null || _session.SelectedPointIds.Count == 0)
        {
            return;
        }

        var points = sketch.Points.Where(point => _session.SelectedPointIds.Contains(point.Id)).ToList();
        if (points.Count == 0)
        {
            return;
        }

        if (points.Count > 1)
        {
            FeatureProperties.Children.Add(new TextBlock { Text = $"{points.Count} vertices" });
        }

        var shared = points.All(point => Math.Abs(point.X - points[0].X) < 1e-4 && Math.Abs(point.Y - points[0].Y) < 1e-4);
        if (points.Count > 1 && !shared)
        {
            return;
        }

        AddNumber("X (mm)", "x", points[0].X, -5000, 5000, 1, () => _session.SetPoint(Read("x"), Read("y")));
        AddNumber("Y (mm)", "y", points[0].Y, -5000, 5000, 1, () => _session.SetPoint(Read("x"), Read("y")));
    }

    private void BuildLineProperties()
    {
        var sketch = _session.ActiveSketch;
        var line = sketch?.Lines.FirstOrDefault(item => item.Id == _session.ItemId);
        if (sketch is null || line is null)
        {
            return;
        }

        AddNumber("Length (mm)", "length", SketchEditor.LineLength(sketch, line), 0.1, 5000, 1, () => _session.ApplyLength(Read("length")));
    }

    private void BuildCircleProperties()
    {
        var circle = _session.ActiveSketch?.Circles.FirstOrDefault(item => item.Id == _session.ItemId);
        if (circle is null)
        {
            return;
        }

        AddNumber("Radius (mm)", "radius", circle.Radius, 0.1, 5000, 1, () => _session.ApplyRadius(Read("radius")));
    }

    private void BuildSlotProperties()
    {
        var slot = _session.ActiveSketch?.Slots.FirstOrDefault(item => item.Id == _session.ItemId);
        if (slot is null)
        {
            return;
        }

        AddNumber("Length (mm)", "length", slot.Length, 1, 5000, 1, CommitSlot);
        AddNumber("Width (mm)", "width", slot.Width, 0.5, 5000, 0.5, CommitSlot);
        AddNumber("Angle (°)", "angle", slot.AngleDegrees, -360, 360, 1, CommitSlot);
    }

    private void BuildConstraintProperties()
    {
        var constraint = _session.ActiveSketch?.Constraints.FirstOrDefault(item => item.Id == _session.ItemId);
        if (constraint is null || constraint.Kind is not (ConstraintKind.Length or ConstraintKind.Radius))
        {
            if (constraint is not null)
            {
                FeatureProperties.Children.Add(new TextBlock { Text = ProjectTreeBuilder.Describe(constraint) });
            }

            return;
        }

        AddNumber(constraint.Kind == ConstraintKind.Length ? "Length (mm)" : "Radius (mm)", "value", constraint.Value, 0.1, 5000, 1, () => _session.SetConstraintValue(Read("value")));
    }

    private void CommitSlot() => _session.SetSlot(Read("length"), Read("width"), Read("angle"));

    private void CommitGear()
    {
        var feature = _session.SelectedFeature;
        if (feature is null)
        {
            return;
        }

        _session.SetGear(
            Read("module"),
            (int)Math.Round(Read("teeth")),
            Read("pressure"),
            Read("face"),
            Read("bore"),
            feature.GearType == GearKind.Bevel ? Read("pitch") : feature.PitchAngleDegrees);
    }

    private void AddNumber(string label, string key, double value, double minimum, double maximum, double increment, Action commit)
    {
        FeatureProperties.Children.Add(new TextBlock { Text = label });
        var box = new NumericUpDown
        {
            Minimum = (decimal)minimum,
            Maximum = (decimal)maximum,
            Increment = (decimal)increment,
            FormatString = "0.###",
            Value = (decimal)value
        };
        box.ValueChanged += (_, _) =>
        {
            if (!_syncingProperties)
            {
                commit();
            }
        };
        _propertyBoxes.Add((key, box));
        FeatureProperties.Children.Add(box);
    }

    private void AddCheck(string label, string key, bool value, Action commit)
    {
        var box = new CheckBox { Content = label, IsChecked = value, Tag = key };
        box.IsCheckedChanged += (_, _) =>
        {
            if (!_syncingProperties)
            {
                commit();
            }
        };
        FeatureProperties.Children.Add(box);
    }

    private double Read(string key)
        => (double)(_propertyBoxes.LastOrDefault(item => item.Key == key).Box?.Value ?? 0);

    private bool ReadCheck(string key)
    {
        foreach (var child in FeatureProperties.Children)
        {
            if (child is CheckBox box && Equals(box.Tag, key))
            {
                return box.IsChecked == true;
            }
        }

        return false;
    }

    private void PushFeatureValues()
    {
        switch (_session.SelectedKind)
        {
            case ProjectNodeKind.Sketch:
                var selected = _session.ActiveSketch?.Points.Where(point => _session.SelectedPointIds.Contains(point.Id)).ToList();
                if (selected is { Count: > 0 } && selected.All(point => Math.Abs(point.X - selected[0].X) < 1e-4 && Math.Abs(point.Y - selected[0].Y) < 1e-4))
                {
                    SetBox("x", selected[0].X);
                    SetBox("y", selected[0].Y);
                }

                break;
            case ProjectNodeKind.Constraint:
                var constraint = _session.ActiveSketch?.Constraints.FirstOrDefault(item => item.Id == _session.ItemId);
                if (constraint is not null)
                {
                    SetBox("value", constraint.Value);
                }

                break;
            case ProjectNodeKind.Line:
                var sketch = _session.ActiveSketch;
                var line = sketch?.Lines.FirstOrDefault(item => item.Id == _session.ItemId);
                if (sketch is not null && line is not null)
                {
                    SetBox("length", SketchEditor.LineLength(sketch, line));
                }

                break;
            case ProjectNodeKind.Circle:
                var circle = _session.ActiveSketch?.Circles.FirstOrDefault(item => item.Id == _session.ItemId);
                if (circle is not null)
                {
                    SetBox("radius", circle.Radius);
                }

                break;
            case ProjectNodeKind.Slot:
                var slot = _session.ActiveSketch?.Slots.FirstOrDefault(item => item.Id == _session.ItemId);
                if (slot is not null)
                {
                    SetBox("length", slot.Length);
                    SetBox("width", slot.Width);
                    SetBox("angle", slot.AngleDegrees);
                }

                break;
            case ProjectNodeKind.Pad when _session.SelectedFeature is PartFeature pad:
                SetBox("distance", pad.DistanceMm);
                SetCheck("reversed", pad.Reversed);
                break;
            case ProjectNodeKind.Hole when _session.SelectedFeature is PartFeature hole:
                SetBox("depth", hole.DepthMm);
                SetCheck("through", hole.ThroughAll);
                break;
            case ProjectNodeKind.Gear when _session.SelectedFeature is PartFeature gear:
                SetBox("module", gear.ModuleMm);
                SetBox("teeth", gear.Teeth);
                SetBox("pressure", gear.PressureAngleDegrees);
                SetBox("face", gear.FaceWidthMm);
                SetBox("bore", gear.BoreMm);
                SetBox("pitch", gear.PitchAngleDegrees);
                break;
        }
    }

    private void SetBox(string key, double value)
    {
        var box = _propertyBoxes.LastOrDefault(item => item.Key == key).Box;
        if (box is not null)
        {
            box.Value = (decimal)value;
        }
    }

    private void SetCheck(string key, bool value)
    {
        foreach (var child in FeatureProperties.Children)
        {
            if (child is CheckBox box && Equals(box.Tag, key))
            {
                box.IsChecked = value;
            }
        }
    }

    private void UpdateMarkers()
    {
        MarkerLayer.Children.Clear();
        _bubbleCloses.Clear();
        foreach (var marker in _session.GetMarkers())
        {
            if (!Scene.TryWorldToScreen(marker.X, marker.Y, marker.Z, out var screen))
            {
                continue;
            }

            var label = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#CC2C3138")),
                Padding = new Thickness(4, 1),
                CornerRadius = new CornerRadius(3),
                Child = new TextBlock { Text = marker.Text, FontSize = 11 }
            };
            Canvas.SetLeft(label, screen.X + 8);
            Canvas.SetTop(label, screen.Y - 10);
            MarkerLayer.Children.Add(label);
        }

        if (_session.HasMarquee)
        {
            var (x1, y1, x2, y2) = _session.Marquee;
            var crossing = x2 < x1;
            var marquee = new Border
            {
                Width = Math.Max(1, Math.Abs(x2 - x1)),
                Height = Math.Max(1, Math.Abs(y2 - y1)),
                BorderBrush = crossing ? Brushes.MediumSeaGreen : Brushes.CornflowerBlue,
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(crossing ? Color.Parse("#2232A06A") : Color.Parse("#224A90E2"))
            };
            Canvas.SetLeft(marquee, Math.Min(x1, x2));
            Canvas.SetTop(marquee, Math.Min(y1, y2));
            MarkerLayer.Children.Add(marquee);
        }

        foreach (var bubble in _session.GetBubbles())
        {
            if (!Scene.TryWorldToScreen(bubble.X, bubble.Y, bubble.Z, out var screen))
            {
                continue;
            }

            AddBubble(bubble, screen);
        }
    }

    private void AddBubble(ErrorBubble bubble, Point screen)
    {
        var close = new TextBlock
        {
            Text = "×",
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8, 0, 0, 0)
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = bubble.Message, TextWrapping = TextWrapping.Wrap, MaxWidth = 180 });
        row.Children.Add(close);
        var body = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#F23A2428")),
            BorderBrush = Brushes.IndianRed,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 6, 8, 8),
            Child = row
        };
        body.Measure(new Size(240, 400));
        var size = body.DesiredSize;
        var left = screen.X + 18;
        var top = screen.Y - size.Height - 22;
        Canvas.SetLeft(body, left);
        Canvas.SetTop(body, top);
        MarkerLayer.Children.Add(body);
        var tail = new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = Brushes.IndianRed
        };
        Canvas.SetLeft(tail, screen.X - 5);
        Canvas.SetTop(tail, screen.Y - 5);
        MarkerLayer.Children.Add(tail);
        _bubbleCloses.Add((new Rect(left + size.Width - 24, top + 4, 20, 20), bubble.FeatureId));
    }

    private void ShowRibbon(int tab)
    {
        HomeRibbon.IsVisible = tab == 0;
        SketchRibbon.IsVisible = tab == 1;
        FeaturesRibbon.IsVisible = tab == 2;
        MarkTool(HomeTab, tab == 0);
        MarkTool(SketchTab, tab == 1);
        MarkTool(FeaturesTab, tab == 2);
    }

    private static void MarkTool(Button button, bool active)
    {
        if (active)
        {
            button.Classes.Add("active");
        }
        else
        {
            button.Classes.Remove("active");
        }
    }

    private void RebuildTree()
    {
        _updatingTree = true;
        Explorer.Items.Clear();
        TreeViewItem? selected = null;
        foreach (var node in ProjectTreeBuilder.Build(_session.Document))
        {
            var item = CreateTreeItem(node, ref selected);
            Explorer.Items.Add(item);
        }

        if (selected is not null)
        {
            selected.IsSelected = true;
            selected.BringIntoView();
        }

        _updatingTree = false;
    }

    private TreeViewItem CreateTreeItem(ProjectNode node, ref TreeViewItem? selected)
    {
        var item = new TreeViewItem
        {
            Header = TreeHeader(node),
            Tag = node,
            IsExpanded = true
        };
        if (_session.HasSelection
            && node.Kind == _session.SelectedKind
            && node.OwnerId == _session.OwnerId
            && node.ItemId == _session.ItemId)
        {
            selected = item;
        }

        foreach (var child in node.Children)
        {
            item.Items.Add(CreateTreeItem(child, ref selected));
        }

        return item;
    }

    private Control TreeHeader(ProjectNode node)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (this.TryGetResource($"Icon.{node.Icon}", ActualThemeVariant, out var resource) && resource is Avalonia.Media.Geometry geometry)
        {
            panel.Children.Add(new Avalonia.Controls.Shapes.Path
            {
                Data = geometry,
                Width = 16,
                Height = 16,
                Stretch = Stretch.Uniform,
                Stroke = node.Frozen ? Brushes.IndianRed : Brushes.White,
                StrokeThickness = 1.3,
                StrokeJoin = PenLineJoin.Round,
                StrokeLineCap = PenLineCap.Round,
                Fill = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        panel.Children.Add(new TextBlock
        {
            Text = node.Title,
            Foreground = node.Frozen ? Brushes.IndianRed : Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        });
        return panel;
    }

    private void RebuildRecentMenu()
    {
        RecentMenu.Items.Clear();
        if (_session.Settings.RecentFiles.Count == 0)
        {
            RecentMenu.Items.Add(new MenuItem { Header = "(empty)", IsEnabled = false });
            return;
        }

        foreach (var path in _session.Settings.RecentFiles)
        {
            var item = new MenuItem { Header = path, Tag = path };
            item.Click += async (_, _) =>
            {
                if (!await ConfirmDiscardAsync())
                {
                    return;
                }

                try
                {
                    _session.LoadFile(path);
                    RebuildRecentMenu();
                }
                catch (Exception ex)
                {
                    await Dialogs.ShowMessageAsync(this, "Open project", ex.Message);
                }
            };
            RecentMenu.Items.Add(item);
        }
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!_session.History.IsDirty)
        {
            return true;
        }

        var choice = await Dialogs.AskSaveAsync(this);
        if (choice == SaveChoice.Cancel)
        {
            return false;
        }

        if (choice == SaveChoice.Save)
        {
            return await SaveAsync(saveAs: false);
        }

        return true;
    }

    private async Task<bool> SaveAsync(bool saveAs)
    {
        var path = _session.FilePath;
        if (saveAs || string.IsNullOrEmpty(path))
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save project",
                SuggestedFileName = string.IsNullOrEmpty(path) ? "Untitled.mmproj" : System.IO.Path.GetFileName(path),
                DefaultExtension = "mmproj",
                FileTypeChoices = [ProjectFileType()]
            });
            path = file?.TryGetLocalPath();
            if (path is null)
            {
                return false;
            }
        }

        try
        {
            _session.SaveFile(path);
            RebuildRecentMenu();
            return true;
        }
        catch (Exception ex)
        {
            await Dialogs.ShowMessageAsync(this, "Save project", ex.Message);
            return false;
        }
    }

    private static FilePickerFileType ProjectFileType() => new("Motion Maker project")
    {
        Patterns = ["*.mmproj"]
    };
}
