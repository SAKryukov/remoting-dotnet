// Remoting for .NET Core, .NET 5, and later
//
// Copyright (c) Sergey A Kryukov, 2017-2021
//
// http://www.SAKryukov.org
// http://www.codeproject.com/Members/SAKryukov
// https://github.com/SAKryukov
//

namespace Remoting {
    using System.Reflection;
    using System.Reflection.Emit;
    using Object = System.Object;
    using Stream = System.IO.Stream;
    using DataContractSerializer = System.Runtime.Serialization.DataContractSerializer;
    using TcpListener = System.Net.Sockets.TcpListener;
    using TcpClient = System.Net.Sockets.TcpClient;
    using StreamReader = System.IO.StreamReader;
    using StreamWriter = System.IO.StreamWriter;
    using ClientList = System.Collections.Generic.List<System.Net.Sockets.TcpClient>;
    using Dns = System.Net.Dns;
    using IPAddress = System.Net.IPAddress;
    using Thread = System.Threading.Thread;
    using MethodDictionary = System.Collections.Generic.Dictionary<string, System.Reflection.Emit.DynamicMethod>;
    using ManualResetEvent = System.Threading.ManualResetEvent;

    public class Server<CONTRACT, IMPLEMENTATION> where IMPLEMENTATION : CONTRACT where CONTRACT : IContract {

        public Server(int port, IMPLEMENTATION implementor) {
            methodDictionary = new();
            this.implementor = implementor;
            AddCallers(methodDictionary, implementor.GetType(), typeof(CONTRACT));
            localIpAddress = Dns.GetHostEntry("localhost").AddressList[0];
            this.port = port;
            listener = new(localIpAddress, port);
            listeningThread = new(ListenerThreadBody);
            protocolThread = new(ProtocolThreadBody);
            static DynamicMethod CreateCaller(System.Type implementor, MethodInfo method) {
                var parameterInfo = method.GetParameters();
                System.Type[] parameters = new System.Type[parameterInfo.Length + 1];
                parameters[0] = implementor;
                for (var index = 1; index < parameters.Length; ++index)
                    parameters[index] = parameterInfo[index - 1].ParameterType;
                DynamicMethod result = new($"{method.DeclaringType.FullName}.{method.Name}", method.ReturnType, parameters);
                var generator = result.GetILGenerator();
                for (var index = 0; index < parameters.Length; ++index)
                    generator.Emit(OpCodes.Ldarg_S, index);
                generator.Emit(OpCodes.Call, method);
                generator.Emit(OpCodes.Ret);
                return result;
            } //CreateCaller
            static void AddCallers(MethodDictionary dictionary, System.Type implementorType, System.Type interfaceType) {
                var interfaces = interfaceType.GetInterfaces();
                foreach (var parentInterfaceType in interfaces)
                    AddCallers(dictionary, implementorType, parentInterfaceType);
                System.Type type = interfaceType;
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var method in methods) {
                    var parameterInfo = method.GetParameters();
                    var methodName = $"{interfaceType.FullName}.{method.Name}";
                    var implementorMethod = implementorType.GetMethod(method.Name);
                    if (implementorMethod == null)
                        implementorMethod = implementorType.GetMethod(
                            methodName,
                            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance,
                            null,
                            System.Array.ConvertAll(method.GetParameters(), new System.Converter<ParameterInfo, System.Type>(el => el.ParameterType)),
                            null);
                    dictionary.Add(method.ToString(), CreateCaller(implementorType, implementorMethod));
                } //loop
            } //AddCallers
        } //Server

        public void Start() {
            listener.Start();
            listeningThread.Start();
            protocolThread.Start();
        } //Start

        public void Stop() {
            doStop = true;
            using TcpClient stopper = new();
            stopper.Connect(localIpAddress, port);
            var stream = stopper.GetStream();
            StreamWriter writer = new(stream);
            writer.AutoFlush = true;
            writer.WriteLine(string.Empty);
        } //Stop

        readonly Thread listeningThread;
        readonly Thread protocolThread;

        void ListenerThreadBody() {
            while (!doStop) {
                var client = listener.AcceptTcpClient();
                clientList.Add(client);
                protocolStopper.Set();
            }
        }
        void ProtocolThreadBody() {
            while (!doStop) {
                protocolStopper.WaitOne();
                for (int index = clientList.Count - 1; index >= 0; --index) {
                    try {
                        var client = clientList[index];
                        ClientDialog(client);
                    } catch(System.Exception) {
                        if (doStop) return;
                        var client = clientList[index];
                        clientList.RemoveAt(index);
                        client.Dispose();
                        if (clientList.Count < 1)
                            protocolStopper.Reset();
                    } //exception
                } //loop clients
                void ClientDialog(TcpClient client) {
                    using Stream stream = client.GetStream();
                    using StreamReader reader = new(stream);
                    using StreamWriter writer = new(stream);
                    var requestLine = reader.ReadLine();
                    string responseLine = GenerateResponse(requestLine);
                    writer.WriteLine(responseLine);
                } //ClientDialog
            } //infinite loop
        } //ProtocolThreadBody

        string GenerateResponse(string request) {
            var callRequest = (MethodSchema)Utility.StringToObject(serializer, request);
            var dynamicMethod = methodDictionary[callRequest.MethodName]; // the heart of the remote procedure call
            if (dynamicMethod == null)
                return DefinitionSet.InterfaceMethodNotFoundIndicator;
            var allParameters = new Object[callRequest.actualParameters.Length + 1];
            callRequest.actualParameters.CopyTo(allParameters, 1);
            allParameters[0] = implementor; // plays the role of "this"
            Object response = dynamicMethod.Invoke(null, allParameters); // the heart of the remote procedure call
            if (response == null)
                return DefinitionSet.NullIndicator;
            DataContractSerializer responseSerializer = new(response.GetType());
            return Utility.ObjectToString(responseSerializer, response);
        } //GenerateResponse

        readonly IMPLEMENTATION implementor;
        readonly DataContractSerializer serializer = new(typeof(MethodSchema));
        readonly TcpListener listener;
        readonly ClientList clientList = new();
        readonly IPAddress localIpAddress;
        readonly int port;
        readonly MethodDictionary methodDictionary;
        readonly ManualResetEvent protocolStopper = new(false);
        bool doStop = false;
    } //class Server

}
