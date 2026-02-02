using Aveva.Core.Database;
using System.Runtime.CompilerServices;

namespace Aveva.ClashChecker.NetCallable.Extensions;

public static class DbElementExtensoins
{
    public static DbElement GetOwnerByType(this DbElement element, string typeName)
    {
        DbElementType parsedType = DbElementType.GetElementType(typeName);
        if (!parsedType.IsValid)
            return DbElement.GetElement("");
        return GetOwnerByType(element, parsedType);
    }

    public static DbElement GetOwnerByType(this DbElement element, DbElementType type)
    {
        if(element.Owner.ElementType == type)
            return element.Owner;
        if (element.Owner.ElementType == DbElementTypeInstance.WORLD)
            return DbElement.GetElement("");
        return GetOwnerByType(element.Owner, type);
    }

    public static bool TryGetOwnerByType(this DbElement element, DbElementType type, out DbElement owner)
    {
        owner = GetOwnerByType(element, type);
        return !owner.IsNull;
    }

}
