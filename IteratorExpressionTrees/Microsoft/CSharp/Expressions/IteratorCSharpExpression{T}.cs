using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Microsoft.CSharp.Expressions
{
    public sealed class IteratorCSharpExpression<TDelegate> : IteratorCSharpExpression
    {
        internal IteratorCSharpExpression(IteratorInfo iteratorInfo, Expression body, ReadOnlyCollection<ParameterExpression> parameters)
            : base(iteratorInfo, body, parameters)
        {
        }

        public override Type Type => typeof(TDelegate);

        public TDelegate Compile() => Reduce().Compile();

        public new Expression<TDelegate> Reduce()
        {
            var originalParameters = Parameters;
            var lambdaWithParametersTwice = Expression.Lambda(Expression.Lambda(Body, originalParameters), originalParameters); // NB: Trick to rename parameters in body and retain original ones at the top.

            var alphaRenamedOuterLambda = new AlphaRenamer().VisitAndConvert(lambdaWithParametersTwice, nameof(Reduce));
            var alphaRenamedInnerLambda = (LambdaExpression)alphaRenamedOuterLambda.Body;
            var clonedParameters = alphaRenamedInnerLambda.Parameters;

            var alphaRenamedBody = alphaRenamedInnerLambda.Body;

            var bodyWithTry = Expression.TryFinally(alphaRenamedBody, Expression.Empty()); // NB: Simplifies the jump table generation.

            var analyzer = new IteratorBodyAnalyzer(new ReadOnlyCollection<ParameterExpression>(Array.Empty<ParameterExpression>())); // NB: Could pass parameters later if we do data flow analysis.
            analyzer.Visit(bodyWithTry);

            var yieldReturnInfo = new Dictionary<YieldReturnCSharpExpression, (int state, LabelTarget resumeLabel)>();
            var tryStatementInfo = new Dictionary<TryExpression, (LabelTarget label, List<(int state, LabelTarget label)> branches)>();

            int stateIndex = 1;
            int tryLabelIndex = 1;
            foreach (var yield in analyzer.YieldReturns)
            {
                if (!IteratorInfo.ElementType.IsAssignableFrom(yield.Node.Value.Type))
                {
                    throw new InvalidOperationException("Type of value in yield return node is not assignable to iterator element type.");
                }

                var stateId = stateIndex++;
                yieldReturnInfo.Add(yield.Node, (stateId, Expression.Label("__State" + stateId)));

                foreach (var @try in yield.TryStatements)
                {
                    if (!tryStatementInfo.TryGetValue(@try, out var data))
                    {
                        var tryId = tryLabelIndex++;
                        var branches = new List<(int, LabelTarget)>();
                        tryStatementInfo.Add(@try, (Expression.Label("__Try" + tryId), branches));
                    }
                }
            }

            foreach (var yield in analyzer.YieldReturns)
            {
                var (state, resumeLabel) = yieldReturnInfo[yield.Node];

                var targetLabel = resumeLabel;

                for (var i = yield.TryStatements.Length - 1; i >= 0; i--)
                {
                    var @try = yield.TryStatements[i];
                    var (label, branches) = tryStatementInfo[@try];

                    branches.Add((state, targetLabel));

                    targetLabel = label;
                }
            }

            var localsToHoist = new HashSet<ParameterExpression>(analyzer.YieldReturns.SelectMany(y => y.Variables));

            var stateVariable = Expression.Parameter(typeof(int), "state");
            var shouldBreakVariable = Expression.Parameter(typeof(bool), "shouldBreak");
            var nextStateOutVariable = Expression.Parameter(typeof(int).MakeByRefType(), "nextState");
            var hasNextOutVariable = Expression.Parameter(typeof(bool).MakeByRefType(), "hasNext");
            var shouldRunFinallyVariable = Expression.Parameter(typeof(bool), "__shouldRunFinally");
            var resultTemporaryVariable = Expression.Parameter(IteratorInfo.ElementType, "__result");

            var assignNextStateNone = Expression.Assign(nextStateOutVariable, Expression.Constant(-1));
            var assignHasNextFalse = Expression.Assign(hasNextOutVariable, Expression.Constant(false));

            var yieldBreakLabel = Expression.Label("__YieldBreak");
            var returnLabel = Expression.Label(IteratorInfo.ElementType, "__Return");

            var rewriter = new IteratorBodyRewriter(stateVariable, shouldBreakVariable, nextStateOutVariable, hasNextOutVariable, shouldRunFinallyVariable, resultTemporaryVariable, yieldBreakLabel, returnLabel, yieldReturnInfo, tryStatementInfo, localsToHoist);

            var rewrittenIteratorBody = rewriter.Visit(bodyWithTry);

            var body =
                Expression.Block(
                    IteratorInfo.ElementType,
                    new[] { shouldRunFinallyVariable, resultTemporaryVariable },
                    Expression.Assign(shouldRunFinallyVariable, Expression.Constant(true)),
                    rewrittenIteratorBody,
                    Expression.Label(yieldBreakLabel),
                    assignNextStateNone,
                    assignHasNextFalse,
                    Expression.Label(returnLabel, Expression.Default(IteratorInfo.ElementType))
                );

            var tryGetNextLambda = Expression.Lambda(typeof(TryGetNext<>).MakeGenericType(IteratorInfo.ElementType), body, stateVariable, shouldBreakVariable, nextStateOutVariable, hasNextOutVariable);

            var tryGetNextLambdaFactory =
                Expression.Lambda(
                    Expression.Block(
                        clonedParameters.Concat(localsToHoist),
                        clonedParameters.Zip(originalParameters, (c, o) => Expression.Assign(c, o)).Concat(new Expression[]
                        {
                            tryGetNextLambda
                        })
                    )
                );

            // Func<TryGetNext<T>> factory = () =>
            // {
            //     var p1' = p1;
            //     var p2' = p2;
            //     var h1 = default;
            //     var h2 = default;
            //     return (a, b, c, d) => ...;
            // };
            // ((p1, p2) => new TIterator(factory))(p1, p2, default, default);

            var runtimeIteratorBuilderType = GetRuntimeIteratorBuilderType(IteratorInfo.BuilderType);
            var runtimeIteratorBuilderCtor = runtimeIteratorBuilderType.GetConstructors().Single();

            var result =
                Expression.Lambda<TDelegate>(
                    Expression.New(runtimeIteratorBuilderCtor, tryGetNextLambdaFactory),
                    originalParameters
                );

            return result;
        }

        protected override LambdaExpression ReduceCore() => (Expression<TDelegate>)Reduce();

        protected internal override Expression Accept(CSharpExpressionVisitor visitor) => visitor.VisitIterator(this);

        public IteratorCSharpExpression<TDelegate> Update(Expression body, ReadOnlyCollection<ParameterExpression> parameters)
        {
            if (body != Body || parameters != Parameters)
            {
                return CSharpExpression.Iterator<TDelegate>(body, parameters);
            }

            return this;
        }

        private sealed class AlphaRenamer : CSharpExpressionVisitor
        {
            private readonly Stack<Dictionary<ParameterExpression, ParameterExpression>> _env = new Stack<Dictionary<ParameterExpression, ParameterExpression>>();

            protected override Expression VisitLambda<T>(Expression<T> node)
            {
                var parameters = node.Parameters;

                if (parameters.Count > 0)
                {
                    var newParameters = Push(parameters);

                    var body = Visit(node.Body);

                    Pop();

                    return node.Update(body, newParameters);
                }
                else
                {
                    var body = Visit(node.Body);

                    return node.Update(body, parameters);
                }
            }

            protected override CatchBlock VisitCatchBlock(CatchBlock node)
            {
                if (node.Variable != null)
                {
                    var newVariable = Push(new[] { node.Variable }).Single();

                    var filter = Visit(node.Filter);
                    var body = Visit(node.Body);

                    Pop();

                    return node.Update(newVariable, filter, body);
                }
                else
                {
                    var filter = Visit(node.Filter);
                    var body = Visit(node.Body);

                    return node.Update(node.Variable, filter, body);
                }
            }

            protected override Expression VisitBlock(BlockExpression node)
            {
                var variables = node.Variables;

                if (variables.Count > 0)
                {
                    var newVariables = Push(variables);

                    var expressions = Visit(node.Expressions);

                    Pop();

                    return node.Update(newVariables, expressions);
                }
                else
                {
                    var expressions = Visit(node.Expressions);

                    return node.Update(variables, expressions);
                }
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                if (TryFindVariable(node, out var substitution) && substitution != null)
                {
                    return substitution;
                }

                return node;
            }

            private IEnumerable<ParameterExpression> Push(IEnumerable<ParameterExpression> variables)
            {
                var newFrame = new Dictionary<ParameterExpression, ParameterExpression>();
                var newVariables = new List<ParameterExpression>();

                foreach (var variable in variables)
                {
                    if (TryFindVariable(variable, out _))
                    {
                        var substition = Expression.Parameter(variable.Type, variable.Name);
                        newFrame.Add(variable, substition);
                        newVariables.Add(substition);
                    }
                    else
                    {
                        newFrame.Add(variable, null);
                        newVariables.Add(variable);
                    }
                }

                _env.Push(newFrame);

                return newVariables;
            }

            private bool TryFindVariable(ParameterExpression variable, out ParameterExpression substitution)
            {
                foreach (var frame in _env)
                {
                    foreach (var (var, subst) in frame)
                    {
                        if (variable == var)
                        {
                            substitution = subst;
                            return true;
                        }
                    }
                }

                substitution = null;
                return false;
            }

            private void Pop()
            {
                _env.Pop();
            }
        }

        private class IteratorVisitor : CSharpExpressionVisitor
        {
            protected internal override Expression VisitIterator<TIteratorDelegate>(IteratorCSharpExpression<TIteratorDelegate> node) => node;

            protected override Expression VisitLambda<T>(Expression<T> node) => node;
        }

        private sealed class YieldReturnInfo
        {
            public YieldReturnCSharpExpression Node;
            public ParameterExpression[] Variables;
            public TryExpression[] TryStatements;
        }

        private sealed class IteratorBodyAnalyzer : IteratorVisitor
        {
            public List<YieldReturnInfo> YieldReturns = new List<YieldReturnInfo>();

            private bool _rejectYield;
            private Stack<IEnumerable<ParameterExpression>> _variables = new Stack<IEnumerable<ParameterExpression>>();
            private List<TryExpression> _tryStatements = new List<TryExpression>();

            public IteratorBodyAnalyzer(ReadOnlyCollection<ParameterExpression> parameters)
            {
                if (parameters.Count > 0)
                {
                    _variables.Push(parameters);
                }
            }

            protected override Expression VisitBlock(BlockExpression node)
            {
                if (node.Variables.Count > 0)
                {
                    _variables.Push(node.Variables);
                }

                Visit(node.Expressions);

                if (node.Variables.Count > 0)
                {
                    _variables.Pop();
                }

                return node;
            }

            protected override Expression VisitTry(TryExpression node)
            {
                _tryStatements.Add(node);
                {
                    Visit(node.Body);

                    var rejectYield = _rejectYield;
                    _rejectYield = true;
                    {
                        Visit(node.Handlers, VisitCatchBlock);
                        Visit(node.Finally);
                        Visit(node.Fault);
                    }
                    _rejectYield = rejectYield;
                }
                _tryStatements.RemoveAt(_tryStatements.Count - 1);

                return node;
            }

            protected internal override Expression VisitYieldReturn(YieldReturnCSharpExpression node)
            {
                if (_rejectYield)
                    throw new InvalidOperationException("Yield return statement cannot occur here.");

                var info = new YieldReturnInfo { Node = node, Variables = _variables.SelectMany(vars => vars).ToArray(), TryStatements = _tryStatements.ToArray() };

                YieldReturns.Add(info);

                return node;
            }

            protected internal override Expression VisitYieldBreak(YieldBreakCSharpExpression node)
            {
                if (_rejectYield)
                    throw new InvalidOperationException("Yield break statement cannot occur here.");

                return node;
            }

            protected override Expression VisitGoto(GotoExpression node)
            {
                if (node.Kind == GotoExpressionKind.Return)
                    throw new InvalidOperationException("Return statement cannot occur in body of iterator. Use yield break.");

                return base.VisitGoto(node);
            }
        }

        private sealed class IteratorBodyRewriter : IteratorVisitor
        {
            private readonly ParameterExpression _stateVariable, _shouldBreakVariable, _nextStateOutVariable, _hasNextOutVariable, _shouldRunFinallyVariable, _resultTemporaryVariable;
            private readonly LabelTarget _returnLabel;
            private readonly Expression _assignHasNextTrue, _assignShouldRunFinallyFalse, _gotoYieldBreak, _checkYieldBreak;
            private readonly Dictionary<YieldReturnCSharpExpression, (int state, LabelTarget resumeLabel)> _yieldReturnInfo;
            private readonly Dictionary<TryExpression, (LabelTarget label, List<(int state, LabelTarget label)> branches)> _tryStatementInfo;
            private readonly HashSet<ParameterExpression> _localsToHoist;

            public IteratorBodyRewriter(ParameterExpression stateVariable, ParameterExpression shouldBreakVariable, ParameterExpression nextStateOutVariable, ParameterExpression hasNextOutVariable, ParameterExpression shouldRunFinallyVariable, ParameterExpression resultTemporaryVariable, LabelTarget yieldBreakLabel, LabelTarget returnLabel, Dictionary<YieldReturnCSharpExpression, (int state, LabelTarget resumeLabel)> yieldReturnInfo, Dictionary<TryExpression, (LabelTarget label, List<(int state, LabelTarget label)> branches)> tryStatementInfo, HashSet<ParameterExpression> localsToHoist)
            {
                _stateVariable = stateVariable;
                _shouldBreakVariable = shouldBreakVariable;
                _nextStateOutVariable = nextStateOutVariable;
                _hasNextOutVariable = hasNextOutVariable;
                _shouldRunFinallyVariable = shouldRunFinallyVariable;
                _resultTemporaryVariable = resultTemporaryVariable;
                _returnLabel = returnLabel;
                _assignHasNextTrue = Expression.Assign(hasNextOutVariable, Expression.Constant(true));
                _assignShouldRunFinallyFalse = Expression.Assign(_shouldRunFinallyVariable, Expression.Constant(false));
                _gotoYieldBreak = Expression.Goto(yieldBreakLabel);
                _checkYieldBreak =
                    Expression.IfThen(
                        _shouldBreakVariable,
                        _gotoYieldBreak
                    );
                _yieldReturnInfo = yieldReturnInfo;
                _tryStatementInfo = tryStatementInfo;
                _localsToHoist = localsToHoist;
            }

            protected override Expression VisitBlock(BlockExpression node)
            {
                var expressions = Visit(node.Expressions).AsEnumerable();

                var hoistedVariables = node.Variables.Intersect(_localsToHoist).ToArray();

                if (hoistedVariables.Length > 0)
                {
                    var initVariables = hoistedVariables.Select(v => Expression.Assign(v, Expression.Default(v.Type)));
                    expressions = initVariables.Concat(expressions);
                }

                var newVariables = node.Variables.Except(_localsToHoist);

                return node.Update(newVariables, expressions);
            }

            protected override Expression VisitTry(TryExpression node)
            {
                var res = (TryExpression)base.VisitTry(node);

                if (_tryStatementInfo.TryGetValue(node, out var info))
                {
                    var (label, branches) = info;

                    var switchCases = branches.GroupBy(b => b.label).Select(g => Expression.SwitchCase(Expression.Goto(g.Key), g.Select(t => Expression.Constant(t.state)))).ToArray();

                    return
                        Expression.Block(
                            Expression.Label(label),
                            res.Update(
                                Expression.Block(
                                    Expression.Switch(
                                        _stateVariable,
                                        Expression.Empty(),
                                        switchCases
                                    ),
                                    res.Body
                                ),
                                res.Handlers,
                                res.Finally == null ? null :
                                Expression.IfThen(
                                    _shouldRunFinallyVariable,
                                    res.Finally
                                ),
                                res.Fault
                            )
                        );
                }

                return res;
            }

            protected internal override Expression VisitYieldBreak(YieldBreakCSharpExpression node) => _gotoYieldBreak;

            protected internal override Expression VisitYieldReturn(YieldReturnCSharpExpression node)
            {
                var (state, resumeLabel) = _yieldReturnInfo[node];

                return
                    Expression.Block(
                        typeof(void),
                        Expression.Assign(_resultTemporaryVariable, node.Value),
                        Expression.Assign(_nextStateOutVariable, Expression.Constant(state)),
                        _assignHasNextTrue,
                        _assignShouldRunFinallyFalse,
                        Expression.Return(_returnLabel, _resultTemporaryVariable),
                        Expression.Label(resumeLabel),
                        _checkYieldBreak
                    );
            }
        }
    }
}
