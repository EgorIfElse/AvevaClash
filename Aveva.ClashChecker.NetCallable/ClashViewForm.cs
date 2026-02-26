using Aveva.Core.Database;
using Aveva.Core.Database.Filters;
using Aveva.Core.PMLNet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Aveva.ClashChecker.NetCallable.Exceptions;
using PML = Aveva.Core.Utilities.CommandLine.Command;
using TypeFilter = Aveva.Core.Database.Filters.TypeFilter;
using ClashChecker;
using Aveva.ClashChecker.NetCallable.Extensions;
namespace Aveva.ClashChecker.NetCallable
{
    public partial class ClashViewForm
    {

        [PMLNetCallable]
        public ClashViewForm()
        {
        }

    
        private DateTime GetLastProjectCheckDate()
        {
            var file = File.ReadAllLines("какой нибудь лог"); //lastcheck мы создаем из CheckAll
            return DateTime.Now;
        }
        private DateTime GetGpsetLastCheckTime(DbElement gpset)
        {
            return DateTime.Now;
        }

        private bool isGreenGpset(string GpsetRef)
        {
            bool retval = false;

            if (GpsetRef == "ALL" || GpsetRef == "CE") return false;

            var Gpset = DbElement.GetElement(GpsetRef);
            var User = Project.CurrentProject.LoginUser;
            var checker = ClashChecker();
            var now = DateTime.Now;

            if (Gpset.IsNull || !Gpset.IsValid ) return false;

            if (checker.GetDepartment(Gpset, "GPSET") != User && !(checker.GetDepartment(Gpset, "GPSET") == "SOT" && User == "OGS"))
            {
                PML.CreateCommand($"$p {GpsetRef} - это комплект другого отдела").RunInPdms();
                return false;
            }

            var gpsetlastmod = Gpset.EvaluateAsString(DbExpression.Parse("lastm"));

            return retval;
        }
    }

}
