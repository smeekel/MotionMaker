using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MotionMaker.Core;
using MotionMaker.Viewport;

namespace MotionMaker.App;

public partial class MainWindow : Window
{
    private readonly Session _session;
    private bool _updatingTree;
    private bool _syncingProperties;
    private bool _acceptClose;
    private bool _confirmingClose;
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
        => await Dialogs.ShowMessageAsync(this, "About Motion Maker", "Motion Maker builds independent 3D parts and assemblies that place them. This stage covers the project, the plate, and part sketches.");

    private void OnSelectTool(object? sender, RoutedEventArgs e) => _session.SetTool(SketchTool.Select);

    private void OnLineTool(object? sender, RoutedEventArgs e) => _session.SetTool(SketchTool.Line);

    private void OnRectangleTool(object? sender, RoutedEventArgs e) => _session.SetTool(SketchTool.Rectangle);

    private void OnCircleTool(object? sender, RoutedEventArgs e) => _session.SetTool(SketchTool.Circle);

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

    private void OnCursorMoved(double x, double y, bool hit)
    {
        _cursor = hit ? $"X {x:0.00}    Y {y:0.00}    Z 0.00 mm" : "—";
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
        SketchTools.IsVisible = _session.SelectedPart is not null;
        SelectTool.FontWeight = _session.Tool == SketchTool.Select ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal;
        LineTool.FontWeight = _session.Tool == SketchTool.Line ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal;
        RectangleTool.FontWeight = _session.Tool == SketchTool.Rectangle ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal;
        CircleTool.FontWeight = _session.Tool == SketchTool.Circle ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal;
        Scene.LeftButtonOrbits = _session.SelectedPart is null;
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

        PropertyNote.Text = _session.SelectedKind switch
        {
            ProjectNodeKind.Assembly or ProjectNodeKind.Plate => "Each assembly has its own plate. Parts stay independent until a later stage places them here.",
            ProjectNodeKind.Body => "This part has no solid yet. The sketch is the input. Extrude arrives in a later stage.",
            ProjectNodeKind.Bom or ProjectNodeKind.BomLine => "The bill of materials counts placed copies. Nothing is placed yet.",
            ProjectNodeKind.Placements or ProjectNodeKind.Placement => "Placements will list parts on this plate. The list is empty.",
            ProjectNodeKind.Part or ProjectNodeKind.Sketch or ProjectNodeKind.Line or ProjectNodeKind.Circle or ProjectNodeKind.Constraint
                when _session.SelectedPart is PartDesign part =>
                $"Part \"{part.Name}\". Sketch: {part.Sketches.FirstOrDefault()?.Lines.Count ?? 0} lines, {part.Sketches.FirstOrDefault()?.Circles.Count ?? 0} circles, {part.Sketches.FirstOrDefault()?.Constraints.Count ?? 0} constraints.",
            _ => assembly is null ? "Select an assembly or a part." : assembly.Name
        };
        _syncingProperties = false;
    }

    private void UpdateMarkers()
    {
        MarkerLayer.Children.Clear();
        foreach (var marker in _session.GetMarkers())
        {
            if (!Scene.TryWorldToScreen(marker.X, marker.Y, 0, out var screen))
            {
                continue;
            }

            var label = new Border
            {
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#CC2C3138")),
                Padding = new Avalonia.Thickness(4, 1),
                CornerRadius = new Avalonia.CornerRadius(3),
                Child = new TextBlock { Text = marker.Text, FontSize = 11 }
            };
            Canvas.SetLeft(label, screen.X + 8);
            Canvas.SetTop(label, screen.Y - 10);
            MarkerLayer.Children.Add(label);
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
            Header = node.Title,
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
                SuggestedFileName = string.IsNullOrEmpty(path) ? "Untitled.mmproj" : Path.GetFileName(path),
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
