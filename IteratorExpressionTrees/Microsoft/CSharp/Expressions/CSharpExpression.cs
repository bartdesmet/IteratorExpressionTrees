using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Microsoft.CSharp.Expressions
{
    public abstract class CSharpExpression : Expression
    {
        public override ExpressionType NodeType => ExpressionType.Extension;

        public static IteratorCSharpExpression<TDelegate> Iterator<TDelegate>(Expression body, params ParameterExpression[] parameters)
        {
            if (body == null)
                throw new ArgumentNullException(nameof(body));
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            return MakeIterator<TDelegate>(body, new ReadOnlyCollection<ParameterExpression>(parameters.ToArray()));
        }

        public static IteratorCSharpExpression<TDelegate> Iterator<TDelegate>(Expression body, IEnumerable<ParameterExpression> parameters)
        {
            if (body == null)
                throw new ArgumentNullException(nameof(body));
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            return MakeIterator<TDelegate>(body, new ReadOnlyCollection<ParameterExpression>(parameters.ToArray()));
        }

        private static IteratorCSharpExpression<TDelegate> MakeIterator<TDelegate>(Expression body, ReadOnlyCollection<ParameterExpression> parameters)
        {
            if (parameters.Contains(default))
                throw new ArgumentNullException(nameof(parameters));

            var delegateType = typeof(TDelegate);

            if (!typeof(Delegate).IsAssignableFrom(delegateType))
                throw new ArgumentException("Iterator type should be a delegate type.", nameof(TDelegate));
            if (body.Type != typeof(void))
                throw new ArgumentException("Type of body should be void.", nameof(body));
            if (parameters.Any(p => p.IsByRef))
                throw new ArgumentException("Parameter can't be by ref.", nameof(parameters));

            var invoke = delegateType.GetMethod("Invoke");

            var iterableType = invoke.ReturnType;

            var builderTypeAttribute = iterableType.GetCustomAttribute<IterableBuilderTypeAttribute>();
            if (builderTypeAttribute == null)
                throw new ArgumentException("Return type is not an iterable type. No builder type attribute found.", nameof(TDelegate));

            var builderType = builderTypeAttribute.Type;
            if (builderType == null)
                throw new ArgumentException("Return type is not an iterable type. No builder type found.", nameof(TDelegate));

            if (!builderType.IsClass)
                throw new ArgumentException("Return type is not an iterable type. Builder type should be a class.", nameof(TDelegate));
            if (!builderType.IsAbstract)
                throw new ArgumentException("Return type is not an iterable type. Builder type should be an abstract class.", nameof(TDelegate));

            var getEnumerator = iterableType.GetMethod("GetEnumerator", BindingFlags.Public | BindingFlags.Instance);
            if (getEnumerator == null)
                throw new ArgumentException("Return type is not an iterable type. GetEnumerator method not found.");

            var iteratorType = getEnumerator.ReturnType;

            if (getEnumerator.GetParameters().Length != 0 || iteratorType == typeof(void))
                throw new ArgumentException("Return type is not an iterable type. GetEnumerator method has invalid signature.");

            var moveNext = iteratorType.GetMethod("MoveNext", BindingFlags.Public | BindingFlags.Instance);
            if (moveNext == null)
                throw new ArgumentException("Return type is not an iterable type. MoveNext method not found.");

            if (moveNext.GetParameters().Length != 0 || moveNext.ReturnType != typeof(bool))
                throw new ArgumentException("Return type is not an iterable type. MoveNext method has invalid signature.");

            var current = iteratorType.GetProperty("Current", BindingFlags.Public | BindingFlags.Instance);
            if (current == null)
                throw new ArgumentException("Return type is not an iterable type. Current property not found.");
            if (current.GetIndexParameters().Length != 0)
                throw new ArgumentException("Return type is not an iterable type. Current property should not be an indexer.");
            if (current.GetGetMethod() == null)
                throw new ArgumentException("Return type is not an iterable type. Current property has no getter.");

            var elementType = current.PropertyType;

            var closedBuilderType = builderType;

            if (builderType.IsGenericTypeDefinition)
            {
                if (builderType.GetGenericArguments().Length != 1)
                    throw new ArgumentException("Return type is not an iterable type. A generic builder type should have a single type parameter.", nameof(TDelegate));

                closedBuilderType = builderType.MakeGenericType(elementType);
            }

            if (!iterableType.IsAssignableFrom(closedBuilderType))
                throw new ArgumentException("Return type is not compatible with builder type.", nameof(TDelegate));

            var tryGetNext = closedBuilderType.GetMethod("TryGetNext", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (tryGetNext == null)
                throw new ArgumentException("Return type is not an iterable type. TryGetNext method not found on builder type.", nameof(TDelegate));
            if (!tryGetNext.IsVirtual)
                throw new ArgumentException("Return type is not an iterable type. TryGetNext method on builder type is not virtual.", nameof(TDelegate));
            if (!elementType.IsAssignableFrom(tryGetNext.ReturnType))
                throw new ArgumentException("Return type is not an iterable type. TryGetNext method return type is not compatible with element type.", nameof(TDelegate));
            var tryGetNextParameters = tryGetNext.GetParameters();
            if (tryGetNextParameters.Length != 4 || tryGetNextParameters[0].ParameterType != typeof(int) || tryGetNextParameters[1].ParameterType != typeof(bool) || tryGetNextParameters[2].ParameterType != typeof(int).MakeByRefType() || tryGetNextParameters[3].ParameterType != typeof(bool).MakeByRefType())
                throw new ArgumentException("Return type is not an iterable type. TryGetNext method on builder type has invalid signature.", nameof(TDelegate));

            var clone = closedBuilderType.GetMethod("Clone", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (clone == null)
                throw new ArgumentException("Return type is not an iterable type. Clone method not found on builder type.", nameof(TDelegate));
            if (!clone.IsVirtual)
                throw new ArgumentException("Return type is not an iterable type. Clone method on builder type is not virtual.", nameof(TDelegate));
            if (!iterableType.IsAssignableFrom(clone.ReturnType))
                throw new ArgumentException("Return type is not an iterable type. Clone method return type is not compatible with iterable type.", nameof(TDelegate));
            if (clone.GetParameters().Length != 0)
                throw new ArgumentException("Return type is not an iterable type. Clone method on builder type has invalid signature.", nameof(TDelegate));

            var pars = invoke.GetParameters();

            if (pars.Length != parameters.Count)
                throw new ArgumentException("Parameter count doesn't match the delegate parameter count.", nameof(parameters));

            for (var i = 0; i < pars.Length; i++)
            {
                if (!pars[i].ParameterType.IsAssignableFrom(parameters[i].Type))
                    throw new ArgumentException("Parameter not compatible with corresponding delegate parameter.", nameof(parameters));
            }

            return new IteratorCSharpExpression<TDelegate>(new IteratorInfo(elementType, closedBuilderType), body, parameters);
        }

        public static IteratorCSharpExpression Iterator(Type delegateType, Expression body, params ParameterExpression[] parameters)
        {
            if (delegateType == null)
                throw new ArgumentNullException(nameof(delegateType));
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            var iteratorFactory = typeof(CSharpExpression).GetMethods(BindingFlags.NonPublic | BindingFlags.Static).Single(m => m.Name == nameof(MakeIterator) && m.IsGenericMethodDefinition);

            try
            {
                return (IteratorCSharpExpression)iteratorFactory.MakeGenericMethod(delegateType).Invoke(null, new object[] { body, new ReadOnlyCollection<ParameterExpression>(parameters.ToArray()) });
            }
            catch (TargetInvocationException tie)
            {
                throw tie.InnerException;
            }
        }

        public static YieldReturnCSharpExpression YieldReturn(Expression value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            if (value.Type == typeof(void))
                throw new ArgumentException("Type should not be void.", nameof(value));
            if (value.Type.IsPointer || value.Type.IsByRef || value.Type.IsGenericParameter)
                throw new ArgumentException("Type should not be a pointer, by reference, or generic parameter type.", nameof(value));

            return new YieldReturnCSharpExpression(value);
        }

        public static YieldBreakCSharpExpression YieldBreak() => new YieldBreakCSharpExpression();

        protected internal abstract Expression Accept(CSharpExpressionVisitor visitor);
    }
}
