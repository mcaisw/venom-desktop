namespace VenomDesktop;

public sealed class AudioSnapshot
{
    public float[] Bands { get; init; } = [];
    public double Rms { get; init; }
    public double Peak { get; init; }
    public double Bass { get; init; }
    public double Mid { get; init; }
    public double Air { get; init; }
    public double Impact { get; init; }
    public bool HasSignal { get; init; }
}
