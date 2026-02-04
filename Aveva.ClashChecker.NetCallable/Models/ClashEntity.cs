using System;

namespace Aveva.ClashChecker.NetCallable.Models;

public record ClashEntity
{
    public static string Flnm2 { get; set; } = "";
    public static string Flnm1 { get; set; } = "";
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
    public DateTime Date { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public bool Existing { get; set; }
    public string RequestToDept { get; set; } = "";
    public string RequestUser { get; set; } = "";
    public DateTime RequestDate { get; set; }
    public string ApproveUser { get; set; } = "";
    public DateTime ApproveDate { get; set; }
    public string ApproveReason { get; set; } = "";
    public string InWorkUser { get; set; } = "";
    public DateTime InWorkDate { get; set; }

}


