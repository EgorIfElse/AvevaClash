using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aveva.ClashChecker.NetCallable.Models
{
    public class DepartmentInfo
    {
        public string Dept { get; set; }
        public string Tdept { get; set; }
        public string[] Mark { get; set; }


        public static readonly List<DepartmentInfo> Departments = [
            new() {
                Dept = "ARX",
                Tdept = "TEPRDARX",
                Mark = new[]{"_AC","_CE"}
            },
            new ()
            {
                Dept = "ASU",
                Tdept = "TEPRDASU",
                Mark = new[]{"_AS"}
            },
            new ()
            {
                Dept = "ETO",
                Tdept = "TEPRDETO",
                Mark = new[]{"_ED"}
            },
            new ()
            {
                Dept = "OGS",
                Tdept = "TEPRDOGS",
                Mark = new[]{"_HS"}
            },
            new ()
            {
                Dept = "OIV",
                Tdept = "TEPRDOIV",
                Mark = new[]{"_HV"}
            },
            new ()
            {
                Dept = "OMK",
                Tdept = "TEPRDOMK",
                Mark = new[]{"_SC"}
            },
            new ()
            {
                Dept = "OVS",
                Tdept = "TEPRDOVS",
                Mark = new[]{"_VS"}
            },
            new ()
            {
                Dept = "SOT",
                Tdept = "TEPRDSOT",
                Mark = new[]{"_RC"}
            },
            new()

            {
                Dept = "OWP",
                Tdept = "TEPRDOWP",
                Mark = new[]{"_WT"}
            },
            new(){
                Dept = "TMO",
                Tdept = "TEPRDTMO",
                Mark = new[]{"_TD","_WT"}
            },
            new()
            {
                Dept = "VIK",
                Tdept = "TEPRDVIK",
                Mark = new[]{"_WS","_FP"}
            }
            ];
    }
}
