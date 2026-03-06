using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aveva.ClashChecker.NetCallable.Models
{
    public static class DateSubstracts
    {
        public static double DateSubstract(double d1, double m1, double y1, double d2, double m2, double y2)
        {
            return new DateTime(Convert.ToInt16(y1), Convert.ToInt16(m1), Convert.ToInt16(d1)).Subtract(new DateTime(Convert.ToInt16(y2), Convert.ToInt16(m2), Convert.ToInt16(d2))).Days;
        }

        public static string DateAdd(double d1, double m1, double y1, double day)
        {
            DateTime dateTime = new DateTime(Convert.ToInt16(y1), Convert.ToInt16(m1), Convert.ToInt16(d1)).Add(new TimeSpan(Convert.ToInt16(day), 0, 0, 0));
            return Convert.ToString(dateTime.Day) + " " + Convert.ToString(dateTime.Month) + " " + Convert.ToString(dateTime.Year);
        }
    }
}
