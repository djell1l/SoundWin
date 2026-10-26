using System;
using System.Linq;
using System.Reflection;
using SoundCloudExplode;

namespace SoundWin.Core.Services;

public static class SoundCloudApiInspector
{
    public static void Inspect()
    {
        var type = typeof(SoundCloudClient);
        Console.WriteLine($"Type: {type.FullName}");

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            Console.WriteLine($"Property: {prop.PropertyType.Name} {prop.Name}");
            foreach (var m in prop.PropertyType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var parameters = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                Console.WriteLine($"   Method: {m.ReturnType.Name} {m.Name}({parameters})");
            }
        }
    }
}
