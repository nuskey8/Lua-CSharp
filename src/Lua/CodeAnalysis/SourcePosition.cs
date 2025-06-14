namespace Lua.CodeAnalysis;

public record struct SourcePosition
{
    public SourcePosition(int line, int column)
    {
        Line = line;
        Column = column;
    }

    public int Line { get; set; }
    public int Column { get; set; }
    public override readonly string ToString() => $"({Line},{Column})";
}