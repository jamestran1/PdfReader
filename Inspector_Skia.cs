using System;
using System.Linq;
using System.Reflection;

try
{
    var assembly = Assembly.Load("SkiaSharp.Views.WPF");
    Console.WriteLine($"Assembly: {assembly.FullName}");
    var types = assembly.GetTypes().Where(t => t.IsPublic && !t.IsAbstract);
    foreach (var type in types)
    {
        Console.WriteLine($"Type: {type.FullName}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}