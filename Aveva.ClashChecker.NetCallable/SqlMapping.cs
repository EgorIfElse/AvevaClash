using Aveva.ClashChecker.NetCallable.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aveva.ClashChecker.NetCallable.Sql;

public static class SqlMapping
{
    public static readonly string ClashSql =
      $"ID '{nameof(ClashEntity.Id)}', " +
    $"GL '{nameof(ClashEntity.Building)}', " +
    $"CT '{nameof(ClashEntity.ClashType)}', " +
    $"R1 '{nameof(ClashEntity.FirstElement)}', " +
    $"E1 '{nameof(ClashEntity.FirstType)}', " +
    $"U1 '{nameof(ClashEntity.FirstUserMode)}', " +
    //$"Flnm1 '{nameof(ClashEntity.Flnm1)}', " +
    $"D1 '{nameof(ClashEntity.FirstDept)}', " +
    $"G1 '{nameof(ClashEntity.FirstGpset)}', " +
    $"R2 '{nameof(ClashEntity.SecondElement)}', " +
    $"E2 '{nameof(ClashEntity.SecondType)}', " +
    $"U2 '{nameof(ClashEntity.SecondUserMode)}', " +
    //$"Flnm2 '{nameof(ClashEntity.Flnm2)}', " +
    $"XT '{nameof(ClashEntity.Existing)}', " +
    $"D2 '{nameof(ClashEntity.SecondDept)}', " +
    $"G2 '{nameof(ClashEntity.SecondGpset)}', " +
    $"DT '{nameof(ClashEntity.Date)}', " +
    $"X0 '{nameof(ClashEntity.X)}', " +
    $"Y0 '{nameof(ClashEntity.Y)}', " +
    $"Z0 '{nameof(ClashEntity.Z)}', " +
    
    //$"Sequence '{nameof(ClashEntity.Sequence)}', " +
    //$"Building '{nameof(ClashEntity.Building)}', " +
    $"RT '{nameof(ClashEntity.RequestToDept)}', " +
    $"RU '{nameof(ClashEntity.RequestUser)}', " +
    $"RD '{nameof(ClashEntity.RequestDate)}', " +
    $"AU '{nameof(ClashEntity.ApproveUser)}', " +
    $"AD '{nameof(ClashEntity.ApproveDate)}', " +
    $"AR '{nameof(ClashEntity.ApproveReason)}', " +
    $"WU '{nameof(ClashEntity.InWorkUser)}', " +
    $"WD '{nameof(ClashEntity.InWorkDate)}'";

     
}
