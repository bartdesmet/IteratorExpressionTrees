# Iterator Expression Trees

This repository contains a prototype of expression tree support for iterators. It should be merged with the `Microsoft.CSharp.Expressions` effort and forms the basis for experiments with async iterators and bi-directional iterators.

## Usage

An example of constructing, compiling, instantiating, and iterating over a `Range` iterator is shown below:

```csharp
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
    );

IIterable<int> iterator = expr.Compile()(5, 10);

foreach (var x in iterator)
{
    Console.WriteLine(x);
}
```

## Goals

The goals of this project are:

* Extend the C# expression tree extensions to support iterators. Hopefully we can get iterator lambdas at some point.
* Play with implementation techniques for async iterators and bi-directional iterators.
* Experiment with operator fusion, a technique where iterators get inlined, allowing for runtime LINQ query optimization.
