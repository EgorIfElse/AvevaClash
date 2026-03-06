using Aveva.Core.Database;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aveva.ClashChecker.NetCallable.Extensions
{
    public static class DbDepthElementExtensions
    {
        public static DbElement GetOwnerByDepth(this DbElement dbElement, int DbDepth)
        {
            int ElDepth = dbElement.GetInteger(DbAttribute.GetDbAttribute("DbDepth")); //5

            if (DbDepth < 0 || !dbElement.IsValid) return null;

            while (ElDepth > DbDepth)
            {
                dbElement = dbElement.Owner;
                ElDepth--;

            }
            return ElDepth == DbDepth ? dbElement : null;
        }

        public static DbElement GetSite(this DbElement dbElement)
        {
            return dbElement.GetOwnerByDepth(1);
        }

        public static DbElement GetZone(this DbElement dbElement)
        {
            return dbElement.GetOwnerByDepth(2);
        }

        public static DbElement GetPipe(this DbElement dbElement)
        {
            return dbElement.GetOwnerByDepth(3);
        }

        public static DbElement GetGpwl(this DbElement dbElement)
        {
            return dbElement.GetOwnerByDepth(4);
        }
    }
}
