using System;
using System.Linq.Expressions;

namespace Microsoft.CSharp.Expressions
{
    public sealed class YieldBreakCSharpExpression : CSharpExpression
    {
        internal YieldBreakCSharpExpression()
        {
        }

        public override Type Type => typeof(void);

        public override bool CanReduce => false;

        protected internal override Expression Accept(CSharpExpressionVisitor visitor) => visitor.VisitYieldBreak(this);
    }
}
