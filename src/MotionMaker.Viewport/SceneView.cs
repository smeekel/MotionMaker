using Avalonia;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using MotionMaker.Core;

namespace MotionMaker.Viewport;

public sealed class SceneView : OpenGlControlBase
{
    private readonly GlRenderer _renderer = new();
    private bool _glReady;
    private Point _last;
    private bool _orbiting;
    private bool _panning;
    private bool _sketching;
    private bool _pendingPick;
    private bool _shift;
    private double _lastU;
    private double _lastV;
    private Vec3 _lastWorld;
    private Vec3 _rayOrigin;
    private Vec3 _rayDirection;
    private bool _lastHit;
    private SceneSnapshot _snapshot = new();

    public SceneView()
    {
        Focusable = true;
    }

    public ViewportScene? Scene { get; set; }

    public bool LeftButtonOrbits { get; set; } = true;

    public bool PickOrOrbit { get; set; }

    public bool InvertWheel { get; set; }

    public double OrbitSensitivity { get; set; } = 1;

    public double PanSensitivity { get; set; } = 1;

    public bool HasRendered { get; private set; }

    public string? GlError { get; private set; }

    public event Action? GlStateChanged;

    public event Action? ViewChanged;

    public event Action<double, double, double, bool>? CursorMoved;

    public event Action<SketchPointer>? FaceClicked;

    public event Action<SketchPointer>? SketchGesture;

    public void InvalidateView()
    {
        _snapshot = SceneSnapshot.From(Scene);
        RequestNextFrameRendering();
    }

    public bool TryWorldToScreen(double x, double y, double z, out Point screen)
    {
        screen = default;
        if (Scene?.Camera is null || Bounds.Width <= 1 || Bounds.Height <= 1)
        {
            return false;
        }

        if (!CameraMath.TryProject(Scene.Camera, Bounds.Width, Bounds.Height, new Vec3(x, y, z), out var projected))
        {
            return false;
        }

        screen = new Point((projected.NdcX * 0.5 + 0.5) * Bounds.Width, (1 - (projected.NdcY * 0.5 + 0.5)) * Bounds.Height);
        return projected.NdcX is > -1.05 and < 1.05 && projected.NdcY is > -1.05 and < 1.05;
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        try
        {
            _renderer.Init(gl);
            _glReady = true;
            GlError = null;
        }
        catch (Exception ex)
        {
            GlError = ex.Message;
            _glReady = false;
            NotifyGlState();
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        _renderer.DisposeGl();
        _glReady = false;
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (!_glReady)
        {
            return;
        }

        try
        {
            var scale = VisualRoot?.RenderScaling ?? 1;
            var pixelsW = Math.Max(1, (int)(Bounds.Width * scale));
            var pixelsH = Math.Max(1, (int)(Bounds.Height * scale));
            _renderer.Render(gl, fb, pixelsW, pixelsH, Bounds.Width, Bounds.Height, _snapshot);
            HasRendered = true;
            GlError = _renderer.Error;
        }
        catch (Exception ex)
        {
            GlError = ex.Message;
            NotifyGlState();
        }
    }

    public void BeginPointer(PointerPressedEventArgs e, Point point)
    {
        Focus();
        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsRightButtonPressed || properties.IsMiddleButtonPressed)
        {
            _panning = true;
            _last = point;
            e.Handled = true;
            return;
        }

        if (!properties.IsLeftButtonPressed || Scene?.Camera is null)
        {
            return;
        }

        _shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var forceOrbit = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (PickOrOrbit && !forceOrbit)
        {
            _pendingPick = true;
            _last = point;
            e.Handled = true;
            return;
        }

        if (LeftButtonOrbits || forceOrbit)
        {
            _orbiting = true;
            _last = point;
            e.Handled = true;
            return;
        }

        _sketching = true;
        _last = point;
        UpdateCursor(point);
        RaiseSketch(SketchPointerPhase.Pressed);
        e.Handled = true;
    }

    public void MovePointer(PointerEventArgs e, Point point)
    {
        _shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (_pendingPick && Math.Abs(point.X - _last.X) + Math.Abs(point.Y - _last.Y) > 5)
        {
            _pendingPick = false;
            _orbiting = true;
        }

        if (Scene?.Camera is CameraState camera && (_orbiting || _panning))
        {
            var dx = point.X - _last.X;
            var dy = point.Y - _last.Y;
            _last = point;
            if (_orbiting)
            {
                CameraMath.Orbit(camera, dx, dy, OrbitSensitivity);
            }
            else
            {
                CameraMath.Pan(camera, dx, dy, Math.Max(Bounds.Height, 1), PanSensitivity);
            }

            InvalidateView();
            ViewChanged?.Invoke();
        }
        else if (_sketching)
        {
            _last = point;
        }

        UpdateCursor(point);
        if (_sketching)
        {
            RaiseSketch(SketchPointerPhase.Moved);
        }

        e.Handled = _orbiting || _panning || _sketching;
    }

    public void EndPointer(Point? point)
    {
        var wasActive = _orbiting || _panning || _sketching || _pendingPick;
        var pick = _pendingPick;
        _pendingPick = false;
        EndCameraDrag();
        UpdateCursor(point ?? _last);
        if (pick)
        {
            FaceClicked?.Invoke(MakePointer(SketchPointerPhase.Released));
        }

        FinishSketch();
        if (wasActive)
        {
            InvalidateView();
        }
    }

    public void WheelPointer(PointerWheelEventArgs e, Point point)
    {
        if (Scene?.Camera is not CameraState camera)
        {
            return;
        }

        CameraMath.Zoom(camera, e.Delta.Y, InvertWheel, 1);
        InvalidateView();
        ViewChanged?.Invoke();
        UpdateCursor(point);
        e.Handled = true;
    }

    private void EndCameraDrag()
    {
        _orbiting = false;
        _panning = false;
    }

    private void FinishSketch()
    {
        if (!_sketching)
        {
            return;
        }

        _sketching = false;
        RaiseSketch(SketchPointerPhase.Released);
    }

    private void NotifyGlState()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            GlStateChanged?.Invoke();
        }
        else
        {
            Dispatcher.UIThread.Post(() => GlStateChanged?.Invoke());
        }
    }

    private void UpdateCursor(Point point)
    {
        if (Scene?.Camera is not CameraState camera || Bounds.Width <= 1 || Bounds.Height <= 1)
        {
            _lastHit = false;
            CursorMoved?.Invoke(0, 0, 0, false);
            return;
        }

        var plane = Scene.WorkPlane ?? new SketchPlane();
        CameraMath.TryRay(camera, Bounds.Width, Bounds.Height, point.X, point.Y, out _rayOrigin, out _rayDirection);
        _lastHit = CameraMath.TryHitSketchPlane(
            camera,
            Bounds.Width,
            Bounds.Height,
            point.X,
            point.Y,
            plane,
            out _lastU,
            out _lastV,
            out _lastWorld);
        CursorMoved?.Invoke(_lastWorld.X, _lastWorld.Y, _lastWorld.Z, _lastHit);
    }

    private void RaiseSketch(SketchPointerPhase phase)
    {
        var tolerance = 1d;
        if (Scene?.Camera is CameraState camera && Bounds.Height > 1)
        {
            tolerance = CameraMath.VisibleHalfHeight(camera) / Bounds.Height * 10;
        }

        SketchGesture?.Invoke(MakePointer(phase));
    }

    private SketchPointer MakePointer(SketchPointerPhase phase)
    {
        var tolerance = 1d;
        if (Scene?.Camera is CameraState camera && Bounds.Height > 1)
        {
            tolerance = CameraMath.VisibleHalfHeight(camera) / Bounds.Height * 10;
        }

        return new SketchPointer(
            phase,
            _lastU,
            _lastV,
            _lastHit,
            tolerance,
            _last.X,
            _last.Y,
            _shift,
            Math.Max(Bounds.Width, 1),
            Math.Max(Bounds.Height, 1),
            _rayOrigin,
            _rayDirection,
            _lastWorld.Z);
    }
}
