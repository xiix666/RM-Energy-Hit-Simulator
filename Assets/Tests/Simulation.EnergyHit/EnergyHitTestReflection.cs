using System;
using System.Reflection;

namespace Simulation.EnergyHit.Tests
{
    /// <summary>
    /// Reflection helpers used to reach the <c>internal</c> types of
    /// <c>EnergyHitDebuggerWindow.cs</c> from the separate test assembly.
    /// The production assembly is left untouched (no InternalsVisibleTo).
    /// </summary>
    internal static class EnergyHitTestReflection
    {
        private const BindingFlags AnyStatic =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private const BindingFlags AnyInstance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>Resolves a type that lives in the same assembly as <see cref="EnergyHitStatistics"/>.</summary>
        public static Type GetType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, throwOnError: false);
                if (type != null) return type;
            }
            throw new InvalidOperationException($"Type {fullName} was not found in the production assembly.");
        }

        public static object GetStaticField(Type type, string fieldName)
        {
            FieldInfo field = type.GetField(fieldName, AnyStatic);
            if (field == null)
            {
                throw new MissingFieldException(type.FullName, fieldName);
            }

            return field.GetValue(null);
        }

        public static void SetStaticField(Type type, string fieldName, object value)
        {
            FieldInfo field = type.GetField(fieldName, AnyStatic);
            if (field == null)
            {
                throw new MissingFieldException(type.FullName, fieldName);
            }

            field.SetValue(null, value);
        }

        public static object InvokeStatic(Type type, string methodName, params object[] args)
        {
            MethodInfo method = type.GetMethod(methodName, AnyStatic);
            if (method == null)
            {
                throw new MissingMethodException(type.FullName, methodName);
            }

            return method.Invoke(null, args);
        }

        public static MethodInfo GetStaticMethod(Type type, string methodName)
        {
            MethodInfo method = type.GetMethod(methodName, AnyStatic);
            if (method == null)
            {
                throw new MissingMethodException(type.FullName, methodName);
            }

            return method;
        }

        public static void SetInstanceField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, AnyInstance);
            if (field == null)
            {
                throw new MissingFieldException(target.GetType().FullName, fieldName);
            }

            field.SetValue(target, value);
        }
    }
}
