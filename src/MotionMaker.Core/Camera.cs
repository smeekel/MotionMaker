namespace MotionMaker.Core;

public readonly record struct Vec3(double X, double Y, double Z)
{
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Vec3 operator *(Vec3 a, double scale) => new(a.X * scale, a.Y * scale, a.Z * scale);

    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

    public Vec3 Normalized()
    {
        var length = Length;
        return length < 1e-12 ? new Vec3(0, 0, 0) : this * (1.0 / length);
    }

    public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vec3 Cross(Vec3 a, Vec3 b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);
}

public enum ViewPreset
{
    Top,
    Front,
    Right,
    Iso
}

public sealed class CameraState
{
    public double TargetX { get; set; }

    public double TargetY { get; set; }

    public double TargetZ { get; set; }

    public double Distance { get; set; } = 400;

    public double YawDegrees { get; set; } = 45;

    public double PitchDegrees { get; set; } = 35;

    public bool Perspective { get; set; } = true;

    public double FieldOfViewDegrees { get; set; } = 45;

    public CameraState Clone() => new()
    {
        TargetX = TargetX,
        TargetY = TargetY,
        TargetZ = TargetZ,
        Distance = Distance,
        YawDegrees = YawDegrees,
        PitchDegrees = PitchDegrees,
        Perspective = Perspective,
        FieldOfViewDegrees = FieldOfViewDegrees
    };

    public static CameraState CreateAssemblyView(PlateSettings plate)
    {
        var camera = new CameraState
        {
            YawDegrees = 45,
            PitchDegrees = 35,
            Perspective = true
        };
        CameraMath.FrameBounds(camera, 0, 0, 0, plate.WidthMm, plate.DepthMm, 0, 16, 9);
        return camera;
    }

    public static CameraState CreatePartView()
    {
        var camera = new CameraState
        {
            YawDegrees = -90,
            PitchDegrees = CameraMath.MaxPitchDegrees,
            Perspective = false,
            TargetX = 0,
            TargetY = 0,
            TargetZ = 0
        };
        CameraMath.FrameBounds(camera, -50, -50, 0, 50, 50, 0, 16, 9);
        return camera;
    }
}

public readonly record struct CameraBasis(Vec3 Eye, Vec3 Forward, Vec3 Right, Vec3 Up);

public readonly record struct ProjectedPoint(double NdcX, double NdcY, double Depth);

public static class CameraMath
{
    public const double MinPitchDegrees = -89;
    public const double MaxPitchDegrees = 89;
    public const double MinDistance = 1;
    public const double MaxDistance = 100_000;
    private const double FrameFill = 0.85;

    public static CameraBasis Basis(CameraState camera)
    {
        var yaw = camera.YawDegrees * Math.PI / 180.0;
        var pitch = camera.PitchDegrees * Math.PI / 180.0;
        var cosPitch = Math.Cos(pitch);
        var sinPitch = Math.Sin(pitch);
        var cosYaw = Math.Cos(yaw);
        var sinYaw = Math.Sin(yaw);
        var offset = new Vec3(cosPitch * cosYaw, cosPitch * sinYaw, sinPitch);
        var target = new Vec3(camera.TargetX, camera.TargetY, camera.TargetZ);
        var eye = target + offset * camera.Distance;
        var forward = (target - eye).Normalized();
        var right = Vec3.Cross(forward, new Vec3(0, 0, 1));
        if (right.Length < 1e-6)
        {
            right = new Vec3(sinYaw, -cosYaw, 0);
            if (right.Length < 1e-8)
            {
                right = new Vec3(1, 0, 0);
            }
        }

        right = right.Normalized();
        var up = Vec3.Cross(right, forward).Normalized();
        return new CameraBasis(eye, forward, right, up);
    }

    public static double VisibleHalfHeight(CameraState camera)
        => camera.Distance * Math.Tan(camera.FieldOfViewDegrees * Math.PI / 360.0);

    public static void Orbit(CameraState camera, double deltaXPixels, double deltaYPixels, double sensitivity)
    {
        camera.YawDegrees -= deltaXPixels * 0.4 * sensitivity;
        camera.PitchDegrees = Math.Clamp(
            camera.PitchDegrees - deltaYPixels * 0.4 * sensitivity,
            MinPitchDegrees,
            MaxPitchDegrees);
    }

    public static void Pan(
        CameraState camera,
        double deltaXPixels,
        double deltaYPixels,
        double viewportHeightPixels,
        double sensitivity)
    {
        var basis = Basis(camera);
        var scale = (2 * VisibleHalfHeight(camera) / Math.Max(viewportHeightPixels, 1)) * sensitivity;
        camera.TargetX += basis.Right.X * deltaXPixels * scale - basis.Up.X * deltaYPixels * scale;
        camera.TargetY += basis.Right.Y * deltaXPixels * scale - basis.Up.Y * deltaYPixels * scale;
        camera.TargetZ += basis.Right.Z * deltaXPixels * scale - basis.Up.Z * deltaYPixels * scale;
    }

    public static void Zoom(CameraState camera, double wheelNotches, bool invert, double sensitivity)
    {
        var direction = invert ? -1 : 1;
        camera.Distance *= Math.Exp(-wheelNotches * 0.12 * sensitivity * direction);
        camera.Distance = Math.Clamp(camera.Distance, MinDistance, MaxDistance);
    }

    public static void SetPreset(CameraState camera, ViewPreset preset)
    {
        switch (preset)
        {
            case ViewPreset.Top:
                camera.YawDegrees = -90;
                camera.PitchDegrees = MaxPitchDegrees;
                break;
            case ViewPreset.Front:
                camera.YawDegrees = -90;
                camera.PitchDegrees = 0;
                break;
            case ViewPreset.Right:
                camera.YawDegrees = 0;
                camera.PitchDegrees = 0;
                break;
            case ViewPreset.Iso:
                camera.YawDegrees = 45;
                camera.PitchDegrees = 35;
                camera.Perspective = true;
                break;
        }
    }

    public static void FrameBounds(
        CameraState camera,
        double minX,
        double minY,
        double minZ,
        double maxX,
        double maxY,
        double maxZ,
        double viewportWidth,
        double viewportHeight)
    {
        camera.TargetX = (minX + maxX) * 0.5;
        camera.TargetY = (minY + maxY) * 0.5;
        camera.TargetZ = (minZ + maxZ) * 0.5;
        var basis = Basis(camera);
        var aspect = Math.Max(viewportWidth, 1) / Math.Max(viewportHeight, 1);
        var tanHalf = Math.Tan(camera.FieldOfViewDegrees * Math.PI / 360.0);
        var required = MinDistance;
        var center = new Vec3(camera.TargetX, camera.TargetY, camera.TargetZ);
        foreach (var corner in Corners(minX, minY, minZ, maxX, maxY, maxZ))
        {
            var offset = corner - center;
            var camX = Math.Abs(Vec3.Dot(offset, basis.Right));
            var camY = Math.Abs(Vec3.Dot(offset, basis.Up));
            var along = Vec3.Dot(offset, basis.Forward);
            var half = Math.Max(camY, camX / aspect);
            var needed = camera.Perspective
                ? half / (FrameFill * tanHalf) - along
                : half / (FrameFill * tanHalf);
            required = Math.Max(required, needed);
            required = Math.Max(required, 0.2 - along);
        }

        camera.Distance = Math.Clamp(required, MinDistance, MaxDistance);
    }

    public static bool TryProject(
        CameraState camera,
        double viewportWidth,
        double viewportHeight,
        Vec3 world,
        out ProjectedPoint projected)
    {
        var basis = Basis(camera);
        var offset = world - basis.Eye;
        var camX = Vec3.Dot(offset, basis.Right);
        var camY = Vec3.Dot(offset, basis.Up);
        var depth = Vec3.Dot(offset, basis.Forward);
        var aspect = Math.Max(viewportWidth, 1) / Math.Max(viewportHeight, 1);
        var tanHalf = Math.Tan(camera.FieldOfViewDegrees * Math.PI / 360.0);
        var halfHeight = camera.Perspective ? depth * tanHalf : VisibleHalfHeight(camera);
        if (halfHeight < 1e-8 || (camera.Perspective && depth < 0.05))
        {
            projected = default;
            return false;
        }

        projected = new ProjectedPoint(camX / (halfHeight * aspect), camY / halfHeight, depth);
        return true;
    }

    public static bool TryHitPlane(
        CameraState camera,
        double viewportWidth,
        double viewportHeight,
        double pixelX,
        double pixelY,
        double planeZ,
        out double hitX,
        out double hitY)
    {
        var ndcX = (pixelX / Math.Max(viewportWidth, 1)) * 2 - 1;
        var ndcY = 1 - (pixelY / Math.Max(viewportHeight, 1)) * 2;
        var basis = Basis(camera);
        var tanHalf = Math.Tan(camera.FieldOfViewDegrees * Math.PI / 360.0);
        var aspect = Math.Max(viewportWidth, 1) / Math.Max(viewportHeight, 1);
        Vec3 origin;
        Vec3 direction;
        if (camera.Perspective)
        {
            origin = basis.Eye;
            direction = (basis.Forward
                + basis.Right * (ndcX * tanHalf * aspect)
                + basis.Up * (ndcY * tanHalf)).Normalized();
        }
        else
        {
            var halfHeight = VisibleHalfHeight(camera);
            origin = basis.Eye
                + basis.Right * (ndcX * halfHeight * aspect)
                + basis.Up * (ndcY * halfHeight);
            direction = basis.Forward;
        }

        if (Math.Abs(direction.Z) < 1e-9)
        {
            hitX = 0;
            hitY = 0;
            return false;
        }

        var t = (planeZ - origin.Z) / direction.Z;
        if (camera.Perspective && t < 0)
        {
            hitX = 0;
            hitY = 0;
            return false;
        }

        hitX = origin.X + direction.X * t;
        hitY = origin.Y + direction.Y * t;
        return true;
    }

    private static IEnumerable<Vec3> Corners(
        double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
    {
        yield return new Vec3(minX, minY, minZ);
        yield return new Vec3(maxX, minY, minZ);
        yield return new Vec3(minX, maxY, minZ);
        yield return new Vec3(maxX, maxY, minZ);
        yield return new Vec3(minX, minY, maxZ);
        yield return new Vec3(maxX, minY, maxZ);
        yield return new Vec3(minX, maxY, maxZ);
        yield return new Vec3(maxX, maxY, maxZ);
    }
}
