using System;
using System.Linq;
using System.Reflection;

try
{
    var assembly = Assembly.Load("PdfiumViewer");
    var type = assembly.GetType("PdfiumViewer.Core.PdfPage");
    var methods = type.GetMethods().Where(m => m.Name == "Render");
    foreach (var method in methods)
    {
        Console.WriteLine($"Method: {method.Name}, Returns: {method.ReturnType.Name}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}