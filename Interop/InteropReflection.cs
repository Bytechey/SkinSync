// SkinSync Interop
// Copyright (c) 2026 Bytechey
// SPDX-License-Identifier: MIT

using System;
using System.Reflection;

namespace SkinSyncMod.Interop
{
    /// <summary>反射工具箱：按名解析类型与成员、剥掉 by-ref 匹配、安全调用。</summary>
    internal static class InteropReflection
    {
        private const BindingFlags AllMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        /// <summary>剥掉 ref/in/out 修饰，返回形参的元素类型。</summary>
        internal static Type StripByRef(Type type)
        {
            return type != null && type.IsByRef ? type.GetElementType() : type;
        }

        /// <summary>在指定程序集内按全名取类型；找不到返回 null。</summary>
        internal static Type FindType(Assembly assembly, string fullName)
        {
            if (assembly == null || string.IsNullOrEmpty(fullName))
            {
                return null;
            }

            try
            {
                return assembly.GetType(fullName, false, false);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>按「名称 + 形参个数 + 剥掉 by-ref 后的元素类型」查找方法，返回第一个匹配项。</summary>
        /// <remarks>形参类型传 null 表示该位不参与匹配，用于形参类型必须从目标方法本身取得的场景。</remarks>
        internal static MethodInfo FindMethod(Type type, string name, params Type[] parameterTypes)
        {
            if (type == null || string.IsNullOrEmpty(name) || parameterTypes == null)
            {
                return null;
            }

            MethodInfo[] methods = type.GetMethods(AllMembers);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.Name != name || method.IsGenericMethodDefinition)
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != parameterTypes.Length)
                {
                    continue;
                }

                bool matched = true;
                for (int p = 0; p < parameters.Length; p++)
                {
                    Type wanted = parameterTypes[p];
                    if (wanted != null && StripByRef(parameters[p].ParameterType) != wanted)
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                {
                    return method;
                }
            }

            return null;
        }

        /// <summary>按「名称 + 形参个数」查找方法，返回第一个匹配项。</summary>
        /// <remarks>仅用于形参类型必须从目标方法本身取得的成员，例如接收器登记方法的委托形参。</remarks>
        internal static MethodInfo FindMethodByCount(Type type, string name, int parameterCount)
        {
            if (type == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            MethodInfo[] methods = type.GetMethods(AllMembers);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.Name != name || method.IsGenericMethodDefinition)
                {
                    continue;
                }

                if (method.GetParameters().Length == parameterCount)
                {
                    return method;
                }
            }

            return null;
        }

        /// <summary>按实参形状查找方法：名称、静态性、形参个数与剥掉 by-ref 后的元素类型都要匹配。</summary>
        /// <remarks>实参为 null 时只要求形参不是非可空值类型；out 形参允许传 null 占位。</remarks>
        internal static MethodInfo FindMethodByArgs(Type type, string name, bool staticOnly, object[] args)
        {
            if (type == null || string.IsNullOrEmpty(name) || args == null)
            {
                return null;
            }

            MethodInfo[] methods = type.GetMethods(AllMembers);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.Name != name || method.IsGenericMethodDefinition || method.IsStatic != staticOnly)
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != args.Length)
                {
                    continue;
                }

                bool matched = true;
                for (int p = 0; p < parameters.Length; p++)
                {
                    Type parameterType = StripByRef(parameters[p].ParameterType);
                    object argument = args[p];
                    if (argument == null)
                    {
                        if (parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) == null)
                        {
                            matched = false;
                            break;
                        }

                        continue;
                    }

                    if (!parameterType.IsInstanceOfType(argument))
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                {
                    return method;
                }
            }

            return null;
        }

        /// <summary>按形参元素类型查找公开实例构造函数，找不到返回 null。</summary>
        internal static ConstructorInfo FindConstructor(Type type, params Type[] parameterTypes)
        {
            if (type == null || parameterTypes == null)
            {
                return null;
            }

            ConstructorInfo[] constructors = type.GetConstructors(AllMembers);
            for (int i = 0; i < constructors.Length; i++)
            {
                ParameterInfo[] parameters = constructors[i].GetParameters();
                if (parameters.Length != parameterTypes.Length)
                {
                    continue;
                }

                bool matched = true;
                for (int p = 0; p < parameters.Length; p++)
                {
                    if (StripByRef(parameters[p].ParameterType) != parameterTypes[p])
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                {
                    return constructors[i];
                }
            }

            return null;
        }

        /// <summary>按名查找字段，找不到或匹配歧义时返回 null。</summary>
        /// <remarks>派生类用 new 隐藏基类同名字段时 GetField 抛 AmbiguousMatchException，与 <see cref="FindType"/> 一致按「找不到」处理。</remarks>
        internal static FieldInfo FindField(Type type, string name)
        {
            if (type == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            try
            {
                return type.GetField(name, AllMembers);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>按名查找属性，找不到返回 null。</summary>
        internal static PropertyInfo FindProperty(Type type, string name)
        {
            if (type == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            PropertyInfo[] properties = type.GetProperties(AllMembers);
            for (int i = 0; i < properties.Length; i++)
            {
                if (properties[i].Name == name)
                {
                    return properties[i];
                }
            }

            return null;
        }

        /// <summary>按名查找事件，找不到或匹配歧义时返回 null。</summary>
        /// <remarks>派生类用 new 隐藏基类同名事件时 GetEvent 抛 AmbiguousMatchException，与 <see cref="FindType"/> 一致按「找不到」处理。</remarks>
        internal static EventInfo FindEvent(Type type, string name)
        {
            if (type == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            try
            {
                return type.GetEvent(name, AllMembers);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>读取字段或属性；成员缺失或目标抛异常时返回 false 且 value 为 null。</summary>
        internal static bool TryReadMember(MemberInfo member, object instance, out object value)
        {
            value = null;
            if (member == null)
            {
                return false;
            }

            try
            {
                if (member is PropertyInfo property)
                {
                    value = property.GetValue(instance, null);
                    return true;
                }

                if (member is FieldInfo field)
                {
                    value = field.GetValue(instance);
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }

        /// <summary>写入字段或属性；成员缺失或目标抛异常时返回 false。</summary>
        internal static bool TryWriteMember(MemberInfo member, object instance, object value)
        {
            if (member == null)
            {
                return false;
            }

            try
            {
                if (member is PropertyInfo property)
                {
                    property.SetValue(instance, value, null);
                    return true;
                }

                if (member is FieldInfo field)
                {
                    field.SetValue(instance, value);
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }

        /// <summary>读取字段或属性；成员缺失或目标抛异常时返回 null。</summary>
        internal static object ReadMember(MemberInfo member, object instance)
        {
            try
            {
                if (member is PropertyInfo property)
                {
                    return property.GetValue(instance, null);
                }

                if (member is FieldInfo field)
                {
                    return field.GetValue(instance);
                }
            }
            catch (Exception)
            {
                return null;
            }

            return null;
        }

        /// <summary>安全调用方法；成员缺失或目标抛异常时返回 false 且 result 为 null。</summary>
        /// <remarks>by-ref 形参的读回值留在传入的 args 数组里，调用方从同一数组取。</remarks>
        internal static bool TryInvoke(MethodInfo method, object instance, object[] args, out object result)
        {
            result = null;
            if (method == null)
            {
                return false;
            }

            try
            {
                result = method.Invoke(instance, args);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>安全构造实例；构造函数缺失或目标抛异常时返回 false 且 instance 为 null。</summary>
        internal static bool TryConstruct(ConstructorInfo constructor, object[] args, out object instance)
        {
            instance = null;
            if (constructor == null)
            {
                return false;
            }

            try
            {
                instance = constructor.Invoke(args);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>按枚举成员名取枚举值；类型或成员缺失时返回 null。</summary>
        internal static object EnumValue(Type enumType, string name)
        {
            if (enumType == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            try
            {
                return Enum.Parse(enumType, name, false);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>把实例方法绑定为目标委托类型的委托；签名不兼容时返回 null。</summary>
        internal static Delegate BindDelegate(Type delegateType, object target, MethodInfo method)
        {
            if (delegateType == null || target == null || method == null)
            {
                return null;
            }

            try
            {
                return Delegate.CreateDelegate(delegateType, target, method, false);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
