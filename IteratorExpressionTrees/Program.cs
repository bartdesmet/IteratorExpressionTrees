using Microsoft.CSharp.Expressions;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace IteratorExpressionTrees
{
    class Program
    {
        static void Main(string[] args)
        {
            var start = Expression.Parameter(typeof(int), "start");
            var count = Expression.Parameter(typeof(int), "count");

            var i = Expression.Parameter(typeof(int), "i");
            var max = Expression.Parameter(typeof(long), "max");
            var @break = Expression.Label("__Break");

            var expr =
                CSharpExpression.Iterator<Func<int, int, IIterable<int>>>(
                    Expression.Block(
                        new[] { i, max },
                        Expression.Assign(max, Expression.Subtract(Expression.Add(Expression.Convert(start, typeof(long)), Expression.Convert(count, typeof(long))), Expression.Constant(1L, typeof(long)))),
                        Expression.IfThen(
                            Expression.OrElse(
                                Expression.LessThan(count, Expression.Constant(0)),
                                Expression.GreaterThan(max, Expression.Constant((long)int.MaxValue))
                            ),
                            Expression.Throw(Expression.New(typeof(ArgumentOutOfRangeException).GetConstructor(new[] { typeof(string) }), Expression.Constant("count")))
                        ),
                        Expression.Assign(i, Expression.Constant(0)),
                        Expression.Loop(
                            Expression.Block(
                                Expression.IfThen(
                                    Expression.GreaterThanOrEqual(i, count),
                                    Expression.Break(@break)
                                ),
                                CSharpExpression.YieldReturn(Expression.Add(start, i)),
                                Expression.PostIncrementAssign(i)
                            ),
                            @break
                        )
                    ),
                    start,
                    count
                /*
                    Expression.Block(
                        CSharpExpression.YieldReturn(Expression.Constant(1)),
                        CSharpExpression.YieldReturn(Expression.Constant(2)),
                        Expression.TryFinally(
                            Expression.Block(
                                CSharpExpression.YieldReturn(Expression.Constant(3)),
                                Expression.TryFinally(
                                    CSharpExpression.YieldReturn(Expression.Constant(4)),
                                    Expression.Empty()
                                ),
                                CSharpExpression.YieldReturn(Expression.Constant(5))
                            ),
                            Expression.Empty()
                        ),
                        CSharpExpression.YieldReturn(Expression.Constant(6)),
                        Expression.TryFinally(
                            CSharpExpression.YieldReturn(Expression.Constant(7)),
                            Expression.Empty()
                        ),
                        CSharpExpression.YieldReturn(Expression.Constant(8))
                    )
                */
                );

            IIterable<int> iterator = expr.Reduce().Compile()(5, 10);

            foreach (var y in iterator)
            {
                Console.WriteLine(y);
            }
        }
    }
}
