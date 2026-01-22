namespace Aveva.ClashChecker.NetCallable.Models;

public record ClashEntity
{
    public int Id { get; set; }
    public string ClashType { get; set; } = "";
    public string FirstElement { get; set; } = "";
    public string FirstType { get; set; } = "";
    public string FirstUserMode { get; set; } = "";
    public string FirstDept { get; set; } = "";
    public string FirstGpset { get; set; } = "";
    public string SecondElement { get; set; } = "";
    public string SecondType { get; set; } = "";
    public string SecondUserMode { get; set; } = "";
    public string SecondDept { get; set; } = "";
    public string SecondGpset { get; set; } = "";
}
