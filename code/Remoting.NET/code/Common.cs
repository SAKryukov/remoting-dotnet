// Remoting for .NET Core, .NET 5, and later
//
// Copyright (c) Sergey A Kryukov, 2017-2021
//
// http://www.SAKryukov.org
// http://www.codeproject.com/Members/SAKryukov
// https://github.com/SAKryukov
//

namespace Remoting {
    using System.Runtime.Serialization;
    using System.Reflection;
    using UniqueId = System.Int64;
    using Encoding = System.Text.Encoding;
    using MemoryStream = System.IO.MemoryStream;
    using TypeEnumerable = System.Collections.Generic.IEnumerable<System.Type>;
    using TypeList = System.Collections.Generic.List<System.Type>;
    using TypeSet = System.Collections.Generic.HashSet<System.Type>;

    public static class DefinitionSet {
        // IANA port numbers, IETF RFC6335:
        // https://www.iana.org/assignments/service-names-port-numbers/service-names-port-numbers.xhtml
        // https://tools.ietf.org/html/rfc6335
        public static class PortAssignmentsIANA {
            public static class SystemPorts {
                public const int First = 0;
                public const int Last = 0x400 - 1; //1023;
            } //class SystemPorts
            public static class UserPorts { // registered
                public const int First = 0x400; //1024;
                public const int Last = 0xC000 - 1; //49151;
            } //class UserPorts 
            public static class DynamicPrivatePorts { // "Dynamic and/or Private", use only those
                public const int First = 0xC000; //49152
                public const int Last = 0xFFFF; //65535;
            } //class DynamicPrivatePorts   
        } //PortAssignmentsIANA
        internal const string localHost = "localhost";
        internal static readonly string NullIndicator = string.Empty;
        internal static readonly string StopIndicator = "s";
        internal const string InterfaceMethodNotFoundIndicator = "?";
        internal static string FormatNotInterface(System.Type invalidInterface) =>
            $"{invalidInterface.FullName} is not an interface type";
        internal static string FormatBadInterfaceParameter(System.Type invalidInterface, MethodInfo badMethod, ParameterInfo badParameter) =>
            $"{invalidInterface.FullName}.{badMethod.Name}, parameter {badParameter.ParameterType.FullName} {badParameter.Name} is not an input parameter, cannot be serialized";
    } //class DefinitionSet

    public interface IDynamic {
        // not System.IDisposable.Dispose, used to remove from the dictionary of dynamic implementation objects objectIdDictionary:
        void Dispose() { }
    } //IDynamic

    [DataContract(Namespace = "r")]
    class MethodSchema {    
        MethodSchema() { }
        internal MethodSchema(string methodName, object[] actialParameters) {
            this.methodName = methodName;
            this.actualParameters = actialParameters;
        } //MethodSchema
        internal string MethodName { get { return methodName; } }
        internal object[] ActualParameters { get { return actualParameters; } }
        [DataMember(Name = "m")]
        internal string methodName;
        [DataMember(Name = "a")]
        internal object[] actualParameters;
    } //class MethodSchema

    [DataContract(Namespace = "r")]
    class DynamicParameterSchema {
        internal DynamicParameterSchema(UniqueId implementorId) { this.implementorId = implementorId; }
        internal UniqueId ImplementorId { get { return implementorId; } }
        [DataMember(Name = "i")]
        readonly UniqueId implementorId;
    } //DynamicParameterSchema

    static class Utility {
        class InvalidInterfaceException : System.ApplicationException {
            internal InvalidInterfaceException(System.Type invalidInterface) :
                base(DefinitionSet.FormatNotInterface(invalidInterface)) { }
            internal InvalidInterfaceException(System.Type invalidInterface, MethodInfo badMethod, ParameterInfo badParameter) :
                base (DefinitionSet.FormatBadInterfaceParameter(invalidInterface, badMethod, badParameter)) { }
        }; //class InvalidInterfaceException
        static void TraverseTypes(System.Type interfaceType, System.Action<System.Type, MethodInfo, ParameterInfo> action) {
            if (!interfaceType.IsInterface)
                throw new InvalidInterfaceException(interfaceType);
            var interfaces = interfaceType.GetInterfaces();
            foreach (var baseInterfaceType in interfaces)
                TraverseTypes(baseInterfaceType, action);
            TraverseMethods(interfaceType, action);
            static void TraverseMethods(System.Type type, System.Action<System.Type, MethodInfo, ParameterInfo> action) {
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var method in methods) {
                    var parameters = method.GetParameters();
                    foreach (var parameter in parameters) {
                        action(type, method, parameter);
                        if (parameter.ParameterType.IsInterface)
                            TraverseTypes(parameter.ParameterType, action);
                    } //loop
                    action(type, method, null);
                    if (method.ReturnType != typeof(void) && method.ReturnType.IsInterface)
                        TraverseTypes(method.ReturnType, action);
                } //loop methods
            } //AddMethods
        } //TraverseTypes
        internal static TypeEnumerable CollectKnownTypes(System.Type interfaceType) {
            TypeSet typeSet = new();
            typeSet.Add(typeof(DynamicParameterSchema));
            static void AddType(TypeSet typeSet, System.Type type) {
                if (type == typeof(void)) return;
                if (type.IsPrimitive) return;
                if (type == typeof(string)) return;
                if (!typeSet.Contains(type))
                    typeSet.Add(type);
            } //AddType
            TraverseTypes(interfaceType, (interfaceType, method, parameter) => {
                if (parameter == null) return;  
                if (!parameter.IsOut && !parameter.ParameterType.IsByRef)
                    AddType(typeSet, parameter.ParameterType);
                else
                    throw new InvalidInterfaceException(interfaceType, method, parameter);
            });
            TypeList result = new();
            CollectDynamicInterfaceTypes(interfaceType, typeSet);
            foreach (var value in typeSet) result.Add(value);
            return result;
        } //CollectKnownTypes
        internal static void CollectDynamicInterfaceTypes(System.Type interfaceType, TypeSet container) {
            var targetType = typeof(IDynamic);
            TraverseTypes(interfaceType, (interfaceType, method, parameter) => {
                System.Type parameterType = parameter == null ? method.ReturnType : parameter.ParameterType;
                if (parameterType == typeof(void)) return;
                if (!parameterType.IsInterface) return;
                if (targetType.IsAssignableFrom(parameterType))
                    container.Add(parameterType);
                var baseInterfaces = parameterType.GetInterfaces();
                foreach (var baseInterface in baseInterfaces)
                    CollectDynamicInterfaceTypes(baseInterface, container);
            });
        } //CollectDynamicInterfaceTypes
        internal static string ObjectToString(DataContractSerializer serializer, object graph) {
            using MemoryStream memoryStream = new();
            serializer.WriteObject(memoryStream, graph);
            return Encoding.UTF8.GetString(memoryStream.ToArray());
        } //ObjectToString
        internal static object StringToObject(DataContractSerializer serializer, string source) {
            using MemoryStream memoryStream = new(Encoding.UTF8.GetBytes(source));
            return serializer.ReadObject(memoryStream);
        } //StringToObject
    } //class Utility

}
