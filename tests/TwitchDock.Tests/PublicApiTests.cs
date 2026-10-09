using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace TwitchDock.Tests;

/// <summary>
/// Detects unintended public API changes (the plan's breaking-change check). Each package's surface is compared with
/// tests/TwitchDock.Tests/PublicApi/&lt;assembly&gt;.txt. After an intended change, run the tests once with
/// TWITCHDOCK_UPDATE_PUBLIC_API=1 and review the snapshot diff like any other code change.
/// </summary>
public sealed class PublicApiTests
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static TheoryData<string> Assemblies() =>
    [
        typeof(Core.AccessToken).Assembly.GetName().Name!, typeof(Authentication.TwitchOAuthClient).Assembly.GetName().Name!,
        typeof(Helix.HelixClient).Assembly.GetName().Name!, typeof(EventSub.EventSubMessage).Assembly.GetName().Name!,
        typeof(Chat.TwitchChatClient).Assembly.GetName().Name!, typeof(DependencyInjection.ServiceCollectionExtensions).Assembly.GetName().Name!,
    ];

    [Theory]
    [MemberData(nameof(Assemblies))]
    public void PublicApiMatchesTheApprovedSnapshot(string assemblyName)
    {
        var actual = Describe(Assembly.Load(assemblyName)).ToArray();
        var path = Path.Combine(RepositoryRoot(), "tests", "TwitchDock.Tests", "PublicApi", assemblyName + ".txt");
        if (Environment.GetEnvironmentVariable("TWITCHDOCK_UPDATE_PUBLIC_API") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, string.Join('\n', actual) + "\n");
            return;
        }
        Assert.True(File.Exists(path), $"Missing API snapshot {path}. Run the tests with TWITCHDOCK_UPDATE_PUBLIC_API=1.");
        var expected = File.ReadAllLines(path).Where(line => line.Length > 0).ToArray();
        var removed = expected.Except(actual, StringComparer.Ordinal).Take(40).ToArray();
        var added = actual.Except(expected, StringComparer.Ordinal).Take(40).ToArray();
        Assert.True(removed.Length == 0 && added.Length == 0,
            $"Public API of {assemblyName} changed. Removed:\n{string.Join('\n', removed)}\nAdded:\n{string.Join('\n', added)}\n" +
            "Removals are breaking changes. If intended, update the snapshot with TWITCHDOCK_UPDATE_PUBLIC_API=1 and record the change in CHANGELOG.md.");
    }

    internal static IEnumerable<string> Describe(Assembly assembly)
    {
        foreach (var type in assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            yield return Header(type);
            var members = type.GetMembers(Declared).Where(IsVisible).Select(m => "    " + Member(m)).Order(StringComparer.Ordinal);
            foreach (var member in members) yield return member;
        }
    }

    private static string Header(Type type)
    {
        var kind = type.IsEnum ? "enum" : type.IsInterface ? "interface" : type.IsValueType ? "struct"
            : type is { IsAbstract: true, IsSealed: true } ? "static class" : type.IsAbstract ? "abstract class" : type.IsSealed ? "sealed class" : "class";
        var bases = new List<string>();
        if (type.BaseType is { } baseType && baseType != typeof(object) && baseType != typeof(ValueType) && baseType != typeof(Enum)) bases.Add(Name(baseType));
        bases.AddRange(type.GetInterfaces().Select(Name).Order(StringComparer.Ordinal));
        return $"{kind} {Name(type)}" + (bases.Count > 0 ? " : " + string.Join(", ", bases) : "");
    }

    private static bool IsVisible(MemberInfo member) => member switch
    {
        MethodBase method => (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly) && (!method.IsSpecialName || method is ConstructorInfo || method.Name.StartsWith("op_", StringComparison.Ordinal)),
        FieldInfo field => (field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly) && !field.IsSpecialName,
        PropertyInfo property => property.GetAccessors(true).Any(a => a.IsPublic || a.IsFamily || a.IsFamilyOrAssembly),
        EventInfo @event => @event.AddMethod is { } add && (add.IsPublic || add.IsFamily),
        _ => false,
    };

    private static string Member(MemberInfo member) => member switch
    {
        ConstructorInfo constructor => $"{(constructor.IsStatic ? "static " : "")}.ctor({Parameters(constructor)})",
        MethodInfo method => $"{Modifiers(method)}{method.Name}{(method.IsGenericMethodDefinition ? "<" + string.Join(", ", method.GetGenericArguments().Select(a => a.Name)) + ">" : "")}({Parameters(method)}): {Name(method.ReturnType)}",
        PropertyInfo property => $"{(property.GetAccessors(true)[0].IsStatic ? "static " : "")}{property.Name}{Indexer(property)}: {Name(property.PropertyType)} {{ {Accessors(property)} }}",
        FieldInfo field => $"{(field.IsLiteral ? "const " : field.IsStatic ? "static " : "")}{(field.IsInitOnly ? "readonly " : "")}{field.Name}: {Name(field.FieldType)}{(field.IsLiteral ? " = " + Format(field.GetRawConstantValue()) : "")}",
        EventInfo @event => $"event {@event.Name}: {Name(@event.EventHandlerType!)}",
        _ => member.ToString()!,
    };

    private static string Modifiers(MethodInfo method)
        => (method.IsStatic ? "static " : "") + (method.IsAbstract ? "abstract " : method.IsVirtual && !method.IsFinal ? "virtual " : "") + (method.IsFamily ? "protected " : "");

    private static string Indexer(PropertyInfo property)
    {
        var parameters = property.GetIndexParameters();
        return parameters.Length == 0 ? "" : "[" + string.Join(", ", parameters.Select(p => Name(p.ParameterType))) + "]";
    }

    private static string Accessors(PropertyInfo property)
    {
        var accessors = new List<string>();
        if (property.GetMethod is { } get && (get.IsPublic || get.IsFamily)) accessors.Add("get;");
        if (property.SetMethod is { } set && (set.IsPublic || set.IsFamily))
            accessors.Add(set.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)) ? "init;" : "set;");
        return string.Join(' ', accessors);
    }

    private static string Parameters(MethodBase method) => string.Join(", ", method.GetParameters().Select(p =>
        (p.IsOut ? "out " : p.ParameterType.IsByRef ? (p.IsIn ? "in " : "ref ") : "")
        + (p.GetCustomAttribute<ParamArrayAttribute>() is not null ? "params " : "")
        + Name(p.ParameterType) + " " + p.Name + (p.HasDefaultValue ? " = " + Format(p.RawDefaultValue) : "")));

    private static string Format(object? value) => value switch
    {
        null => "null",
        string text => "\"" + text + "\"",
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()!,
    };

    private static string Name(Type type)
    {
        if (type.IsByRef || type.IsPointer) return Name(type.GetElementType()!);
        if (type.IsArray) return Name(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        if (type.IsGenericParameter) return type.Name;
        if (Nullable.GetUnderlyingType(type) is { } underlying) return Name(underlying) + "?";
        var name = type.IsNested ? Name(type.DeclaringType!) + "." + type.Name : (type.Namespace is { } ns ? ns + "." : "") + type.Name;
        if (!type.IsGenericType) return name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        return (tick < 0 ? name : name[..tick]) + "<" + string.Join(", ", type.GetGenericArguments().Select(Name)) + ">";
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "TwitchDock.slnx"))) return directory.FullName;
        throw new InvalidOperationException("The repository root (TwitchDock.slnx) was not found.");
    }
}
