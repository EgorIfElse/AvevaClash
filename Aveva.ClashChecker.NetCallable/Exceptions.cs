using System;
namespace Aveva.ClashChecker.NetCallable;

public static class Exceptions
{
    //TODO: Придумать код ошибок для функций, вызывающих исключение (чтобы со стороны PML можно было понять, что функция завершила работу с ошибкой)
    /// <summary>
    /// Возвращает string сообщение для PML узла 
    /// </summary>
    public static string ConvertExceptionToPmlMessage(Exception ex, string msg = "") => $"ERROR:{ex.Message};STACKTRACE:{ex.StackTrace};MESSAGE:{msg}";
}
