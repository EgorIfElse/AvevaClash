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
            return GetElementOrOwnerByType(dbElement, DbElementTypeInstance.SITE);
        }

        public static DbElement GetZone(this DbElement dbElement)
        {
            return GetElementOrOwnerByType(dbElement, DbElementTypeInstance.ZONE);
        }

        public static DbElement GetPipe(this DbElement dbElement)
        {
            return GetElementOrOwnerByType(dbElement, DbElementTypeInstance.PIPE);
        }

        public static DbElement GetGpwl(this DbElement dbElement)
        {
            DbElement gpwld = GetElementOrOwnerByType(dbElement, DbElementTypeInstance.GPWLD);

            if (gpwld != null && !gpwld.IsNull && gpwld.IsValid)
                return gpwld;

            return GetElementOrOwnerByType(dbElement, DbElementTypeInstance.SYGPWL);
        }

        private static DbElement GetElementOrOwnerByType(DbElement dbElement, DbElementType elementType)
        {
            if (dbElement == null || dbElement.IsNull || !dbElement.IsValid)
                return DbElement.GetElement("*");

            if (dbElement.ElementType == elementType)
                return dbElement;

            return dbElement.GetOwnerByType(elementType);
        }
    }
}
