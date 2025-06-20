namespace Lua.CodeAnalysis.Syntax;

public enum OperatorPrecedence
{
    /// <summary>
    /// Non-operator token precedence
    /// </summary>
    NonOperator,

    /// <summary>
    /// 'or' operator
    /// </summary>
    Or,

    /// <summary>
    /// 'and' operator
    /// </summary>
    And,

    /// <summary>
    /// Relational operators (&lt;, &lt;=, &gt;, &gt;=, ==, ~=)
    /// </summary>
    Relational,

    /// <summary>
    /// Concat operator (..)
    /// </summary>
    Concat,

    /// <summary>
    /// Addition and Subtraction (+, -)
    /// </summary>
    Addition,

    /// <summary>
    /// Multipilcation, Division and Modulo (*, /, %)
    /// </summary>
    Multiplication,

    /// <summary>
    /// Negate, Not, Length (-, 'not', #)
    /// </summary>
    Unary,

    /// <summary>
    /// Exponentiation (^)
    /// </summary>
    Exponentiation,
}