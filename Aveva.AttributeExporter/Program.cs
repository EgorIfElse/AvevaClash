using Aveva.Core.Database;
using Aveva.Core.Utilities.Messaging;
using System.Collections.Generic;

namespace AVEVA_AttributeExporter
{


    class Program
    {
        public static void Main(string[] args)
        {
            // Инициализация приложения AVEVA
            //Application.Initialize();

            //var exporter = new AttributeExporter();

            // Загружаем конфигурацию из Excel
            //exporter.Initialize(@"C:\Config\attributes_config.xlsx");

            // Экспортируем атрибуты
            //exporter.ExportAttributes(@"C:\Export\attributes.att");
            Aveva.E3D.Standalone.Standalone.Start(78);
            Aveva.E3D.Standalone.Standalone.Open("ARM", "SYSTEM", "XXXXXX", "ALL", out PdmsMessage error);
            DbElement dbWorld = MDB.CurrentMDB.GetFirstWorld(DbType.Design);
        }


        public static List<DbElement> TraverseElements(DbElement element)
        {
            List<DbElement> elements = new List<DbElement>();
            foreach (DbElement elem in element.Members())
            {
                elements.Add(elem);
                elements.AddRange(TraverseElements(element));
            }
            return elements;
        }


    }
}