#nullable enable

namespace NServiceBus;

using System;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;

static class MethodInfoExtensions
{
    extension(MethodInfo method)
    {
        public T? InvokeGeneric<T>(object? target, object?[]? args, Type[] genericTypes) => (T?)method.InvokeGeneric(target, args, genericTypes);

        public T? InvokeGeneric<T>(object?[]? args, Type[] genericTypes) => (T?)method.InvokeGeneric(null, args, genericTypes);

        public T? InvokeGeneric<T>(Type genericType) => (T?)method.InvokeGeneric(null, null, [genericType]);

        public object? InvokeGeneric(object? target, Type[] genericTypes) => method.InvokeGeneric(target, null, genericTypes);

        public object? InvokeGeneric(object? target, object?[]? args, Type[] genericTypes)
        {
            try
            {
                // Enter the contextual reflection scope of the first generic type's assembly
                // to ensure any assembly loading during MakeGenericMethod/Invoke happens
                // in the correct AssemblyLoadContext (not the Default ALC)
                var alc = AssemblyLoadContext.GetLoadContext(genericTypes[0].Assembly);
                using (alc?.EnterContextualReflection())
                {
                    return method.MakeGenericMethod(genericTypes).Invoke(target, args);
                }
            }
            catch (TargetInvocationException e)
            {
                if (e.InnerException != null)
                {
                    ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                }

                var genericParameters = string.Join(",", genericTypes.Select(t => t.Name));
                throw new Exception($"Failed to invoke {method.Name}<{genericParameters}> using reflection", e);
            }
        }
    }
}