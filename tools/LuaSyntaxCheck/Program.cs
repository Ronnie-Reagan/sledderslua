using MoonSharp.Interpreter;

if (args.Length == 0)
{
    Console.Error.WriteLine("Pass one or more files/directories containing Lua sources.");
    return 2;
}

var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
foreach (string arg in args)
{
    if (File.Exists(arg) && arg.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) files.Add(Path.GetFullPath(arg));
    else if (Directory.Exists(arg))
        foreach (string file in Directory.EnumerateFiles(arg, "*.lua", SearchOption.AllDirectories)) files.Add(Path.GetFullPath(file));
}

int failures = 0;
foreach (string file in files)
{
    try
    {
        var script = new Script(CoreModules.Preset_SoftSandbox);
        script.LoadString(File.ReadAllText(file), codeFriendlyName: file);
        Console.WriteLine("OK  " + file);
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine("BAD " + file);
        Console.Error.WriteLine(ex.Message);
    }
}
return failures == 0 ? 0 : 1;
