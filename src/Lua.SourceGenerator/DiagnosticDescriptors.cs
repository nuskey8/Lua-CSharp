using Microsoft.CodeAnalysis;

namespace Lua.SourceGenerator;

public static class DiagnosticDescriptors
{
    const string Category = "Lua";

    public static readonly DiagnosticDescriptor MustBePartial = new(
        id: "LUACS001",
        title: "LuaObject type must be partial.",
        category: Category,
        messageFormat: "LuaObject type '{0}' must be partial",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor NestedNotAllowed = new(
        id: "LUACS002",
        title: "LuaObject type must not be nested",
        messageFormat: "LuaObject type '{0}' must be not nested",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor AbstractNotAllowed = new(
        id: "LUACS003",
        title: "LuaObject type must not abstract",
        messageFormat: "LuaObject object '{0}' must be not abstract",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor InvalidPropertyType = new(
        id: "LUACS004",
        title: "The type of the field or property must be LuaValue or a type that can be converted to LuaValue.",
        messageFormat: "The type of '{0}' must be LuaValue or a type that can be converted to LuaValue.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor InvalidReturnType = new(
        id: "LUACS005",
        title: "The return type must be LuaValue or types that can be converted to LuaValue.",
        messageFormat: "The return type '{0}' must be LuaValue or types that can be converted to LuaValue.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor InvalidParameterType = new(
        id: "LUACS006",
        title: "The parameters must be LuaValue or types that can be converted to LuaValue.",
        messageFormat: "The parameter '{0}' must be LuaValue or types that can be converted to LuaValue.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor DuplicateMetamethod = new(
        id: "LUACS007",
        title: "The type already contains same metamethod.",
        messageFormat: "Type '{0}' already contains a '{1}' metamethod.,",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor InvalidAllowNilMemberType = new(
        id: "LUACS008",
        title: "AllowNil can only be used on LuaObject reference type members.",
        messageFormat: "AllowNil can only be used on LuaObject reference type members.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );
}
