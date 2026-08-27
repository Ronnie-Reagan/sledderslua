namespace AssemblyAuditFixture;

public sealed class CurrentGameType
{
    public object? character;

    public static CurrentGameType Instance { get; } = new CurrentGameType();

    public float GetFuel() => 0.5f;

    public void SetFuel(float value)
    {
    }
}
