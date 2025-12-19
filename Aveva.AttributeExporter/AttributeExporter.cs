//using System;
//using System.Collections.Generic;
//using System.IO;
////using Aveva.E3D.DesignAPI;
////using Aveva.E3D.DesignAPI.Attributes;
////using OfficeOpenXml;

//namespace AVEVA_AttributeExporter;
//{
//    public class AttributeExporter
//    {
//        private Dictionary<string, string> attributeExpressions;
//        private List<string> collectedData;

//        public void Initialize(string excelConfigPath)
//        {
//            // Загружаем конфигурацию из Excel
//            LoadAttributeConfig(excelConfigPath);
//            collectedData = new List<string>();
//        }

//        private void LoadAttributeConfig(string filePath)
//        {
//            attributeExpressions = new Dictionary<string, string>();

//            using (var package = new ExcelPackage(new FileInfo(filePath)))
//            {
//                var worksheet = package.Workbook.Worksheets[0];
//                int rowCount = worksheet.Dimension.Rows;

//                for (int row = 2; row <= rowCount; row++) // Пропускаем заголовок
//                {
//                    string attributeName = worksheet.Cells[row, 1].Text;
//                    string expression = worksheet.Cells[row, 2].Text;

//                    if (!string.IsNullOrEmpty(attributeName))
//                    {
//                        attributeExpressions[attributeName] = expression;
//                    }
//                }
//            }
//        }

//        public void ExportAttributes(string outputFilePath)
//        {
//            try
//            {
//                // Получаем корневой элемент (World)
//                var world = Application.Instance.World;

//                // Рекурсивный обход
//                TraverseElement(world);

//                // Запись в файл
//                File.WriteAllLines(outputFilePath, collectedData);

//                Console.WriteLine($"Экспорт завершен. Экспортировано {collectedData.Count} записей.");
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"Ошибка: {ex.Message}");
//            }
//        }

//        private void TraverseElement(Element element)
//        {
//            if (element == null) return;

//            // Проверяем атрибуты текущего элемента
//            ProcessElementAttributes(element);

//            // Рекурсивно обходим дочерние элементы
//            foreach (var child in element.Children)
//            {
//                TraverseElement(child);
//            }
//        }

//        private void ProcessElementAttributes(Element element)
//        {
//            foreach (var attrConfig in attributeExpressions)
//            {
//                string attributeName = attrConfig.Key;
//                string expression = attrConfig.Value;

//                // Проверяем наличие атрибута у элемента
//                if (element.HasAttribute(attributeName))
//                {
//                    object attributeValue = element.GetAttributeValue(attributeName);

//                    // Применяем выражение PML, если оно задано
//                    if (!string.IsNullOrEmpty(expression))
//                    {
//                        attributeValue = ApplyPMLExpression(attributeValue, expression);
//                    }

//                    // Формируем строку для записи
//                    string record = $"{element.FullName}|{attributeName}|{attributeValue}";
//                    collectedData.Add(record);
//                }
//            }
//        }

//        private object ApplyPMLExpression(object value, string expression)
//        {
//            // Здесь должна быть интеграция с PML движком
//            // Упрощенный пример - базовые операции

//            try
//            {
//                // Заменяем $value в выражении на фактическое значение
//                string processedExpression = expression.Replace("$value", value.ToString());

//                // Простейший пример обработки выражений
//                // В реальном приложении нужен полноценный PML интерпретатор
//                if (processedExpression.Contains("*"))
//                {
//                    var parts = processedExpression.Split('*');
//                    if (double.TryParse(parts[0], out double num1) &&
//                        double.TryParse(parts[1], out double num2))
//                    {
//                        return num1 * num2;
//                    }
//                }

//                return value; // Если не удалось применить выражение
//            }
//            catch
//            {
//                return value;
//            }
//        }
//    }

//}