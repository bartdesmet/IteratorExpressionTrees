using System.Linq.Expressions;

namespace Microsoft.CSharp.Expressions
{
    public class CSharpExpressionVisitor : ExpressionVisitor
    {
        protected override Expression VisitExtension(Expression node)
        {
            if (node is CSharpExpression c)
            {
                return c.Accept(this);
            }

            return base.VisitExtension(node);
        }

        protected internal virtual Expression VisitIterator<TDelegate>(IteratorCSharpExpression<TDelegate> node) => node.Update(Visit(node.Body), VisitAndConvert(node.Parameters, nameof(VisitIterator)));

        protected internal virtual Expression VisitYieldReturn(YieldReturnCSharpExpression node) => node.Update(Visit(node.Value));

        protected internal virtual Expression VisitYieldBreak(YieldBreakCSharpExpression node) => node;
    }
}
