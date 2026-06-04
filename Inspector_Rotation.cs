using System;
using System.Linq;
using System.Reflection;

try
{
    var assembly = Assembly.Load("PdfiumViewer");
    var types = assembly.GetTypes().Where(t => t.Name == "PdfRotation");
    foreach (var type in types)
    {
        Console.WriteLine($"Type: {type.FullName}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}