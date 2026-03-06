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
     $"Id '{nameof(ClashEntity.Id)}', " +
     $"ClashType '{nameof(ClashEntity.ClashType)}', " +
     $"El1 '{nameof(ClashEntity.FirstElement)}', " +
     $"Type1 '{nameof(ClashEntity.FirstType)}', " +
     $"Usermod1 '{nameof(ClashEntity.FirstUserMode)}', " +
     $"Flnm1 '{nameof(ClashEntity.Flnm1)}', " +
     $"Dept1 '{nameof(ClashEntity.FirstDept)}', " +
     $"Gpset1 '{nameof(ClashEntity.FirstGpset)}', " +
     $"El2 '{nameof(ClashEntity.SecondElement)}', " +
     $"Type2 '{nameof(ClashEntity.SecondType)}', " +
     $"Usermod2 '{nameof(ClashEntity.SecondUserMode)}', " +
     $"Flnm2 '{nameof(ClashEntity.Flnm2)}', " +
     $"Dept2 '{nameof(ClashEntity.SecondDept)}', " +
     $"Gpset2 '{nameof(ClashEntity.SecondGpset)}', " +
     $"Date '{nameof(ClashEntity.Date)}', " +
     $"X '{nameof(ClashEntity.X)}', " +
     $"Y '{nameof(ClashEntity.Y)}', " +
     $"Z '{nameof(ClashEntity.Z)}', " +
     $"Existing '{nameof(ClashEntity.Existing)}', " +
     $"Sequence '{nameof(ClashEntity.Sequence)}', " +
     $"Building '{nameof(ClashEntity.Building)}', " +
     $"RequestToDept '{nameof(ClashEntity.RequestToDept)}', " +
     $"RequestUser '{nameof(ClashEntity.RequestUser)}', " +
     $"RequestDate '{nameof(ClashEntity.RequestDate)}', " +
     $"ApproveUser '{nameof(ClashEntity.ApproveUser)}', " +
     $"ApproveDate '{nameof(ClashEntity.ApproveDate)}', " +
     $"ApproveReason '{nameof(ClashEntity.ApproveReason)}', " +
     $"InWorkUser '{nameof(ClashEntity.InWorkUser)}', " +
     $"InWorkDate '{nameof(ClashEntity.InWorkDate)}'";
}
