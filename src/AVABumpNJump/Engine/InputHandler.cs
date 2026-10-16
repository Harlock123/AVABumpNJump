using Avalonia.Input;

namespace AVABumpNJump.Engine;

/// <summary>
/// Keyboard and gamepad boiled down to the controls: steering, the pedals (faster and slower), and the jump.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly ControllerManager _pad = new(0);

    public void OnKeyDown(Key key)
    {
        _held.Remove(key);
        _held.Add(key);
    }

    public void OnKeyUp(Key key) => _held.Remove(key);
    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update() => _pad.Update();

    public Controls Controls
    {
        get
        {
            int steer = (Any(Key.Right, Key.D, Key.NumPad6) ? 1 : 0) - (Any(Key.Left, Key.A, Key.NumPad4) ? 1 : 0);
            if (steer == 0) steer = _pad.DPadLeft || _pad.LeftStickX < -0.4f ? -1 : _pad.DPadRight || _pad.LeftStickX > 0.4f ? 1 : 0;
            int pedal = (Any(Key.Up, Key.W, Key.NumPad8) ? 1 : 0) - (Any(Key.Down, Key.S, Key.NumPad2) ? 1 : 0);
            if (pedal == 0) pedal = _pad.DPadUp || _pad.LeftStickY < -0.4f || _pad.RightTrigger > 0.5f ? 1 : _pad.DPadDown || _pad.LeftStickY > 0.4f || _pad.LeftTrigger > 0.5f ? -1 : 0;
            bool jump = Any(Key.Space, Key.Z, Key.X, Key.LeftCtrl, Key.RightCtrl) || _pad.ButtonA || _pad.ButtonB;
            return new Controls(steer, pedal, jump);
        }
    }

    public bool Start => Any(Key.Enter, Key.D1, Key.NumPad1) || _pad.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}
