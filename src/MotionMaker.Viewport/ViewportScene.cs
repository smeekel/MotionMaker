using MotionMaker.Core;

namespace MotionMaker.Viewport;

public enum SceneColor
{
    Sketch,
    Selected,
    Preview
}

public readonly record struct SceneSegment(double X1, double Y1, double X2, double Y2, SceneColor Color);

public readonly record struct SceneCircle(double X, double Y, double Radius, SceneColor Color);

public readonly record struct ScenePoint(double X, double Y, bool Selected);

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
}

public enum SketchPointerPhase
{
    Pressed,
    Moved,
    Released
}

public readonly record struct SketchPointer(SketchPointerPhase Phase, double X, double Y, bool HitPlane, double ToleranceMm);
