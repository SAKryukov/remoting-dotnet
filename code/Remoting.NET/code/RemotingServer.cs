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
    using ClientList = System.Collections.Generic.List<ClientWrapper>;
    using Dns = System.Net.Dns;
    using IPAddress = System.Net.IPAddress;
    using Thread = System.Threading.Thread;
    using MethodDictionary = System.Collections.Generic.Dictionary<string, System.Reflection.Emit.DynamicMethod>;
    using ManualResetEvent = System.Threading.ManualResetEvent;
    using Debug = System.Diagnostics.Debug;

    public class Server<CONTRACT, IMPLEMENTATION> where IMPLEMENTATION : CONTRACT, new() where CONTRACT : IContract {

        public Server(int port, IMPLEMENTATION implementor) {
            Debug.Assert(implementor != null);
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
                    var parameters = System.Array.ConvertAll(method.GetParameters(), new System.Converter<ParameterInfo, System.Type>(el => el.ParameterType));
                    var implementorMethod = implementorType.GetMethod( // see if there is an implicit interface method implementation, the priority in normal .NET dispatching:
                            method.Name,
                            BindingFlags.Public | BindingFlags.Instance,
                            null,
                            parameters,
                            null);
                    if (implementorMethod == null) // see if there is an explicit method implementation
                        implementorMethod = implementorType.GetMethod(
                            methodName,
                            BindingFlags.NonPublic | BindingFlags.Instance,
                            null,
                            parameters,
                            null);
                    Debug.Assert(implementorMethod != null);
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
            writer.WriteLine(DefinitionSet.StopIndicator);
            listener.Stop();
        } //Stop

        readonly Thread listeningThread;
        readonly Thread protocolThread;

        void ListenerThreadBody() {
            while (!doStop) {
                var client = listener.AcceptTcpClient();
                clientList.Add(new ClientWrapper(client));
                protocolStopper.Set();
            }
        }
        void ProtocolThreadBody() {
            while (!doStop) {
                protocolStopper.WaitOne();
                for (int index = clientList.Count - 1; index >= 0; --index) {
                    try {
                        ClientDialog(clientList[index]);
                        if (doStop) return;
                    } catch (System.Exception e) {
                        System.Console.WriteLine($"{e.GetType().FullName}: {e.Message}");
                        if (doStop) return;
                        var client = clientList[index];
                        clientList.RemoveAt(index);
                        ((System.IDisposable)client).Dispose();
                        if (clientList.Count < 1)
                            protocolStopper.Reset();
                    } //exception
                } //loop clients
                void ClientDialog(ClientWrapper wrapper) {
                    var requestLine = wrapper.reader.ReadLine();
                    if (requestLine == DefinitionSet.StopIndicator) {
                        doStop = true;
                        return;
                    } //if
                    string responseLine = GenerateResponse(requestLine);
                    wrapper.writer.WriteLine(responseLine);
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

    class ClientWrapper : System.IDisposable {
        internal ClientWrapper(TcpClient client) {
            this.client = client;
            stream = client.GetStream();
            reader = new(stream);
            writer = new(stream);
            writer.AutoFlush = true;
        }
        void System.IDisposable.Dispose() {
            writer.Dispose();
            reader.Dispose();
            stream.Dispose();
            client.Dispose();
        }
        internal readonly TcpClient client;
        internal readonly Stream stream;
        internal readonly StreamReader reader;
        internal readonly StreamWriter writer;
    } //class ClientWrapper

}
