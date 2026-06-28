namespace TelemetryDash.ViewModels;

public class SessionItemViewModel
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;

    public override string ToString() => Label;
}
