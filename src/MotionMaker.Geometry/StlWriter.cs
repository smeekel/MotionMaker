using System.Text;
using MotionMaker.Core;

namespace MotionMaker.Geometry;

public static class StlWriter
{
    public static void Write(MeshData mesh, Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(new byte[80]);
        var count = mesh.Indices.Count / 3;
        writer.Write((uint)count);
        for (var index = 0; index + 2 < mesh.Indices.Count; index += 3)
        {
            var a = MeshQuery.At(mesh, mesh.Indices[index]);
            var b = MeshQuery.At(mesh, mesh.Indices[index + 1]);
            var c = MeshQuery.At(mesh, mesh.Indices[index + 2]);
            var normal = Vec3.Cross(b - a, c - a).Normalized();
            Write(writer, normal);
            Write(writer, a);
            Write(writer, b);
            Write(writer, c);
            writer.Write((ushort)0);
        }
    }

    public static int ReadTriangleCount(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        stream.Position = 80;
        return (int)reader.ReadUInt32();
    }

    private static void Write(BinaryWriter writer, Vec3 value)
    {
        writer.Write((float)value.X);
        writer.Write((float)value.Y);
        writer.Write((float)value.Z);
    }
}
