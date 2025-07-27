using System.Text;

namespace Lua.SourceGenerator;

sealed class CodeBuilder
{
    public ref struct IndentScope
    {
        readonly CodeBuilder source;

        public IndentScope(CodeBuilder source, string? startLine = null)
        {
            this.source = source;
            if (startLine != null)
            {
                source.AppendLine(startLine);
            }

            source.IncreaseIndent();
        }

        public void Dispose()
        {
            source.DecreaseIndent();
        }
    }

    public ref struct BlockScope
    {
        readonly CodeBuilder source;

        public BlockScope(CodeBuilder source, string? startLine = null)
        {
            this.source = source;
            if (startLine != null)
            {
                source.AppendLine(startLine);
            }

            source.BeginBlock();
        }

        public void Dispose()
        {
            source.EndBlock();
        }
    }

    readonly StringBuilder buffer = new();
    int indentLevel;

    public IndentScope BeginIndentScope(string? startLine = null)
    {
        return new(this, startLine);
    }

    public BlockScope BeginBlockScope(string? startLine = null)
    {
        return new(this, startLine);
    }

    public void Append(string value, bool indent = true)
    {
        if (indent)
        {
            buffer.Append($"{new string(' ', indentLevel * 4)} {value}");
        }
        else
        {
            buffer.Append(value);
        }
    }

    public void AppendLine(string? value = null, bool indent = true)
    {
        if (string.IsNullOrEmpty(value))
        {
            buffer.AppendLine();
        }
        else if (indent)
        {
            buffer.AppendLine($"{new string(' ', indentLevel * 4)} {value}");
        }
        else
        {
            buffer.AppendLine(value);
        }
    }

    public void AppendByteArrayString(byte[] bytes)
    {
        buffer.Append("{ ");
        var first = true;
        foreach (var x in bytes)
        {
            if (!first)
            {
                buffer.Append(", ");
            }

            buffer.Append(x);
            first = false;
        }

        buffer.Append(" }");
    }

    public override string ToString()
    {
        return buffer.ToString();
    }

    public void IncreaseIndent()
    {
        indentLevel++;
    }

    public void DecreaseIndent()
    {
        if (indentLevel > 0)
        {
            indentLevel--;
        }
    }

    public void BeginBlock()
    {
        AppendLine("{");
        IncreaseIndent();
    }

    public void EndBlock()
    {
        DecreaseIndent();
        AppendLine("}");
    }

    public void Clear()
    {
        buffer.Clear();
    }
}