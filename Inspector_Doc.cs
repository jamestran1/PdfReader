using System;
using System.Linq;
using System.Reflection;

try
{
    var assembly = Assembly.Load("PdfiumViewer");
    var type = assembly.GetType("PdfiumViewer.Core.PdfDocument");
    Console.WriteLine($"Type: {type.FullName}");
    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
    foreach (var method in methods)
    {
        Console.WriteLine($"Method: {method.Name}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}