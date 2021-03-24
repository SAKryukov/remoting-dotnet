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
            public static class UserPorts { //registered
                public const int First = 0x400; //1024;
                public const int Last = 0xC000 - 1; //49151;
            } //class UserPorts 
            public static class DynamicPrivatePorts { // "Dynamic and/or Private", use only those
                public const int First = 0xC000; //49152
                public const int Last = 0xFFFF; //65535;
            } //class DynamicPrivatePorts   
        } //PortAssignmentsIANA
        internal static readonly string NullIndicator = string.Empty;
        internal static readonly string StopIndicator = "s";
        internal const string InterfaceMethodNotFoundIndicator = "?";
        internal static string FormatBadInterfaceParameter(System.Type invalidInterface, MethodInfo badMethod, ParameterInfo badParameter) =>
            $"{invalidInterface.FullName}.{badMethod.Name}, parameter {badParameter.ParameterType.FullName} {badParameter.Name} is not an input parameter, cannot be serialized";
    } //class DefinitionSet

    public interface IContract { }

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

    
    static class Utility {
        class InvalidInterfaceException : System.ApplicationException {
            internal InvalidInterfaceException(System.Type invalidInterface, MethodInfo badMethod, ParameterInfo badParameter) :
                base (DefinitionSet.FormatBadInterfaceParameter(invalidInterface, badMethod, badParameter)) { }
        };
        internal static TypeEnumerable CollectKnownTypes(System.Type interfaceType) {
            TypeSet typeSet = new();
            var interfaces = interfaceType.GetInterfaces();
            foreach (var parentInterfaceType in interfaces)
                AddMethods(parentInterfaceType, typeSet);
            AddMethods(interfaceType, typeSet);
            TypeList result = new();
            foreach (var value in typeSet) result.Add(value);
            return result;
            static void AddType(TypeSet typeSet, System.Type type) {
                if (type == typeof(void)) return;
                if (type.IsPrimitive) return;
                if (type == typeof(string)) return;
                if (!typeSet.Contains(type))
                    typeSet.Add(type);
            } //AddType
            static void AddMethods(System.Type type, TypeSet typeSet) {
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var method in methods) {
                    var parameters = method.GetParameters();
                    foreach (var parameter in parameters)
                        if (!parameter.IsOut && !parameter.ParameterType.IsByRef)
                            AddType(typeSet, parameter.ParameterType);
                        else
                            throw new InvalidInterfaceException(type, method, parameter);
                    AddType(typeSet, method.ReturnType);
                } //loop methods
            } //AddMethods
        } //CollectKnownTypes
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
