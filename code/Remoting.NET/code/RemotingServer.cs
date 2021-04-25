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
    //using UniqueId = System.Int64; //SA???
    using ObjectIDGenerator = System.Runtime.Serialization.ObjectIDGenerator;
    using ObjectIdDictionary = System.Collections.Generic.Dictionary<System.Int64, object>;
    using DataContractSerializer = System.Runtime.Serialization.DataContractSerializer;
    using TcpListener = System.Net.Sockets.TcpListener;
    using TcpClient = System.Net.Sockets.TcpClient;
    using Stream = System.IO.Stream;
    using StreamReader = System.IO.StreamReader;
    using StreamWriter = System.IO.StreamWriter;
    using ClientList = System.Collections.Generic.List<ClientWrapper>;
    using Dns = System.Net.Dns;
    using IPAddress = System.Net.IPAddress;
    using Thread = System.Threading.Thread;
    using MethodDictionary = System.Collections.Generic.Dictionary<string, System.Reflection.Emit.DynamicMethod>;
    using ManualResetEvent = System.Threading.ManualResetEvent;
    using Debug = System.Diagnostics.Debug;

    public partial class Server<CONTRACT, IMPLEMENTATION> where IMPLEMENTATION : CONTRACT, new() where CONTRACT : class {

        public Server(int port, IMPLEMENTATION implementor) {
            Debug.Assert(implementor != null);
            ExecutionPhaseChanged?.Invoke(this, new ExecutionPhaseEventArgs(ExecutionPhase.ReflectionStarted));
            var knownTypes = Utility.CollectKnownTypes(typeof(CONTRACT));
            serializer = new(typeof(MethodSchema), knownTypes);
            methodDictionary = new();
            CreateImplementingDynamicMethods(implementor);
            ExecutionPhaseChanged?.Invoke(this, new ExecutionPhaseEventArgs(ExecutionPhase.ReflectionComplete));
            this.implementor = implementor;
            this.port = port;
            localIpAddress = Dns.GetHostEntry(DefinitionSet.localHost).AddressList[0];
            listener = new(localIpAddress, port);
            listeningThread = new(ListenerThreadBody);
            protocolThread = new(ProtocolThreadBody);
        } //Server

        public void Start() {
            listener.Start();
            listeningThread.Start();
            protocolThread.Start();
            ExecutionPhaseChanged?.Invoke(this, new ExecutionPhaseEventArgs(ExecutionPhase.Started));
        } //Start

        public void Stop() {
            RequestStop();
            using TcpClient stopper = new();
            stopper.Connect(localIpAddress, port);
            var stream = stopper.GetStream();
            StreamWriter writer = new(stream);
            writer.AutoFlush = true;
            writer.WriteLine(DefinitionSet.StopIndicator);
            listener.Stop();
            ExecutionPhaseChanged?.Invoke(this, new ExecutionPhaseEventArgs(ExecutionPhase.Stopped));
        } //Stop

        #region events

        public class ConnectionStatusEventArgs : System.EventArgs {
            internal ConnectionStatusEventArgs(int clientCount) { ClientCount = clientCount; }
            public int ClientCount { get; private init; }
        }
        public class PhaseStatusEventArgs : System.EventArgs {
            internal PhaseStatusEventArgs(int clientCount) { ClientCount = clientCount; }
            public int ClientCount { get; private init; }
        }
        public System.EventHandler<ConnectionStatusEventArgs> Connected, Disconnected;

        public enum ExecutionPhase { ReflectionStarted, ReflectionComplete, Started, StopRequested, Stopped, Failed }
        public class ExecutionPhaseEventArgs : System.EventArgs {
            internal ExecutionPhaseEventArgs(ExecutionPhase phase) { Phase = phase; }
            public ExecutionPhase Phase { get; private init; }
        }
        public System.EventHandler<ExecutionPhaseEventArgs> ExecutionPhaseChanged;

        #endregion events

    } //class Server

    #region implementation

    public partial class Server<CONTRACT, IMPLEMENTATION> {

        static void CreateImplementingDynamicMethods(System.Type interfaceType, object implementor, MethodDictionary methodDictionary) {
            AddCallers(methodDictionary, implementor.GetType(), interfaceType);
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
                generator.Emit(method.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, method);
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
        } //CreateImplementingDynamicMethods

        void CreateImplementingDynamicMethods(IMPLEMENTATION implementor) {
            CreateImplementingDynamicMethods(typeof(CONTRACT), implementor, methodDictionary);
        } //CreateImplementingDynamicMethods

        void ListenerThreadBody() {
            while (!doStop) {
                var client = listener.AcceptTcpClient();
                lock (lockObject) {
                    clientList.Add(new ClientWrapper(client));
                    Connected?.Invoke(this, new ConnectionStatusEventArgs(clientList.Count));
                } //lock
                protocolStopper.Set();
            }
        } // ExecutionPhaseChanged

        readonly object lockObject = new();

        void ProtocolThreadBody() {
            while (!doStop) {
                int index = 0;
                lock (lockObject)
                    index = ChooseNextClient();
                protocolStopper.WaitOne();
                try {
                    lock (lockObject) {
                        if (clientList.Count <= index) break;
                        ClientDialog(clientList[index]);
                    } //lock
                    if (doStop) return;
                } catch (System.Exception) {
                    if (doStop) return;
                    ClientWrapper client = null;
                    lock (lockObject) {
                        client = clientList[index];
                        clientList.RemoveAt(index);
                    } //lock
                    ((System.IDisposable)client).Dispose();
                    lock (lockObject) {
                        if (clientList.Count < 1)
                            protocolStopper.Reset();
                        Disconnected?.Invoke(this, new ConnectionStatusEventArgs(clientList.Count));
                    } //lock
                } //exception
            } //infinite loop
            int ChooseNextClient() {
                if (clientList.Count > 1) {
                    var min = int.MaxValue;
                    var winnerIndex = 0;
                    for (int index = 0; index < clientList.Count; ++index) {
                        var serviceCount = clientList[index].serviceCount;
                        if (serviceCount < 1) return 0;
                        if (serviceCount < min) {
                            min = serviceCount;
                            winnerIndex = index;
                        } //if
                    } //loop
                    if (min > int.MaxValue / 2) Normalize(min);
                    return winnerIndex;
                    void Normalize(int min) {
                        foreach (var wrapper in clientList) wrapper.serviceCount -= min;
                    } //Normalize
                } else if (clientList.Count < 1)
                    protocolStopper.Reset();
                return 0;
            } //ChooseNextUser
            void ClientDialog(ClientWrapper wrapper) {
                var requestLine = wrapper.reader.ReadLine();
                if (requestLine == DefinitionSet.StopIndicator) {
                    RequestStop();
                    return;
                } //if
                string responseLine = GenerateResponse(requestLine);
                wrapper.writer.WriteLine(responseLine);
            } //ClientDialog
        } //ProtocolThreadBody

        string GenerateResponse(string request) {
            var callRequest = (MethodSchema)Utility.StringToObject(serializer, request);
            var dynamicMethod = methodDictionary[callRequest.MethodName]; // the heart of the remote procedure call
            if (dynamicMethod == null) {
                ExecutionPhaseChanged?.Invoke(this, new ExecutionPhaseEventArgs(ExecutionPhase.Failed));
                return DefinitionSet.InterfaceMethodNotFoundIndicator;
            } //if
            var allParameters = new Object[callRequest.actualParameters.Length + 1];
            callRequest.actualParameters.CopyTo(allParameters, 1);
            allParameters[0] = implementor; // plays the role of "this"
            Object response = dynamicMethod.Invoke(null, allParameters); // the heart of the remote procedure call
            if (response == null)
                return DefinitionSet.NullIndicator;
            DataContractSerializer responseSerializer = new(response.GetType());
            if (response.GetType().IsAssignableTo(typeof(IDynamic))) {
                var id = idGenerator.GetId(response, out bool firstTime);
                if (firstTime)
                    objectIdDictionary.Add(id, response);
                return id.ToString();
                //SA???
            } //if
            return Utility.ObjectToString(responseSerializer, response);
        } //GenerateResponse

        bool doStop = false;
        void RequestStop() {
            doStop = true;
            ExecutionPhaseChanged?.Invoke(this, new ExecutionPhaseEventArgs(ExecutionPhase.StopRequested));
        } //RequestStop

        readonly ObjectIDGenerator idGenerator = new();
        readonly ObjectIdDictionary objectIdDictionary = new();
        readonly Thread listeningThread;
        readonly Thread protocolThread;
        readonly IMPLEMENTATION implementor;
        readonly DataContractSerializer serializer;
        readonly TcpListener listener;
        readonly ClientList clientList = new();
        readonly IPAddress localIpAddress;
        readonly int port;
        readonly MethodDictionary methodDictionary;
        readonly ManualResetEvent protocolStopper = new(false);

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
        internal int serviceCount = 0;
        internal readonly TcpClient client;
        internal readonly Stream stream;
        internal readonly StreamReader reader;
        internal readonly StreamWriter writer;
    } //class ClientWrapper

    #endregion implementation

}
