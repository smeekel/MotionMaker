using System.Numerics;
using Avalonia.OpenGL;
using MotionMaker.Core;
using Silk.NET.OpenGL;

namespace MotionMaker.Viewport;

internal sealed class GlRenderer : IDisposable
{
    private GL? _gl;
    private uint _colorProgram;
    private uint _meshProgram;
    private uint _textureProgram;
    private uint _vao;
    private uint _vbo;
    private int _colorLocation;
    private readonly Dictionary<string, LabelTexture> _labels = [];

    public string? Error => null;

    public void Init(GlInterface glInterface)
    {
        _gl = GL.GetApi(name => glInterface.GetProcAddress(name));
        _colorProgram = CreateProgram(ColorVertex, ColorFragment);
        _meshProgram = CreateProgram(MeshVertex, MeshFragment);
        _textureProgram = CreateProgram(TextureVertex, TextureFragment);
        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _colorLocation = _gl.GetUniformLocation(_colorProgram, "uColor");
    }

    public void Render(
        GlInterface _,
        int framebuffer,
        int pixelWidth,
        int pixelHeight,
        double dipWidth,
        double dipHeight,
        SceneSnapshot scene)
    {
        var gl = _gl ?? throw new InvalidOperationException("OpenGL is not initialized.");
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)framebuffer);
        gl.Viewport(0, 0, (uint)Math.Max(pixelWidth, 1), (uint)Math.Max(pixelHeight, 1));
        gl.Enable(EnableCap.DepthTest);
        gl.DepthFunc(DepthFunction.Lequal);
        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        gl.ClearColor(0.15f, 0.16f, 0.18f, 1f);
        gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        if (!scene.HasContent || scene.Camera is null)
        {
            return;
        }

        var camera = scene.Camera;
        var width = Math.Max(dipWidth, 1);
        var height = Math.Max(dipHeight, 1);
        gl.Disable(EnableCap.DepthTest);
        DrawPlate(gl, camera, width, height, scene);
        DrawSketchGrid(gl, camera, width, height, scene);
        gl.Enable(EnableCap.DepthTest);
        DrawSolid(gl, camera, width, height, scene);
        gl.Disable(EnableCap.DepthTest);
        DrawSegments(gl, camera, width, height, scene);
        DrawCircles(gl, camera, width, height, scene.Circles);
        if (scene.PreviewCircle is SceneCircle preview)
        {
            DrawCircles(gl, camera, width, height, [preview]);
        }

        DrawPoints(gl, camera, width, height, scene);
        DrawScreenTriad(gl, camera);
    }

    public void DisposeGl()
    {
        var gl = _gl;
        if (gl is null)
        {
            return;
        }

        foreach (var label in _labels.Values)
        {
            gl.DeleteTexture(label.Texture);
        }

        _labels.Clear();
        if (_colorProgram != 0)
        {
            gl.DeleteProgram(_colorProgram);
        }

        if (_meshProgram != 0)
        {
            gl.DeleteProgram(_meshProgram);
        }

        if (_textureProgram != 0)
        {
            gl.DeleteProgram(_textureProgram);
        }

        if (_vao != 0)
        {
            gl.DeleteVertexArray(_vao);
        }

        if (_vbo != 0)
        {
            gl.DeleteBuffer(_vbo);
        }

        _gl = null;
    }

    public void Dispose() => DisposeGl();

    private void DrawPlate(GL gl, CameraState camera, double width, double height, SceneSnapshot scene)
    {
        var plate = scene.Plate;
        if (plate is null)
        {
            DrawWorldAxes(gl, camera, width, height, 25);
            return;
        }

        var x1 = 0d;
        var y1 = 0d;
        var x2 = plate.WidthMm;
        var y2 = plate.DepthMm;
        DrawPlateFill(gl, camera, width, height, x1, y1, x2, y2);
        if (scene.ShowPlateGrid)
        {
            var lines = new List<GridLine>();
            AddGrid(lines, x1, x2, y1, y2, plate.MinorSpacingMm, plate.MajorSpacingMm, vertical: true);
            AddGrid(lines, y1, y2, x1, x2, plate.MinorSpacingMm, plate.MajorSpacingMm, vertical: false);
            DrawGridLines(gl, camera, width, height, lines);
            DrawLabels(gl, camera, width, height, plate);
        }

        DrawLine(gl, camera, width, height, x1, y1, x2, y1, new Vector4(0.78f, 0.84f, 0.78f, 1));
        DrawLine(gl, camera, width, height, x2, y1, x2, y2, new Vector4(0.78f, 0.84f, 0.78f, 1));
        DrawLine(gl, camera, width, height, x2, y2, x1, y2, new Vector4(0.78f, 0.84f, 0.78f, 1));
        DrawLine(gl, camera, width, height, x1, y2, x1, y1, new Vector4(0.78f, 0.84f, 0.78f, 1));
        DrawWorldAxes(gl, camera, width, height, Math.Min(40, Math.Min(plate.WidthMm, plate.DepthMm) * 0.25));
    }

    private void DrawSketchGrid(GL gl, CameraState camera, double width, double height, SceneSnapshot scene)
    {
        if (!scene.ShowSketchGrid || scene.Plate is not null)
        {
            return;
        }

        if (!TryVisibleRect(camera, width, height, out var minX, out var minY, out var maxX, out var maxY))
        {
            var half = CameraMath.VisibleHalfHeight(camera) * 2;
            minX = camera.TargetX - half;
            maxX = camera.TargetX + half;
            minY = camera.TargetY - half;
            maxY = camera.TargetY + half;
        }

        var minor = Math.Max(scene.SketchMinorMm, 0.1);
        var span = Math.Max(maxX - minX, maxY - minY);
        while (span / minor > 240)
        {
            minor *= 10;
        }

        var major = Math.Max(scene.SketchMajorMm, minor * 10);
        while (major < minor * 5)
        {
            major *= 10;
        }

        var lines = new List<GridLine>();
        var startX = Math.Floor(minX / minor) * minor;
        var startY = Math.Floor(minY / minor) * minor;
        for (var x = startX; x <= maxX; x += minor)
        {
            lines.Add(new GridLine(x, minY, x, maxY, IsMultiple(x, major)));
        }

        for (var y = startY; y <= maxY; y += minor)
        {
            lines.Add(new GridLine(minX, y, maxX, y, IsMultiple(y, major)));
        }

        DrawGridLines(gl, camera, width, height, lines);
    }

    private void DrawSolid(GL gl, CameraState camera, double width, double height, SceneSnapshot scene)
    {
        if (scene.SolidPositions.Length < 9 || scene.SolidIndices.Length < 3)
        {
            return;
        }

        var vertices = new List<float>(scene.SolidIndices.Length * 6);
        var light = Vector3.Normalize(new Vector3(0.35f, 0.55f, 0.76f));
        for (var index = 0; index + 2 < scene.SolidIndices.Length; index += 3)
        {
            if (!TryProjected(camera, width, height, scene.SolidPositions, scene.SolidIndices[index], out var a, out var aw)
                || !TryProjected(camera, width, height, scene.SolidPositions, scene.SolidIndices[index + 1], out var b, out var bw)
                || !TryProjected(camera, width, height, scene.SolidPositions, scene.SolidIndices[index + 2], out var c, out var cw))
            {
                continue;
            }

            var normal = Vector3.Normalize(Vector3.Cross(bw - aw, cw - aw));
            if (float.IsNaN(normal.X))
            {
                normal = new Vector3(0, 0, 1);
            }

            var shade = Math.Clamp(0.32f + 0.68f * Math.Max(0, Vector3.Dot(normal, light)), 0, 1);
            var face = index / 3 < scene.SolidFaces.Length ? scene.SolidFaces[index / 3] : "";
            var highlighted = face.Length > 0 && face == scene.HighlightFaceId;
            var tint = scene.SolidFrozen
                ? new Vector3(0.72f, 0.28f, 0.32f)
                : highlighted ? new Vector3(0.86f, 0.62f, 0.28f) : new Vector3(0.58f, 0.66f, 0.74f);
            var color = tint * shade;
            AddMeshVertex(vertices, a, color);
            AddMeshVertex(vertices, b, color);
            AddMeshVertex(vertices, c, color);
        }

        if (vertices.Count < 18)
        {
            return;
        }

        gl.UseProgram(_meshProgram);
        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        gl.BufferData(BufferTargetARB.ArrayBuffer, vertices.ToArray(), BufferUsageARB.DynamicDraw);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 0);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 3 * sizeof(float));
        gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)(vertices.Count / 6));
        gl.DisableVertexAttribArray(1);

        if (!scene.SolidFrozen)
        {
            return;
        }

        gl.Disable(EnableCap.DepthTest);

        var edges = new List<float>();
        for (var index = 0; index + 2 < scene.SolidIndices.Length; index += 3)
        {
            AddEdge(edges, camera, width, height, scene.SolidPositions, scene.SolidIndices[index], scene.SolidIndices[index + 1]);
            AddEdge(edges, camera, width, height, scene.SolidPositions, scene.SolidIndices[index + 1], scene.SolidIndices[index + 2]);
            AddEdge(edges, camera, width, height, scene.SolidPositions, scene.SolidIndices[index + 2], scene.SolidIndices[index]);
        }

        DrawNdc(gl, edges, PrimitiveType.Lines, new Vector4(0.95f, 0.35f, 0.38f, 1));
    }

    private static void AddMeshVertex(List<float> vertices, Vector3 position, Vector3 color)
    {
        vertices.Add(position.X);
        vertices.Add(position.Y);
        vertices.Add(position.Z);
        vertices.Add(color.X);
        vertices.Add(color.Y);
        vertices.Add(color.Z);
    }

    private static bool TryProjected(
        CameraState camera,
        double width,
        double height,
        double[] positions,
        int index,
        out Vector3 ndc,
        out Vector3 world)
    {
        world = new Vector3(
            (float)positions[index * 3],
            (float)positions[index * 3 + 1],
            (float)positions[index * 3 + 2]);
        if (!CameraMath.TryProject(camera, width, height, new Vec3(world.X, world.Y, world.Z), out var projected))
        {
            ndc = default;
            return false;
        }

        var depth = (float)Math.Clamp(projected.Depth / Math.Max(camera.Distance * 8, 1), 0, 1);
        ndc = new Vector3((float)projected.NdcX, (float)projected.NdcY, -1f + 2f * depth);
        return true;
    }

    private static void AddEdge(List<float> vertices, CameraState camera, double width, double height, double[] positions, int a, int b)
    {
        if (TryProjected(camera, width, height, positions, a, out var pa, out _)
            && TryProjected(camera, width, height, positions, b, out var pb, out _))
        {
            vertices.Add(pa.X);
            vertices.Add(pa.Y);
            vertices.Add(pb.X);
            vertices.Add(pb.Y);
        }
    }

    private void DrawSegments(GL gl, CameraState camera, double width, double height, SceneSnapshot scene)
    {
        foreach (var color in new[] { SceneColor.Sketch, SceneColor.Preview, SceneColor.Selected, SceneColor.Error })
        {
            var vertices = new List<float>();
            foreach (var segment in scene.Segments)
            {
                if (segment.Color != color)
                {
                    continue;
                }

                AddProjected(vertices, camera, width, height, segment.X1, segment.Y1, segment.Z1, segment.X2, segment.Y2, segment.Z2);
            }

            DrawNdc(gl, vertices, PrimitiveType.Lines, ColorOf(color));
        }
    }

    private void DrawCircles(GL gl, CameraState camera, double width, double height, IReadOnlyList<SceneCircle> circles)
    {
        foreach (var color in new[] { SceneColor.Sketch, SceneColor.Preview, SceneColor.Selected, SceneColor.Error })
        {
            var vertices = new List<float>();
            foreach (var circle in circles)
            {
                if (circle.Color != color)
                {
                    continue;
                }

                const int steps = 72;
                for (var step = 0; step < steps; step++)
                {
                    var a0 = step * Math.PI * 2 / steps;
                    var a1 = (step + 1) * Math.PI * 2 / steps;
                    AddProjected(
                        vertices,
                        camera,
                        width,
                        height,
                        circle.X + Math.Cos(a0) * circle.Radius,
                        circle.Y + Math.Sin(a0) * circle.Radius,
                        0.05,
                        circle.X + Math.Cos(a1) * circle.Radius,
                        circle.Y + Math.Sin(a1) * circle.Radius,
                        0.05);
                }
            }

            DrawNdc(gl, vertices, PrimitiveType.Lines, ColorOf(color));
        }
    }

    private void DrawPoints(GL gl, CameraState camera, double width, double height, SceneSnapshot scene)
    {
        var half = CameraMath.VisibleHalfHeight(camera) / height * 7;
        foreach (var selected in new[] { false, true })
        {
            var vertices = new List<float>();
            foreach (var point in scene.Points)
            {
                if (point.Selected != selected)
                {
                    continue;
                }

                var basis = CameraMath.Basis(camera);
                var right = basis.Right * half;
                var up = basis.Up * half;
                AddProjected(
                    vertices,
                    camera,
                    width,
                    height,
                    point.X - right.X,
                    point.Y - right.Y,
                    point.Z - right.Z,
                    point.X + right.X,
                    point.Y + right.Y,
                    point.Z + right.Z);
                AddProjected(
                    vertices,
                    camera,
                    width,
                    height,
                    point.X - up.X,
                    point.Y - up.Y,
                    point.Z - up.Z,
                    point.X + up.X,
                    point.Y + up.Y,
                    point.Z + up.Z);
            }

            DrawNdc(gl, vertices, PrimitiveType.Lines, selected ? new Vector4(1f, 0.62f, 0.15f, 1f) : new Vector4(0.95f, 0.97f, 1f, 1f));
        }
    }

    private void DrawWorldAxes(GL gl, CameraState camera, double width, double height, double length)
    {
        length = Math.Max(length, 1);
        DrawLine(gl, camera, width, height, 0, 0, length, 0, new Vector4(0.92f, 0.28f, 0.24f, 1), 0, 0, 0.1);
        DrawLine(gl, camera, width, height, 0, 0, 0, length, new Vector4(0.30f, 0.78f, 0.36f, 1), 0, 0, 0.1);
        DrawLine3(gl, camera, width, height, 0, 0, 0, 0, 0, length, new Vector4(0.32f, 0.52f, 0.95f, 1));
    }

    private void DrawScreenTriad(GL gl, CameraState camera)
    {
        var basis = CameraMath.Basis(camera);
        var originX = -0.86f;
        var originY = -0.78f;
        DrawAxis(gl, basis, originX, originY, new Vec3(1, 0, 0), new Vector4(0.92f, 0.28f, 0.24f, 1));
        DrawAxis(gl, basis, originX, originY, new Vec3(0, 1, 0), new Vector4(0.30f, 0.78f, 0.36f, 1));
        DrawAxis(gl, basis, originX, originY, new Vec3(0, 0, 1), new Vector4(0.32f, 0.52f, 0.95f, 1));
    }

    private void DrawAxis(GL gl, CameraBasis basis, float originX, float originY, Vec3 direction, Vector4 color)
    {
        var dx = (float)Vec3.Dot(direction, basis.Right) * 0.09f;
        var dy = (float)Vec3.Dot(direction, basis.Up) * 0.09f;
        DrawNdc(gl, [originX, originY, originX + dx, originY + dy], PrimitiveType.Lines, color);
    }

    private void DrawLabels(GL gl, CameraState camera, double width, double height, PlateSettings plate)
    {
        var major = plate.MajorSpacingMm;
        if (major <= 0)
        {
            return;
        }

        for (var x = 0d; x <= plate.WidthMm + 1e-6; x += major)
        {
            DrawLabel(gl, camera, width, height, Format(x), x, -1.2, 0, true);
        }

        for (var y = 0d; y <= plate.DepthMm + 1e-6; y += major)
        {
            DrawLabel(gl, camera, width, height, Format(y), -1.2, y, 0, false);
        }
    }

    private void DrawLabel(
        GL gl,
        CameraState camera,
        double width,
        double height,
        string text,
        double anchorX,
        double anchorY,
        double z,
        bool alongX)
    {
        var label = GetLabel(gl, text);
        var worldHeight = 4.5;
        var worldWidth = worldHeight * label.Width / Math.Max(label.Height, 1);
        double x0;
        double y0;
        if (alongX)
        {
            x0 = anchorX - worldWidth / 2;
            y0 = anchorY - worldHeight;
        }
        else
        {
            x0 = anchorX - worldWidth;
            y0 = anchorY - worldHeight / 2;
        }

        Span<float> vertices = stackalloc float[24];
        if (!TryQuad(vertices, camera, width, height, x0, y0, x0 + worldWidth, y0 + worldHeight, z))
        {
            return;
        }

        gl.UseProgram(_textureProgram);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, label.Texture);
        gl.Uniform1(_gl!.GetUniformLocation(_textureProgram, "uTex"), 0);
        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        gl.BufferData(BufferTargetARB.ArrayBuffer, vertices, BufferUsageARB.DynamicDraw);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
        gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        gl.DisableVertexAttribArray(1);
    }

    private LabelTexture GetLabel(GL gl, string text)
    {
        if (_labels.TryGetValue(text, out var existing))
        {
            return existing;
        }

        var image = LabelImage.Draw(text);
        var texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, texture);
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        unsafe
        {
            fixed (byte* pixels = image.Pixels)
            {
                gl.TexImage2D(
                    TextureTarget.Texture2D,
                    0,
                    InternalFormat.Rgba,
                    (uint)image.Width,
                    (uint)image.Height,
                    0,
                    PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    pixels);
            }
        }

        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        var created = new LabelTexture(texture, image.Width, image.Height);
        _labels[text] = created;
        return created;
    }

    private void DrawGridLines(GL gl, CameraState camera, double width, double height, List<GridLine> lines)
    {
        var minor = new List<float>();
        var major = new List<float>();
        foreach (var line in lines)
        {
            AddProjected(line.Major ? major : minor, camera, width, height, line.X1, line.Y1, 0, line.X2, line.Y2, 0);
        }

        DrawNdc(gl, minor, PrimitiveType.Lines, new Vector4(0.36f, 0.40f, 0.39f, 1));
        DrawNdc(gl, major, PrimitiveType.Lines, new Vector4(0.62f, 0.70f, 0.63f, 1));
    }

    private void DrawLine(
        GL gl,
        CameraState camera,
        double width,
        double height,
        double x1,
        double y1,
        double x2,
        double y2,
        Vector4 color,
        double z1 = 0,
        double yz = 0,
        double z2 = 0)
    {
        _ = yz;
        var vertices = new List<float>();
        AddProjected(vertices, camera, width, height, x1, y1, z1, x2, y2, z2);
        DrawNdc(gl, vertices, PrimitiveType.Lines, color);
    }

    private void DrawLine3(
        GL gl,
        CameraState camera,
        double width,
        double height,
        double x1,
        double y1,
        double z1,
        double x2,
        double y2,
        double z2,
        Vector4 color)
    {
        var vertices = new List<float>();
        AddProjected(vertices, camera, width, height, x1, y1, z1, x2, y2, z2);
        DrawNdc(gl, vertices, PrimitiveType.Lines, color);
    }

    private void DrawPlateFill(GL gl, CameraState camera, double width, double height, double x1, double y1, double x2, double y2)
    {
        const double z = -0.02;
        if (!TryPoint(camera, width, height, x1, y1, z, out var p00)
            || !TryPoint(camera, width, height, x2, y1, z, out var p10)
            || !TryPoint(camera, width, height, x2, y2, z, out var p11)
            || !TryPoint(camera, width, height, x1, y2, z, out var p01))
        {
            return;
        }

        DrawNdc(
            gl,
            [p00.X, p00.Y, p10.X, p10.Y, p11.X, p11.Y, p00.X, p00.Y, p11.X, p11.Y, p01.X, p01.Y],
            PrimitiveType.Triangles,
            new Vector4(0.23f, 0.25f, 0.27f, 1f));
    }

    private void DrawNdc(GL gl, List<float> vertices, PrimitiveType primitive, Vector4 color)
    {
        if (vertices.Count < 2)
        {
            return;
        }

        gl.UseProgram(_colorProgram);
        gl.Uniform4(_colorLocation, color);
        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        gl.BufferData(BufferTargetARB.ArrayBuffer, vertices.ToArray(), BufferUsageARB.DynamicDraw);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
        gl.DrawArrays(primitive, 0, (uint)(vertices.Count / 2));
    }

    private static bool TryPoint(
        CameraState camera,
        double width,
        double height,
        double x,
        double y,
        double z,
        out (float X, float Y) point)
    {
        if (!CameraMath.TryProject(camera, width, height, new Vec3(x, y, z), out var projected))
        {
            point = default;
            return false;
        }

        point = ((float)projected.NdcX, (float)projected.NdcY);
        return true;
    }

    private static void AddProjected(
        List<float> vertices,
        CameraState camera,
        double width,
        double height,
        double x1,
        double y1,
        double z1,
        double x2,
        double y2,
        double z2)
    {
        if (!CameraMath.TryProject(camera, width, height, new Vec3(x1, y1, z1), out var a)
            || !CameraMath.TryProject(camera, width, height, new Vec3(x2, y2, z2), out var b))
        {
            return;
        }

        vertices.Add((float)a.NdcX);
        vertices.Add((float)a.NdcY);
        vertices.Add((float)b.NdcX);
        vertices.Add((float)b.NdcY);
    }

    private static bool TryQuad(
        Span<float> vertices,
        CameraState camera,
        double width,
        double height,
        double x0,
        double y0,
        double x1,
        double y1,
        double z)
    {
        if (!CameraMath.TryProject(camera, width, height, new Vec3(x0, y0, z), out var p00)
            || !CameraMath.TryProject(camera, width, height, new Vec3(x1, y0, z), out var p10)
            || !CameraMath.TryProject(camera, width, height, new Vec3(x1, y1, z), out var p11)
            || !CameraMath.TryProject(camera, width, height, new Vec3(x0, y1, z), out var p01))
        {
            return false;
        }

        Write(vertices, 0, p00, 0, 0);
        Write(vertices, 4, p10, 1, 0);
        Write(vertices, 8, p11, 1, 1);
        Write(vertices, 12, p00, 0, 0);
        Write(vertices, 16, p11, 1, 1);
        Write(vertices, 20, p01, 0, 1);
        return true;
    }

    private static void Write(Span<float> vertices, int index, ProjectedPoint point, float u, float v)
    {
        vertices[index] = (float)point.NdcX;
        vertices[index + 1] = (float)point.NdcY;
        vertices[index + 2] = u;
        vertices[index + 3] = v;
    }

    private uint CreateProgram(string vertexSource, string fragmentSource)
    {
        var gl = _gl!;
        var vertex = Compile(ShaderType.VertexShader, vertexSource);
        var fragment = Compile(ShaderType.FragmentShader, fragmentSource);
        var program = gl.CreateProgram();
        gl.AttachShader(program, vertex);
        gl.AttachShader(program, fragment);
        gl.LinkProgram(program);
        gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out var status);
        if (status == 0)
        {
            var log = gl.GetProgramInfoLog(program);
            throw new InvalidOperationException($"OpenGL link failed: {log}");
        }

        gl.DeleteShader(vertex);
        gl.DeleteShader(fragment);
        return program;
    }

    private uint Compile(ShaderType type, string source)
    {
        var gl = _gl!;
        var shader = gl.CreateShader(type);
        gl.ShaderSource(shader, source);
        gl.CompileShader(shader);
        gl.GetShader(shader, ShaderParameterName.CompileStatus, out var status);
        if (status == 0)
        {
            var log = gl.GetShaderInfoLog(shader);
            throw new InvalidOperationException($"OpenGL shader failed: {log}");
        }

        return shader;
    }

    private static void AddGrid(
        List<GridLine> lines,
        double start,
        double end,
        double crossStart,
        double crossEnd,
        double minor,
        double major,
        bool vertical)
    {
        if (minor <= 0)
        {
            return;
        }

        var first = Math.Ceiling(start / minor) * minor;
        for (var value = first; value <= end + 1e-6; value += minor)
        {
            var majorLine = IsMultiple(value, major);
            if (vertical)
            {
                lines.Add(new GridLine(value, crossStart, value, crossEnd, majorLine));
            }
            else
            {
                lines.Add(new GridLine(crossStart, value, crossEnd, value, majorLine));
            }
        }
    }

    private static bool TryVisibleRect(
        CameraState camera,
        double width,
        double height,
        out double minX,
        out double minY,
        out double maxX,
        out double maxY)
    {
        minX = minY = maxX = maxY = 0;
        var hits = new List<(double X, double Y)>();
        foreach (var corner in new[] { (0d, 0d), (width, 0d), (0d, height), (width, height) })
        {
            if (!CameraMath.TryHitPlane(camera, width, height, corner.Item1, corner.Item2, 0, out var x, out var y))
            {
                return false;
            }

            hits.Add((x, y));
        }

        minX = hits.Min(hit => hit.X);
        maxX = hits.Max(hit => hit.X);
        minY = hits.Min(hit => hit.Y);
        maxY = hits.Max(hit => hit.Y);
        return maxX - minX < 20000 && maxY - minY < 20000 && maxX > minX && maxY > minY;
    }

    private static bool IsMultiple(double value, double spacing)
        => spacing > 0 && Math.Abs(value / spacing - Math.Round(value / spacing)) < 1e-4;

    private static string Format(double value) => Math.Abs(value) < 1e-6 ? "0" : value.ToString("0.##");

    private static Vector4 ColorOf(SceneColor color) => color switch
    {
        SceneColor.Selected => new Vector4(1f, 0.62f, 0.15f, 1f),
        SceneColor.Preview => new Vector4(1f, 0.84f, 0.28f, 1f),
        SceneColor.Error => new Vector4(0.95f, 0.35f, 0.38f, 1f),
        _ => new Vector4(0.90f, 0.95f, 1f, 1f)
    };

    private readonly record struct GridLine(double X1, double Y1, double X2, double Y2, bool Major);

    private readonly record struct LabelTexture(uint Texture, int Width, int Height);

    private const string MeshVertex = """
        #version 330 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aColor;
        out vec3 vColor;
        void main() {
            gl_Position = vec4(aPos, 1.0);
            vColor = aColor;
        }
        """;

    private const string MeshFragment = """
        #version 330 core
        in vec3 vColor;
        out vec4 FragColor;
        void main() { FragColor = vec4(vColor, 1.0); }
        """;

    private const string ColorVertex = """
        #version 330 core
        layout(location = 0) in vec2 aPos;
        void main() { gl_Position = vec4(aPos, 0.0, 1.0); }
        """;

    private const string ColorFragment = """
        #version 330 core
        uniform vec4 uColor;
        out vec4 FragColor;
        void main() { FragColor = uColor; }
        """;

    private const string TextureVertex = """
        #version 330 core
        layout(location = 0) in vec2 aPos;
        layout(location = 1) in vec2 aUv;
        out vec2 vUv;
        void main() {
            gl_Position = vec4(aPos, 0.0, 1.0);
            vUv = aUv;
        }
        """;

    private const string TextureFragment = """
        #version 330 core
        in vec2 vUv;
        uniform sampler2D uTex;
        out vec4 FragColor;
        void main() { FragColor = texture(uTex, vUv); }
        """;
}

internal sealed class SceneSnapshot
{
    public bool HasContent { get; init; }

    public CameraState? Camera { get; init; }

    public PlateSettings? Plate { get; init; }

    public bool ShowPlateGrid { get; init; }

    public bool ShowSketchGrid { get; init; }

    public double SketchMinorMm { get; init; }

    public double SketchMajorMm { get; init; }

    public SceneSegment[] Segments { get; init; } = [];

    public SceneCircle[] Circles { get; init; } = [];

    public ScenePoint[] Points { get; init; } = [];

    public SceneCircle? PreviewCircle { get; init; }

    public double[] SolidPositions { get; init; } = [];

    public int[] SolidIndices { get; init; } = [];

    public string[] SolidFaces { get; init; } = [];

    public bool SolidFrozen { get; init; }

    public string? HighlightFaceId { get; init; }

    public static SceneSnapshot From(ViewportScene? scene)
    {
        if (scene is null)
        {
            return new SceneSnapshot();
        }

        PlateSettings? plate = null;
        if (scene.Plate is not null)
        {
            plate = new PlateSettings
            {
                WidthMm = scene.Plate.WidthMm,
                DepthMm = scene.Plate.DepthMm,
                MinorSpacingMm = scene.Plate.MinorSpacingMm,
                MajorSpacingMm = scene.Plate.MajorSpacingMm
            };
        }

        return new SceneSnapshot
        {
            HasContent = scene.HasContent,
            Camera = scene.Camera?.Clone(),
            Plate = plate,
            ShowPlateGrid = scene.ShowPlateGrid,
            ShowSketchGrid = scene.ShowSketchGrid,
            SketchMinorMm = scene.SketchMinorMm,
            SketchMajorMm = scene.SketchMajorMm,
            Segments = scene.Segments.ToArray(),
            Circles = scene.Circles.ToArray(),
            Points = scene.Points.ToArray(),
            PreviewCircle = scene.PreviewCircle,
            SolidPositions = scene.Solid?.Positions.ToArray() ?? [],
            SolidIndices = scene.Solid?.Indices.ToArray() ?? [],
            SolidFaces = scene.Solid?.TriangleFaces.ToArray() ?? [],
            SolidFrozen = scene.SolidFrozen,
            HighlightFaceId = scene.HighlightFaceId
        };
    }
}
