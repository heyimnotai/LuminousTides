/// Global switch that makes gameplay ignore real keyboard and mouse input.
/// The scripted smoke test turns it on so stray key presses cannot change its results.
public static class InputGate
{
    public static bool Blocked;
}
