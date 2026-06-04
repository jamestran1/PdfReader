using System;
using System.Linq;
using System.Reflection;

try
{
    var assembly = Assembly.Load("PdfiumViewer");
    var type = assembly.GetType("PdfiumViewer.Core.PdfPage");
    var method = type.GetMethod("GetCountChars");
    Console.WriteLine($"Method: {method?.Name}, Returns: {method?.ReturnType.Name}");

    method = type.GetMethod("GetCharIndexAtPos");
    Console.WriteLine($"Method: {method?.Name}");
    foreach (var param in method?.GetParameters() ?? Array.Empty<ParameterInfo>())
    {
        Console.WriteLine($"  Param: {param.Name} ({param.ParameterType.Name})");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}