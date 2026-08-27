using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: SleddersAssemblyAudit <Assembly-CSharp.dll> [contract.json]");
    return 2;
}

string assemblyPath = Path.GetFullPath(args[0]);
if (!File.Exists(assemblyPath))
{
    Console.Error.WriteLine($"Assembly not found: {assemblyPath}");
    return 2;
}

string? contractPath = args.Length >= 2 ? Path.GetFullPath(args[1]) : null;
if (contractPath != null && !File.Exists(contractPath))
{
    Console.Error.WriteLine($"Contract not found: {contractPath}");
    return 2;
}

byte[] sha;
using (FileStream hashStream = File.OpenRead(assemblyPath))
    sha = SHA256.HashData(hashStream);

using FileStream stream = File.OpenRead(assemblyPath);
using var pe = new PEReader(stream);
if (!pe.HasMetadata)
{
    Console.Error.WriteLine("Input file does not contain managed metadata.");
    return 2;
}

MetadataReader reader = pe.GetMetadataReader();
ModuleDefinition module = reader.GetModuleDefinition();
Guid mvid = reader.GetGuid(module.Mvid);

var types = new Dictionary<string, TypeSnapshot>(StringComparer.Ordinal);
foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
{
    TypeDefinition type = reader.GetTypeDefinition(handle);
    string name = reader.GetString(type.Name);
    string ns = reader.GetString(type.Namespace);
    if (name == "<Module>")
        continue;

    string fullName = string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    var fields = new HashSet<string>(StringComparer.Ordinal);
    foreach (FieldDefinitionHandle fieldHandle in type.GetFields())
        fields.Add(reader.GetString(reader.GetFieldDefinition(fieldHandle).Name));

    var methods = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
    foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
    {
        MethodDefinition method = reader.GetMethodDefinition(methodHandle);
        string methodName = reader.GetString(method.Name);
        int parameterCount = 0;
        foreach (ParameterHandle parameterHandle in method.GetParameters())
        {
            Parameter parameter = reader.GetParameter(parameterHandle);
            if (parameter.SequenceNumber > 0)
                parameterCount++;
        }

        if (!methods.TryGetValue(methodName, out HashSet<int>? counts))
        {
            counts = new HashSet<int>();
            methods[methodName] = counts;
        }
        counts.Add(parameterCount);
    }

    types[fullName] = new TypeSnapshot(fields, methods);
}

Contract? contract = null;
if (contractPath != null)
{
    contract = JsonSerializer.Deserialize<Contract>(
        File.ReadAllText(contractPath),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidDataException("Contract JSON was empty.");
}

var checks = new List<CheckResult>();
if (contract != null)
{
    foreach (TypeContract expectedType in contract.Types)
    {
        if (!types.TryGetValue(expectedType.Name, out TypeSnapshot? actual))
        {
            checks.Add(new CheckResult("type", expectedType.Name, false, "missing type"));
            continue;
        }

        checks.Add(new CheckResult("type", expectedType.Name, true, null));

        foreach (string field in expectedType.Fields)
        {
            bool found = actual.Fields.Contains(field);
            checks.Add(new CheckResult(
                "field",
                expectedType.Name + "." + field,
                found,
                found ? null : "missing field"));
        }

        foreach (MethodContract method in expectedType.Methods)
        {
            bool found = actual.Methods.TryGetValue(method.Name, out HashSet<int>? counts) &&
                         counts.Contains(method.ParameterCount);
            checks.Add(new CheckResult(
                "method",
                $"{expectedType.Name}.{method.Name}/{method.ParameterCount}",
                found,
                found ? null : "missing method or parameter-count mismatch"));
        }
    }
}

var output = new
{
    assembly = new
    {
        path = assemblyPath,
        sha256 = Convert.ToHexString(sha).ToLowerInvariant(),
        mvid,
        typeCount = reader.TypeDefinitions.Count,
        methodCount = reader.MethodDefinitions.Count,
        fieldCount = reader.FieldDefinitions.Count
    },
    contract = contractPath,
    summary = new
    {
        total = checks.Count,
        passed = checks.Count(x => x.Passed),
        failed = checks.Count(x => !x.Passed)
    },
    checks
};

Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
return checks.Any(x => !x.Passed) ? 1 : 0;

internal sealed record TypeSnapshot(
    HashSet<string> Fields,
    Dictionary<string, HashSet<int>> Methods);

internal sealed class Contract
{
    public List<TypeContract> Types { get; set; } = new();
}

internal sealed class TypeContract
{
    public string Name { get; set; } = string.Empty;
    public List<string> Fields { get; set; } = new();
    public List<MethodContract> Methods { get; set; } = new();
}

internal sealed class MethodContract
{
    public string Name { get; set; } = string.Empty;
    public int ParameterCount { get; set; }
}

internal sealed record CheckResult(string Kind, string Member, bool Passed, string? Detail);
