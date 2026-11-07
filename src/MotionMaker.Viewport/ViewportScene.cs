using MotionMaker.Core;

namespace MotionMaker.Viewport;

public enum SceneColor
{
    Sketch,
    Selected,
    Preview,
    Error
}

public readonly record struct SceneSegment(double X1, double Y1, double X2, double Y2, SceneColor Color, double Z1 = 0.05, double Z2 = 0.05);

public readonly record struct SceneCircle(double X, double Y, double Radius, SceneColor Color);

public readonly record struct ScenePoint(double X, double Y, bool Selected, double Z = 0.08);

public sealed class ViewportScene
{
    public CameraState? Camera { get; set; }

    public bool HasContent { get; set; }

    public PlateSettings? Plate { get; set; }

    public bool ShowPlateGrid { get; set; }

    public bool ShowSketchGrid { get; set; }

    public double SketchMinorMm { get; set; } = 1;

    public double SketchMajorMm { get; set; } = 10;

    public List<SceneSegment> Segments { get; } = [];

    public List<SceneCircle> Circles { get; } = [];

    public List<ScenePoint> Points { get; } = [];

    public SceneCircle? PreviewCircle { get; set; }

    public MeshData? Solid { get; set; }

    public bool SolidFrozen { get; set; }

    public string? HighlightFaceId { get; set; }

    public SketchPlane WorkPlane { get; set; } = new();
}

public enum SketchPointerPhase
{
    Pressed,
    Moved,
    Released
}

public readonly record struct SketchPointer(
    SketchPointerPhase Phase,
    double X,
    double Y,
    bool HitPlane,
    double ToleranceMm,
    double ScreenX,
    double ScreenY,
    bool Shift,
    double ViewWidth,
    double ViewHeight,
    Vec3 RayOrigin,
    Vec3 RayDirection,
    double WorldZ);
