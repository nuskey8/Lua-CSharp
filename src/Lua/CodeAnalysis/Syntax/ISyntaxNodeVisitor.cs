using Lua.CodeAnalysis.Syntax.Nodes;

namespace Lua.CodeAnalysis.Syntax;

public interface ISyntaxNodeVisitor<TContext, TResult>
{
    TResult VisitNumericLiteralNode(NumericLiteralNode node, TContext context);
    TResult VisitBooleanLiteralNode(BooleanLiteralNode node, TContext context);
    TResult VisitNilLiteralNode(NilLiteralNode node, TContext context);
    TResult VisitStringLiteralNode(StringLiteralNode node, TContext context);
    TResult VisitUnaryExpressionNode(UnaryExpressionNode node, TContext context);
    TResult VisitBinaryExpressionNode(BinaryExpressionNode node, TContext context);
    TResult VisitGroupedExpressionNode(GroupedExpressionNode node, TContext context);
    TResult VisitIdentifierNode(IdentifierNode node, TContext context);
    TResult VisitDoStatementNode(DoStatementNode node, TContext context);
    TResult VisitFunctionDeclarationExpressionNode(FunctionDeclarationExpressionNode node, TContext context);
    TResult VisitFunctionDeclarationStatementNode(FunctionDeclarationStatementNode node, TContext context);
    TResult VisitLocalFunctionDeclarationStatementNode(LocalFunctionDeclarationStatementNode node, TContext context);
    TResult VisitWhileStatementNode(WhileStatementNode node, TContext context);
    TResult VisitRepeatStatementNode(RepeatStatementNode node, TContext context);
    TResult VisitIfStatementNode(IfStatementNode node, TContext context);
    TResult VisitLabelStatementNode(LabelStatementNode node, TContext context);
    TResult VisitGotoStatementNode(GotoStatementNode node, TContext context);
    TResult VisitBreakStatementNode(BreakStatementNode node, TContext context);
    TResult VisitReturnStatementNode(ReturnStatementNode node, TContext context);
    TResult VisitAssignmentStatementNode(AssignmentStatementNode node, TContext context);
    TResult VisitLocalAssignmentStatementNode(LocalAssignmentStatementNode node, TContext context);
    TResult VisitCallFunctionExpressionNode(CallFunctionExpressionNode node, TContext context);
    TResult VisitCallFunctionStatementNode(CallFunctionStatementNode node, TContext context);
    TResult VisitNumericForStatementNode(NumericForStatementNode node, TContext context);
    TResult VisitGenericForStatementNode(GenericForStatementNode node, TContext context);
    TResult VisitTableConstructorExpressionNode(TableConstructorExpressionNode node, TContext context);
    TResult VisitTableMethodDeclarationStatementNode(TableMethodDeclarationStatementNode node, TContext context);
    TResult VisitTableIndexerAccessExpressionNode(TableIndexerAccessExpressionNode node, TContext context);
    TResult VisitTableMemberAccessExpressionNode(TableMemberAccessExpressionNode node, TContext context);
    TResult VisitCallTableMethodExpressionNode(CallTableMethodExpressionNode node, TContext context);
    TResult VisitCallTableMethodStatementNode(CallTableMethodStatementNode node, TContext context);
    TResult VisitVariableArgumentsExpressionNode(VariableArgumentsExpressionNode node, TContext context);
    TResult VisitSyntaxTree(LuaSyntaxTree node, TContext context);
}