using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

if (args.Length < 2)
{
    Console.WriteLine("用法: ApiDump <assembly.dll> <TypeFullName> [更多类型...]");
    return;
}

string asmPath = args[0];
var wanted = new HashSet<string>(args.Skip(1));
bool findMode = args[1].StartsWith("--find:");
string findNeedle = findMode ? args[1].Substring("--find:".Length) : null;

using var fs = File.OpenRead(asmPath);
using var pe = new PEReader(fs);
var md = pe.GetMetadataReader();
var provider = new SigProvider();

foreach (var tdh in md.TypeDefinitions)
{
    var td = md.GetTypeDefinition(tdh);
    string ns = md.GetString(td.Namespace);
    string name = md.GetString(td.Name);
    string full = string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    if (findMode)
    {
        if (full.IndexOf(findNeedle, StringComparison.OrdinalIgnoreCase) >= 0)
            Console.WriteLine(full);
        continue;
    }
    if (!wanted.Contains(full))
        continue;

    string baseType = "";
    if (!td.BaseType.IsNil)
        baseType = Decode(td.BaseType, md);

    Console.WriteLine();
    Console.WriteLine($"=== {full} : {baseType} ===");

    foreach (var fh in td.GetFields())
    {
        var f = md.GetFieldDefinition(fh);
        var acc = f.Attributes & FieldAttributes.FieldAccessMask;
        if (acc != FieldAttributes.Public && acc != FieldAttributes.Family)
            continue;
        string ftype = f.DecodeSignature(provider, null);
        Console.WriteLine($"  field  {ftype} {md.GetString(f.Name)}");
    }

    foreach (var ph in td.GetProperties())
    {
        var p = md.GetPropertyDefinition(ph);
        var sig = p.DecodeSignature(provider, null);
        Console.WriteLine($"  prop   {sig.ReturnType} {md.GetString(p.Name)}");
    }

    foreach (var mh in td.GetMethods())
    {
        var m = md.GetMethodDefinition(mh);
        var acc = m.Attributes & MethodAttributes.MemberAccessMask;
        if (acc != MethodAttributes.Public && acc != MethodAttributes.Family)
            continue;
        var sig = m.DecodeSignature(provider, null);
        string ps = string.Join(", ", sig.ParameterTypes);
        Console.WriteLine($"  method {sig.ReturnType} {md.GetString(m.Name)}({ps})");
    }
}

static string Decode(EntityHandle h, MetadataReader md)
{
    return h.Kind switch
    {
        HandleKind.TypeDefinition => md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)h).Name),
        HandleKind.TypeReference => md.GetString(md.GetTypeReference((TypeReferenceHandle)h).Name),
        _ => "?"
    };
}

sealed class SigProvider : ISignatureTypeProvider<string, object>
{
    public string GetArrayType(string e, ArrayShape s) => e + "[]";
    public string GetByReferenceType(string e) => "ref " + e;
    public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr";
    public string GetGenericInstantiation(string g, ImmutableArray<string> a) => g + "<" + string.Join(",", a) + ">";
    public string GetGenericMethodParameter(object c, int i) => "!!" + i;
    public string GetGenericTypeParameter(object c, int i) => "!" + i;
    public string GetModifiedType(string m, string u, bool r) => u;
    public string GetPinnedType(string e) => e;
    public string GetPointerType(string e) => e + "*";
    public string GetPrimitiveType(PrimitiveTypeCode t) => t.ToString();
    public string GetSZArrayType(string e) => e + "[]";

    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k)
        => r.GetString(r.GetTypeDefinition(h).Name);

    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k)
        => r.GetString(r.GetTypeReference(h).Name);

    public string GetTypeFromSpecification(MetadataReader r, object c, TypeSpecificationHandle h, byte k)
        => "spec";
}
