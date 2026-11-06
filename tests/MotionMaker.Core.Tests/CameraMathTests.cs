using MotionMaker.Core;

namespace MotionMaker.Core.Tests;

public class CameraMathTests
{
    [Fact]
    public void DraggingRightTurnsTheSceneToTheRight()
    {
        var camera = CameraState.CreateAssemblyView(new PlateSettings());
        var world = new Vec3(0, 0, 0);
        Assert.True(CameraMath.TryProject(camera, 800, 600, world, out var before));

        CameraMath.Orbit(camera, 30, 0, 1);

        Assert.True(CameraMath.TryProject(camera, 800, 600, world, out var after));
        Assert.True(after.NdcX > before.NdcX);
    }

    [Fact]
    public void DraggingRightPansTheSceneToTheRight()
    {
        var camera = CameraState.CreateAssemblyView(new PlateSettings());
        var world = new Vec3(camera.TargetX, camera.TargetY, camera.TargetZ);
        Assert.True(CameraMath.TryProject(camera, 800, 600, world, out var before));

        CameraMath.Pan(camera, 40, 0, 600, 1);

        Assert.True(CameraMath.TryProject(camera, 800, 600, world, out var after));
        Assert.True(after.NdcX > before.NdcX);
    }

    [Fact]
    public void DraggingDownPansTheSceneDown()
    {
        var camera = CameraState.CreateAssemblyView(new PlateSettings());
        var world = new Vec3(camera.TargetX, camera.TargetY, camera.TargetZ);
        Assert.True(CameraMath.TryProject(camera, 800, 600, world, out var before));

        CameraMath.Pan(camera, 0, 40, 600, 1);

        Assert.True(CameraMath.TryProject(camera, 800, 600, world, out var after));
        Assert.True(after.NdcY < before.NdcY);
    }

    [Fact]
    public void OrbitKeepsTargetAndDistance()
    {
        var camera = Sample();
        var targetX = camera.TargetX;
        var targetY = camera.TargetY;
        var targetZ = camera.TargetZ;
        var distance = camera.Distance;

        CameraMath.Orbit(camera, 30, -12, 1);

        Assert.Equal(targetX, camera.TargetX);
        Assert.Equal(targetY, camera.TargetY);
        Assert.Equal(targetZ, camera.TargetZ);
        Assert.Equal(distance, camera.Distance);
        Assert.NotEqual(45, camera.YawDegrees);
        Assert.NotEqual(35, camera.PitchDegrees);
    }

    [Fact]
    public void PanKeepsDistanceAndAngles()
    {
        var camera = Sample();
        var distance = camera.Distance;
        var yaw = camera.YawDegrees;
        var pitch = camera.PitchDegrees;

        CameraMath.Pan(camera, 40, -15, 600, 1);

        Assert.Equal(distance, camera.Distance);
        Assert.Equal(yaw, camera.YawDegrees);
        Assert.Equal(pitch, camera.PitchDegrees);
        Assert.NotEqual(10, camera.TargetX);
    }

    [Fact]
    public void ZoomKeepsTargetAndMovesDistance()
    {
        var camera = Sample();
        var before = camera.Distance;

        CameraMath.Zoom(camera, 1, invert: false, sensitivity: 1);

        Assert.Equal(10, camera.TargetX);
        Assert.Equal(20, camera.TargetY);
        Assert.Equal(0, camera.TargetZ);
        Assert.True(camera.Distance < before);

        CameraMath.Zoom(camera, 1, invert: true, sensitivity: 1);
        Assert.True(camera.Distance > before * 0.5);
    }

    [Fact]
    public void CenterRayHitsTheTargetOnThePlatePlane()
    {
        var camera = Sample();
        CameraMath.Orbit(camera, 80, 25, 1);
        CameraMath.Pan(camera, 15, -20, 600, 1);
        CameraMath.Zoom(camera, 2, invert: false, 1);

        var hit = CameraMath.TryHitPlane(camera, 800, 600, 400, 300, camera.TargetZ, out var x, out var y);

        Assert.True(hit);
        Assert.Equal(camera.TargetX, x, 3);
        Assert.Equal(camera.TargetY, y, 3);
    }

    [Fact]
    public void FramedPlateCornersStayInsideTheView()
    {
        var camera = CameraState.CreateAssemblyView(new PlateSettings());
        const double width = 1600;
        const double height = 900;
        CameraMath.FrameBounds(camera, 0, 0, 0, 250, 210, 0, width, height);

        foreach (var corner in new[] { (0d, 0d), (250d, 0d), (0d, 210d), (250d, 210d) })
        {
            var projected = CameraMath.TryProject(camera, width, height, new Vec3(corner.Item1, corner.Item2, 0), out var point);
            Assert.True(projected);
            Assert.InRange(point.NdcX, -0.9, 0.9);
            Assert.InRange(point.NdcY, -0.9, 0.9);
        }
    }

    [Fact]
    public void TargetProjectsToTheCenter()
    {
        var camera = Sample();
        CameraMath.Orbit(camera, 40, -10, 1);
        var projected = CameraMath.TryProject(
            camera,
            800,
            600,
            new Vec3(camera.TargetX, camera.TargetY, camera.TargetZ),
            out var point);

        Assert.True(projected);
        Assert.Equal(0, point.NdcX, 6);
        Assert.Equal(0, point.NdcY, 6);
    }

    private static CameraState Sample() => new()
    {
        TargetX = 10,
        TargetY = 20,
        TargetZ = 0,
        Distance = 300,
        YawDegrees = 45,
        PitchDegrees = 35,
        Perspective = true
    };
}
