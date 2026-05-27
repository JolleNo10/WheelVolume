namespace WheelVolume;

internal sealed class WheelDeltaAccumulator
{
    private const int WheelDelta = 120;

    private int _remainder;

    public int AddDelta(short delta)
    {
        _remainder += delta;
        int steps = _remainder / WheelDelta;
        _remainder %= WheelDelta;

        return steps;
    }

    public void Reset()
    {
        _remainder = 0;
    }
}
