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
    using TcpClient = System.Net.Sockets.TcpClient;
    using DataContractSerializer = System.Runtime.Serialization.DataContractSerializer;
    using StreamReader = System.IO.StreamReader;
    using StreamWriter = System.IO.StreamWriter;

    public class Client<CONTRACT> where CONTRACT : IContract {

        public class MethodNotFoundException: System.ApplicationException {
            public MethodNotFoundException(string method) : base(method) { }
        } //class MethodNotFoundException

        public Client(string hostname, int port) {
            client = new();
            Proxy = DispatchProxy.Create<CONTRACT, ClientProxyBase>();
            ((IClientInfrastructure)Proxy).SetupContext(client, serializer, hostname, port);
        } //Client

        public CONTRACT Implementation { get { return Proxy; } }

        interface IClientInfrastructure {
            void SetupContext(TcpClient client, DataContractSerializer serializer, string hostname, int port);
        } //interface IClientInfrastructure

        public class ClientProxyBase : DispatchProxy, IClientInfrastructure {
            ~ClientProxyBase() {
                reader.Dispose();
                writer.Dispose();
                client.Dispose();
            }
            void IClientInfrastructure.SetupContext(TcpClient client, DataContractSerializer serializer, string hostname, int port) {
                this.client = client;
                this.callSerializer = serializer;
                this.hostname = hostname;
                this.port = port;
            }
            protected override object Invoke(MethodInfo targetMethod, object[] args) {
                if (!client.Connected) {
                    client.Connect(hostname, port);
                    var stream = client.GetStream();
                    reader = new(stream);
                    writer = new(stream);
                    writer.AutoFlush = true;
                } //if
                var methodSchema = new MethodSchema(targetMethod.ToString(), args);
                string requestLine = Utility.ObjectToString(callSerializer, methodSchema);
                writer.WriteLine(requestLine);
                string responseLine = reader.ReadLine();
                if (responseLine == string.Empty)
                    throw new MethodNotFoundException(methodSchema.MethodName);
                DataContractSerializer returnSerializer = new(targetMethod.ReturnType);
                return Utility.StringToObject(returnSerializer, responseLine);
            } //Invoke
            TcpClient client;
            string hostname;
            int port;
            DataContractSerializer callSerializer;
            StreamReader reader;
            StreamWriter writer;
        } //class ServerProxyBase

        readonly DataContractSerializer serializer = new(typeof(MethodSchema));
        readonly TcpClient client;
        readonly CONTRACT Proxy;

    } //class Client

}