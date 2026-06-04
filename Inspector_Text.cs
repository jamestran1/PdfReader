using System;
using System.Linq;
using System.Reflection;

try
{
    var assembly = Assembly.Load("PdfiumViewer");
    var type = assembly.GetType("PdfiumViewer.Core.PdfPage");
    var methods = type.GetMethods().Where(m => m.Name == "GetTextBounds");
    foreach (var method in methods)
    {
        Console.WriteLine($"Method: {method.Name}");
        foreach (var param in method.GetParameters())
        {
            Console.WriteLine($"  Param: {param.Name} ({param.ParameterType.Name})");
        }
        Console.WriteLine($"  Returns: {method.ReturnType.Name}");
    }

    methods = type.GetMethods().Where(m => m.Name == "GetText");
    foreach (var method in methods)
    {
        Console.WriteLine($"Method: {method.Name}");
        foreach (var param in method.GetParameters())
        {
            Console.WriteLine($"  Param: {param.Name} ({param.ParameterType.Name})");
        }
        Console.WriteLine($"  Returns: {method.ReturnType.Name}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}