using Microsoft.CodeAnalysis;

namespace Lua.SourceGenerator;

static class RoslynAnalyzerExtensions
{
    public static AttributeData? FindAttribute(this IEnumerable<AttributeData> attributeDataList, string typeName)
    {
        return attributeDataList
            .FirstOrDefault(x => x.AttributeClass?.ToDisplayString() == typeName);
    }

    public static AttributeData? FindAttributeShortName(this IEnumerable<AttributeData> attributeDataList, string typeName)
    {
        return attributeDataList
            .FirstOrDefault(x => x.AttributeClass?.Name == typeName);
    }
}