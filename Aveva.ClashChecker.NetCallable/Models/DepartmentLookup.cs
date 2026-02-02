using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aveva.ClashChecker.NetCallable.Models
{
    public static class DepartmentLookup
    {
        public static Dictionary<string, string> MarkToDept;
            static DepartmentLookup()
        {
            MarkToDept = new Dictionary<string, string>();

            foreach (var dep in DepartmentInfo.Departments)
            {
                foreach (var m in dep.Mark)
                {
                    MarkToDept[m] = dep.Dept;
                }
            }

        }

    }
}
