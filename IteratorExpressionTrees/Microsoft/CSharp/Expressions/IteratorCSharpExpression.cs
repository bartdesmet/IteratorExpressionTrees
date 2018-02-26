using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace Microsoft.CSharp.Expressions
{
    public abstract class IteratorCSharpExpression : CSharpExpression
    {
        private protected IteratorCSharpExpression(IteratorInfo iteratorInfo, Expression body, ReadOnlyCollection<ParameterExpression> parameters)
        {
            IteratorInfo = iteratorInfo;
            Body = body;
            Parameters = parameters;
        }

        public override bool CanReduce => true;

        public IteratorInfo IteratorInfo { get; }
        public Expression Body { get; }
        public ReadOnlyCollection<ParameterExpression> Parameters { get; }

        public override Expression Reduce() => ReduceCore();

        protected abstract LambdaExpression ReduceCore();

        private static ModuleBuilder s_mb;
        private static readonly ConditionalWeakTable<Type, Type> s_runtimeBuilderTypes = new ConditionalWeakTable<Type, Type>();

        protected static Type GetRuntimeIteratorBuilderType(Type builderType)
        {
            if (builderType.IsGenericType)
            {
                if (builderType.IsGenericTypeDefinition)
                {
                    throw new InvalidOperationException();
                }

                if (s_runtimeBuilderTypes.TryGetValue(builderType.GetGenericTypeDefinition(), out var runtimeBuilderType))
                {
                    return runtimeBuilderType.MakeGenericType(builderType.GetGenericArguments());
                }
            }
            else
            {
                if (s_runtimeBuilderTypes.TryGetValue(builderType, out var runtimeBuilderType))
                {
                    return runtimeBuilderType;
                }
            }

            return CreateRuntimeIteratorBuilderType(builderType);
        }

        protected static Type CreateRuntimeIteratorBuilderType(Type builderType)
        {
            if (s_mb == null)
            {
                s_mb = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("__RuntimeCompiler"), AssemblyBuilderAccess.RunAndCollect).DefineDynamicModule("__Iterators");
            }

            //
            // class RB<T> : B<T>
            // {
            //     private readonly Func<TryGetNext<T>> _tryGetNextFactory;
            //     private readonly TryGetNext<T> _tryGetNext;
            //
            //     public RB<T>(Func<TryGetNext<T>> tryGetNextFactory)
            //     {
            //         _tryGetNextFactory = tryGetNextFactory;
            //         _tryGetNext = tryGetNextFactory();
            //     }
            //
            //     public override I<T> Clone() => new RB<T>(_tryGetNextFactory);
            //
            //     public override T TryGetNext(...) => _tryGetNext(...);
            // }
            //

            Type runtimeBuilderType;

            TypeBuilder tb = s_mb.DefineType("__Iterator", TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed);

            if (builderType.IsGenericType)
            {
                var def = builderType.GetGenericTypeDefinition();
                var args = builderType.GetGenericArguments();

                if (args.Length != 1)
                {
                    throw new InvalidOperationException();
                }

                var genPar = tb.DefineGenericParameters(args[0].Name)[0];

                var closedGenericBuilderType = def.MakeGenericType(genPar);

                tb.SetParent(closedGenericBuilderType);

                var tryGetNextType = typeof(TryGetNext<>).MakeGenericType(args);
                var tryGetNextFactoryType = typeof(Func<>).MakeGenericType(tryGetNextType);

                var tryGetNextField = tb.DefineField("_tryGetNext", tryGetNextType, FieldAttributes.Private | FieldAttributes.InitOnly);
                var tryGetNextFactoryField = tb.DefineField("_tryGetNextFactory", tryGetNextFactoryType, FieldAttributes.Private | FieldAttributes.InitOnly);

                var ctorArgs = new[] { tryGetNextFactoryType };
                var ctor = tb.DefineConstructor(MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, CallingConventions.Standard, ctorArgs);
                var ctorBase = TypeBuilder.GetConstructor(closedGenericBuilderType, def.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Single());
                var ctorIL = ctor.GetILGenerator();
                ctorIL.Emit(OpCodes.Ldarg_0);
                ctorIL.Emit(OpCodes.Call, ctorBase);
                ctorIL.Emit(OpCodes.Ldarg_0);
                ctorIL.Emit(OpCodes.Ldarg_1);
                ctorIL.Emit(OpCodes.Stfld, tryGetNextFactoryField);
                ctorIL.Emit(OpCodes.Ldarg_0);
                ctorIL.Emit(OpCodes.Ldarg_1);
                ctorIL.EmitCall(OpCodes.Callvirt, tryGetNextFactoryType.GetMethod("Invoke"), null);
                ctorIL.Emit(OpCodes.Stfld, tryGetNextField);
                ctorIL.Emit(OpCodes.Ret);

                var cloneBase = TypeBuilder.GetMethod(closedGenericBuilderType, def.GetMethod("Clone", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));
                var clone = tb.DefineMethod("Clone", (cloneBase.Attributes & MethodAttributes.MemberAccessMask) | MethodAttributes.HideBySig | MethodAttributes.Virtual, CallingConventions.HasThis, cloneBase.ReturnType, Type.EmptyTypes);
                var cloneIL = clone.GetILGenerator();
                cloneIL.Emit(OpCodes.Ldarg_0);
                cloneIL.Emit(OpCodes.Ldfld, tryGetNextFactoryField);
                cloneIL.Emit(OpCodes.Newobj, tb);
                cloneIL.Emit(OpCodes.Ret);

                var tryGetNextBase = TypeBuilder.GetMethod(closedGenericBuilderType, def.GetMethod("TryGetNext", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));
                var tryGetNext = tb.DefineMethod("TryGetNext", (tryGetNextBase.Attributes & MethodAttributes.MemberAccessMask) | MethodAttributes.HideBySig | MethodAttributes.Virtual, CallingConventions.HasThis, tryGetNextBase.ReturnType, tryGetNextBase.GetParameters().Select(p => p.ParameterType).ToArray());
                var tryGetNextIL = tryGetNext.GetILGenerator();
                tryGetNextIL.Emit(OpCodes.Ldarg_0);
                tryGetNextIL.Emit(OpCodes.Ldfld, tryGetNextField);
                tryGetNextIL.Emit(OpCodes.Ldarg_1);
                tryGetNextIL.Emit(OpCodes.Ldarg_2);
                tryGetNextIL.Emit(OpCodes.Ldarg_3);
                tryGetNextIL.Emit(OpCodes.Ldarg, 4);
                tryGetNextIL.EmitCall(OpCodes.Callvirt, tryGetNextType.GetMethod("Invoke"), null);
                tryGetNextIL.Emit(OpCodes.Ret);

                var runtimeBuilderTypeDef = s_runtimeBuilderTypes.GetValue(def, _ => tb.CreateType());
                runtimeBuilderType = runtimeBuilderTypeDef.MakeGenericType(args);
            }
            else
            {
                throw new NotImplementedException();
            }

            return runtimeBuilderType;
        }
    }
}
