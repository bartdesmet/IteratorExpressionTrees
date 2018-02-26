using System;
using System.Linq.Expressions;

namespace Microsoft.CSharp.Expressions
{
    public sealed class YieldReturnCSharpExpression : CSharpExpression
    {
        internal YieldReturnCSharpExpression(Expression value)
        {
            Value = value;
        }

        public Expression Value { get; }

        public override Type Type => typeof(void);

        public override bool CanReduce => false;

        protected internal override Expression Accept(CSharpExpressionVisitor visitor) => visitor.VisitYieldReturn(this);

        public YieldReturnCSharpExpression Update(Expression value)
        {
            if (value != Value)
            {
                return CSharpExpression.YieldReturn(value);
            }

            return this;
        }
    }
}
