using System.Text;
using Lua.CodeAnalysis.Syntax.Nodes;

namespace Lua.CodeAnalysis.Syntax;

public sealed class DisplayStringSyntaxVisitor : ISyntaxNodeVisitor<DisplayStringSyntaxVisitor.Context, bool>
{
    public sealed class Context
    {
        public readonly ref struct IndentScope
        {
            readonly Context source;

            public IndentScope(Context source)
            {
                this.source = source;
                source.IncreaseIndent();
            }

            public void Dispose()
            {
                source.DecreaseIndent();
            }
        }

        readonly StringBuilder buffer = new();
        int indentLevel;
        bool isNewLine = true;

        public IndentScope BeginIndentScope() => new(this);

        public void Append(string value)
        {
            if (isNewLine)
            {
                buffer.Append(' ', indentLevel * 4);
                isNewLine = false;
            }

            buffer.Append(value);
        }

        public void AppendLine(string value)
        {
            if (isNewLine)
            {
                buffer.Append(' ', indentLevel * 4);
                isNewLine = false;
            }

            buffer.AppendLine(value);
            isNewLine = true;
        }

        public void AppendLine()
        {
            buffer.AppendLine();
            isNewLine = true;
        }

        public override string ToString() => buffer.ToString();

        public void IncreaseIndent()
        {
            indentLevel++;
        }

        public void DecreaseIndent()
        {
            if (indentLevel > 0)
                indentLevel--;
        }

        public void Reset()
        {
            buffer.Clear();
            indentLevel = 0;
            isNewLine = true;
        }
    }

    readonly Context context = new();

    public string GetDisplayString(SyntaxNode node)
    {
        context.Reset();
        node.Accept(this, context);
        return context.ToString();
    }

    public bool VisitBinaryExpressionNode(BinaryExpressionNode node, Context context)
    {
        node.LeftNode.Accept(this, context);
        context.Append($" {node.OperatorType.ToDisplayString()} ");
        node.RightNode.Accept(this, context);
        return true;
    }

    public bool VisitBooleanLiteralNode(BooleanLiteralNode node, Context context)
    {
        context.Append(node.Value ? Keywords.True : Keywords.False);
        return true;
    }

    public bool VisitBreakStatementNode(BreakStatementNode node, Context context)
    {
        context.Append(Keywords.Break);
        return true;
    }

    public bool VisitCallFunctionExpressionNode(CallFunctionExpressionNode node, Context context)
    {
        node.FunctionNode.Accept(this, context);
        context.Append("(");
        VisitSyntaxNodes(node.ArgumentNodes, context);
        context.Append(")");
        return true;
    }

    public bool VisitCallFunctionStatementNode(CallFunctionStatementNode node, Context context)
    {
        node.Expression.Accept(this, context);
        return true;
    }

    public bool VisitDoStatementNode(DoStatementNode node, Context context)
    {
        context.AppendLine("do");
        using (context.BeginIndentScope())
        {
            foreach (var childNode in node.StatementNodes)
            {
                childNode.Accept(this, context);
                context.AppendLine();
            }
        }

        context.AppendLine("end");

        return true;
    }

    public bool VisitFunctionDeclarationExpressionNode(FunctionDeclarationExpressionNode node, Context context)
    {
        context.Append("function(");
        VisitSyntaxNodes(node.ParameterNodes, context);
        if (node.HasVariableArguments)
        {
            if (node.ParameterNodes.Length > 0) context.Append(", ");
            context.Append("...");
        }

        context.AppendLine(")");

        using (context.BeginIndentScope())
        {
            foreach (var childNode in node.Nodes)
            {
                childNode.Accept(this, context);
                context.AppendLine();
            }
        }

        context.AppendLine("end");

        return true;
    }

    public bool VisitFunctionDeclarationStatementNode(FunctionDeclarationStatementNode node, Context context)
    {
        context.Append("function ");
        context.Append(node.Name.ToString());
        context.Append("(");
        VisitSyntaxNodes(node.ParameterNodes, context);
        if (node.HasVariableArguments)
        {
            if (node.ParameterNodes.Length > 0) context.Append(", ");
            context.Append("...");
        }

        context.AppendLine(")");

        using (context.BeginIndentScope())
        {
            foreach (var childNode in node.Nodes)
            {
                childNode.Accept(this, context);
                context.AppendLine();
            }
        }

        context.AppendLine("end");

        return true;
    }

    public bool VisitTableMethodDeclarationStatementNode(TableMethodDeclarationStatementNode node, Context context)
    {
        context.Append("function ");

        for (int i = 0; i < node.MemberPath.Length; i++)
        {
            context.Append(node.MemberPath[i].Name.ToString());

            if (i == node.MemberPath.Length - 2 && node.HasSelfParameter)
            {
                context.Append(":");
            }
            else if (i != node.MemberPath.Length - 1)
            {
                context.Append(".");
            }
        }

        context.Append("(");
        VisitSyntaxNodes(node.ParameterNodes, context);
        if (node.HasVariableArguments)
        {
            if (node.ParameterNodes.Length > 0) context.Append(", ");
            context.Append("...");
        }

        context.AppendLine(")");

        using (context.BeginIndentScope())
        {
            foreach (var childNode in node.Nodes)
            {
                childNode.Accept(this, context);
                context.AppendLine();
            }
        }

        context.AppendLine("end");

        return true;
    }

    public bool VisitGenericForStatementNode(GenericForStatementNode node, Context context)
    {
        context.Append($"for ");
        VisitSyntaxNodes(node.Names, context);
        context.Append(" in ");
        VisitSyntaxNodes(node.ExpressionNodes, context);
        context.AppendLine(" do");
        using (context.BeginIndentScope())
        {
            foreach (var childNode in node.StatementNodes)
            {
                childNode.Accept(this, context);
                context.AppendLine();
            }
        }

        context.AppendLine("end");

        return true;
    }

    public bool VisitGotoStatementNode(GotoStatementNode node, Context context)
    {
        context.Append($"goto {node.Name}");
        return true;
    }

    public bool VisitIdentifierNode(IdentifierNode node, Context context)
    {
        context.Append(node.Name.ToString());
        return true;
    }

    public bool VisitIfStatementNode(IfStatementNode node, Context context)
    {
        context.Append("if ");
        node.IfNode.ConditionNode.Accept(this, context);
        context.AppendLine(" then");

        using (context.BeginIndentScope())
        {
            foreach (var childNode in node.IfNode.ThenNodes)
            {
                childNode.Accept(this, context);
                context.AppendLine();
            }
        }

        foreach (var elseif in node.ElseIfNodes)
        {
            context.Append("elseif ");
            elseif.ConditionNode.Accept(this, context);
            context.AppendLine(" then");

            using (context.BeginIndentScope())
            {
                foreach (var childNode in elseif.ThenNodes)
                {
                    childNode.Accept(this, context);
                    context.AppendLine();
                }
            }
        }

        if (node.ElseNodes.Length > 0)
        {
            context.AppendLine("else");

            using (context.BeginIndentScope())
            {
                foreach (var childNode in node.ElseNodes)
                {
                    childNode.Accept(this, context);
                    context.AppendLine();
                }
            }
        }

        context.Append("end");

        return true;
    }

    public bool VisitLabelStatementNode(LabelStatementNode node, Context context)
    {
        context.Append($"::{node.Name}::");
        return true;
    }

    public bool VisitAssignmentStatementNode(AssignmentStatementNode node, Context context)
    {
        VisitSyntaxNodes(node.LeftNodes, context);

        if (node.RightNodes.Length > 0)
        {
            context.Append(" = ");
            VisitSyntaxNodes(node.RightNodes, context);
        }

        return true;
    }

    public bool VisitLocalAssignmentStatementNode(LocalAssignmentStatementNode node, Context context)
    {
        context.Append("local ");
        return VisitAssignmentStatementNode(node, context);
    }

    public bool VisitLocalFunctionDeclarationStatementNode(LocalFunctionDeclarationStatementNode node, Context context)
    {
        context.Append("local ");
        return VisitFunctionDeclarationStatementNode(node, context);
    }

    public bool VisitNilLiteralNode(NilLiteralNode node, Context context)
    {
        context.Append(Keywords.Nil);
        return true;
    }

    public bool VisitNumericForStatementNode(NumericForStatementNode node, Context context)
    {
        context.Append($"for {node.VariableName} = ");
        node.InitNode.Accept(this, context);
        context.Append(", ");
        node.LimitNode.Accept(this, context);
        if (node.StepNode != null)
        {
            context.Append(", ");
            node.StepNode.Accept(this, context);
        }

        context.AppendLine(" do");
        using (context.BeginIndentScope())
        {
            foreach (var childNode in node.StatementNodes)
            {
                childNode.Accept(this, context);
                context.AppendLine();
            }
        }

        context.AppendLine("end");

        return true;
    }

    public bool VisitNumericLiteralNode(NumericLiteralNode node, Context context)
    {
        context.Append(node.Value.ToString());
        return true;
    }

    public bool VisitRepeatStatementNode(RepeatStatementNode node, Context context)
    {
        context.AppendLine("repeat");

        using (context.BeginIndentScope())
        {
            foreach (var childNode in node.Nodes)
            {
                childNode.Accept(this, context);
                context.AppendLine();
            }
        }

        context.Append("until ");
        node.ConditionNode.Accept(this, context);
        context.AppendLine();

        return true;
    }

    public bool VisitReturnStatementNode(ReturnStatementNode node, Context context)
    {
        context.Append("return ");
        VisitSyntaxNodes(node.Nodes, context);
        return true;
    }

    public bool VisitStringLiteralNode(StringLiteralNode node, Context context)
    {
        if (node.IsShortLiteral)
        {
            context.Append("\"");
            context.Append(node.Text.ToString());
            context.Append("\"");
        }
        else
        {
            context.Append("[[");
            context.Append(node.Text.ToString());
            context.Append("]]");
        }

        return true;
    }

    public bool VisitSyntaxTree(LuaSyntaxTree node, Context context)
    {
        foreach (var statement in node.Nodes)
        {
            statement.Accept(this, context);
            context.AppendLine();
        }

        return true;
    }

    public bool VisitTableConstructorExpressionNode(TableConstructorExpressionNode node, Context context)
    {
        context.AppendLine("{");
        using (context.BeginIndentScope())
        {
            for (int i = 0; i < node.Fields.Length; i++)
            {
                var field = node.Fields[i];

                switch (field)
                {
                    case GeneralTableConstructorField general:
                        context.Append("[");
                        general.KeyExpression.Accept(this, context);
                        context.Append("] = ");
                        general.ValueExpression.Accept(this, context);
                        break;
                    case RecordTableConstructorField record:
                        context.Append($"{record.Key} = ");
                        record.ValueExpression.Accept(this, context);
                        break;
                    case ListTableConstructorField list:
                        list.Expression.Accept(this, context);
                        break;
                }

                context.AppendLine(i == node.Fields.Length - 1 ? "" : ",");
            }
        }

        context.AppendLine("}");

        return true;
    }

    public bool VisitTableIndexerAccessExpressionNode(TableIndexerAccessExpressionNode node, Context context)
    {
        node.TableNode.Accept(this, context);
        context.Append("[");
        node.KeyNode.Accept(this, context);
        context.Append("]");
        return true;
    }

    public bool VisitTableMemberAccessExpressionNode(TableMemberAccessExpressionNode node, Context context)
    {
        node.TableNode.Accept(this, context);
        context.Append($".{node.MemberName}");
        return true;
    }

    public bool VisitCallTableMethodExpressionNode(CallTableMethodExpressionNode node, Context context)
    {
        node.TableNode.Accept(this, context);
        context.Append($":{node.MethodName}(");
        VisitSyntaxNodes(node.ArgumentNodes, context);
        context.Append(")");
        return true;
    }

    public bool VisitCallTableMethodStatementNode(CallTableMethodStatementNode node, Context context)
    {
        return node.Expression.Accept(this, context);
    }

    public bool VisitUnaryExpressionNode(UnaryExpressionNode node, Context context)
    {
        context.Append(node.Operator.ToDisplayString());
        if (node.Operator is UnaryOperator.Not) context.Append(" ");
        node.Node.Accept(this, context);

        return true;
    }

    public bool VisitWhileStatementNode(WhileStatementNode node, Context context)
    {
        context.Append("while ");
        node.ConditionNode.Accept(this, context);
        context.AppendLine(" do");

        using (context.BeginIndentScope())
        {
            foreach (var childNode in node.Nodes)
            {
                childNode.Accept(this, context);
                context.AppendLine();
            }
        }

        context.AppendLine("end");

        return true;
    }

    public bool VisitVariableArgumentsExpressionNode(VariableArgumentsExpressionNode node, Context context)
    {
        context.Append("...");
        return true;
    }

    void VisitSyntaxNodes(SyntaxNode[] nodes, Context context)
    {
        for (int i = 0; i < nodes.Length; i++)
        {
            nodes[i].Accept(this, context);
            if (i != nodes.Length - 1) context.Append(", ");
        }
    }

    public bool VisitGroupedExpressionNode(GroupedExpressionNode node, Context context)
    {
        context.Append("(");
        node.Expression.Accept(this, context);
        context.Append(")");
        return true;
    }
}