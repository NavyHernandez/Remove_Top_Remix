using System;
using System.Linq;
using System.Reflection;

var asm = Assembly.GetAssembly(typeof(FluentSystemIcons.WinUI.FluentIcon));
if (asm == null) { Console.WriteLine("asm null"); return; }
Console.WriteLine("ASM: " + asm.FullName);
foreach (var t in asm.GetExportedTypes().OrderBy(t => t.FullName))
{
    Console.WriteLine("TYPE: " + t.FullName);
    var fs = t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
    foreach (var f in fs.Take(6))
        Console.WriteLine("  FIELD " + f.FieldType.Name + " " + f.Name);
    var ps = t.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
    foreach (var p in ps.Take(6))
        Console.WriteLine("  PROP " + p.PropertyType.Name + " " + p.Name);
    if (fs.Length + ps.Length > 6) Console.WriteLine("  ... mas miembros: " + (fs.Length + ps.Length));
}
