namespace MotionMaker.Geometry;

public sealed class SolveResult
{
    private SolveResult(bool success, string? message)
    {
        Success = success;
        Message = message;
    }

    public bool Success { get; }

    public string? Message { get; }

    public static SolveResult Ok() => new(true, null);

    public static SolveResult Failed(string message) => new(false, message);
}
