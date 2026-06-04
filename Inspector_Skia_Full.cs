using System;
using System.Linq;
using System.Reflection;

void ListTypes(string assemblyName)
{
    try
    {
        var assembly = Assembly.Load(assemblyName);
        Console.WriteLine($"Assembly: {assembly.FullName}");
        var types = assembly.GetTypes().Where(t => t.IsPublic && !t.IsAbstract);
        foreach (var type in types)
        {
            Console.WriteLine($"Type: {type.FullName}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error loading {assemblyName}: {ex.Message}");
    }
}

ListTypes("SkiaSharp.Views.WPF");
ListTypes("SkiaSharp.Views.Desktop.Common");
ListTypes("SkiaSharp");