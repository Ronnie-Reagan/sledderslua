using System.Collections.Immutable;
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
var signatureProvider = new SignatureNameProvider();

var types = new Dictionary<string, TypeSnapshot>(StringComparer.Ordinal);
foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
{
    TypeDefinition type = reader.GetTypeDefinition(handle);
    string name = reader.GetString(type.Name);
    string ns = reader.GetString(type.Namespace);
    if (name == "<Module>")
        continue;

    string fullName = string.IsNullOrEmpty(ns) ? name : ns + "." + name;

    var fields = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (FieldDefinitionHandle fieldHandle in type.GetFields())
    {
        FieldDefinition field = reader.GetFieldDefinition(fieldHandle);
        string fieldName = reader.GetString(field.Name);
        fields[fieldName] = field.DecodeSignature(signatureProvider, genericContext: null);
    }

    var methods = new Dictionary<string, List<MethodSnapshot>>(StringComparer.Ordinal);
    foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
    {
        MethodDefinition method = reader.GetMethodDefinition(methodHandle);
        string methodName = reader.GetString(method.Name);
        MethodSignature<string> signature = method.DecodeSignature(signatureProvider, genericContext: null);
        var snapshot = new MethodSnapshot(
            signature.ParameterTypes.Length,
            signature.ReturnType,
            signature.ParameterTypes.ToArray());

        if (!methods.TryGetValue(methodName, out List<MethodSnapshot>? overloads))
        {
            overloads = new List<MethodSnapshot>();
            methods[methodName] = overloads;
        }
        overloads.Add(snapshot);
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
            bool found = actual.Fields.ContainsKey(field);
            checks.Add(new CheckResult(
                "field",
                expectedType.Name + "." + field,
                found,
                found ? null : "missing field"));
        }

        foreach ((string fieldName, string expectedFieldType) in expectedType.FieldTypes)
        {
            bool found = actual.Fields.TryGetValue(fieldName, out string? actualFieldType);
            bool matched = found && string.Equals(actualFieldType, expectedFieldType, StringComparison.Ordinal);
            checks.Add(new CheckResult(
                "field-signature",
                expectedType.Name + "." + fieldName,
                matched,
                matched
                    ? null
                    : found
                        ? $"expected {expectedFieldType}, found {actualFieldType}"
                        : "missing field"));
        }

        foreach (MethodContract method in expectedType.Methods)
        {
            actual.Methods.TryGetValue(method.Name, out List<MethodSnapshot>? overloads);
            overloads ??= new List<MethodSnapshot>();

            MethodSnapshot? matchingCount = overloads.FirstOrDefault(x => x.ParameterCount == method.ParameterCount);
            bool found = overloads.Any(candidate => MethodMatches(candidate, method));

            string detail;
            if (found)
            {
                detail = string.Empty;
            }
            else if (overloads.Count == 0)
            {
                detail = "missing method";
            }
            else if (matchingCount == null)
            {
                detail = "parameter-count mismatch; found " +
                    string.Join(", ", overloads.Select(FormatMethodSignature));
            }
            else
            {
                detail = "signature mismatch; found " +
                    string.Join(", ", overloads.Select(FormatMethodSignature));
            }

            checks.Add(new CheckResult(
                "method",
                $"{expectedType.Name}.{method.Name}/{method.ParameterCount}",
                found,
                found ? null : detail));
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

static bool MethodMatches(MethodSnapshot candidate, MethodContract expected)
{
    if (candidate.ParameterCount != expected.ParameterCount)
        return false;

    if (!string.IsNullOrWhiteSpace(expected.ReturnType) &&
        !string.Equals(candidate.ReturnType, expected.ReturnType, StringComparison.Ordinal))
        return false;

    if (expected.ParameterTypes.Count > 0 &&
        !candidate.ParameterTypes.SequenceEqual(expected.ParameterTypes, StringComparer.Ordinal))
        return false;

    return true;
}

static string FormatMethodSignature(MethodSnapshot method)
{
    return $"{method.ReturnType} ({string.Join(", ", method.ParameterTypes)})";
}

internal sealed record TypeSnapshot(
    Dictionary<string, string> Fields,
    Dictionary<string, List<MethodSnapshot>> Methods);

internal sealed record MethodSnapshot(
    int ParameterCount,
    string ReturnType,
    string[] ParameterTypes);

internal sealed class Contract
{
    public List<TypeContract> Types { get; set; } = new();
}

internal sealed class TypeContract
{
    public string Name { get; set; } = string.Empty;
    public List<string> Fields { get; set; } = new();
    public Dictionary<string, string> FieldTypes { get; set; } = new(StringComparer.Ordinal);
    public List<MethodContract> Methods { get; set; } = new();
}

internal sealed class MethodContract
{
    public string Name { get; set; } = string.Empty;
    public int ParameterCount { get; set; }
    public string? ReturnType { get; set; }
    public List<string> ParameterTypes { get; set; } = new();
}

internal sealed record CheckResult(string Kind, string Member, bool Passed, string? Detail);

internal sealed class SignatureNameProvider : ISignatureTypeProvider<string, object?>
{
    public string GetArrayType(string elementType, ArrayShape shape)
    {
        string commas = shape.Rank <= 1 ? string.Empty : new string(',', shape.Rank - 1);
        return elementType + "[" + commas + "]";
    }

    public string GetByReferenceType(string elementType) => elementType + "&";

    public string GetFunctionPointerType(MethodSignature<string> signature)
    {
        return "fnptr(" + Format(signature.ReturnType, signature.ParameterTypes) + ")";
    }

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
    {
        return genericType + "<" + string.Join(", ", typeArguments) + ">";
    }

    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;

    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;

    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
    {
        return unmodifiedType;
    }

    public string GetPinnedType(string elementType) => elementType;

    public string GetPointerType(string elementType) => elementType + "*";

    public string GetPrimitiveType(PrimitiveTypeCode typeCode)
    {
        return typeCode switch
        {
            PrimitiveTypeCode.Boolean => "System.Boolean",
            PrimitiveTypeCode.Byte => "System.Byte",
            PrimitiveTypeCode.Char => "System.Char",
            PrimitiveTypeCode.Double => "System.Double",
            PrimitiveTypeCode.Int16 => "System.Int16",
            PrimitiveTypeCode.Int32 => "System.Int32",
            PrimitiveTypeCode.Int64 => "System.Int64",
            PrimitiveTypeCode.IntPtr => "System.IntPtr",
            PrimitiveTypeCode.Object => "System.Object",
            PrimitiveTypeCode.SByte => "System.SByte",
            PrimitiveTypeCode.Single => "System.Single",
            PrimitiveTypeCode.String => "System.String",
            PrimitiveTypeCode.TypedReference => "System.TypedReference",
            PrimitiveTypeCode.UInt16 => "System.UInt16",
            PrimitiveTypeCode.UInt32 => "System.UInt32",
            PrimitiveTypeCode.UInt64 => "System.UInt64",
            PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
            PrimitiveTypeCode.Void => "System.Void",
            _ => typeCode.ToString()
        };
    }

    public string GetSZArrayType(string elementType) => elementType + "[]";

    public string GetTypeFromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        byte rawTypeKind)
    {
        TypeDefinition definition = reader.GetTypeDefinition(handle);
        return FullName(reader.GetString(definition.Namespace), reader.GetString(definition.Name));
    }

    public string GetTypeFromReference(
        MetadataReader reader,
        TypeReferenceHandle handle,
        byte rawTypeKind)
    {
        TypeReference reference = reader.GetTypeReference(handle);
        return FullName(reader.GetString(reference.Namespace), reader.GetString(reference.Name));
    }

    public string GetTypeFromSpecification(
        MetadataReader reader,
        object? genericContext,
        TypeSpecificationHandle handle,
        byte rawTypeKind)
    {
        TypeSpecification specification = reader.GetTypeSpecification(handle);
        return specification.DecodeSignature(this, genericContext);
    }

    private static string FullName(string ns, string name)
    {
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    private static string Format(string returnType, ImmutableArray<string> parameters)
    {
        return returnType + " (" + string.Join(", ", parameters) + ")";
    }
}
