namespace PerfMonitor.Hardware;

public sealed class EmaSmoother
{
    private readonly float _alpha;
    private float? _value;

    public EmaSmoother(float alpha) => _alpha = alpha;

    public float Smooth(float sample)
    {
        _value = _value is null ? sample : _alpha * sample + (1 - _alpha) * _value;
        return _value.Value;
    }
}
