﻿// Remoting for .NET Core, .NET 5, and later
//
// Copyright (c) Sergey A Kryukov, 2017-2021
//
// http://www.SAKryukov.org
// http://www.codeproject.com/Members/SAKryukov
// https://github.com/SAKryukov
//

namespace Remoting {
    using System.Reflection;
    using Stream = System.IO.Stream;
    using TcpClient = System.Net.Sockets.TcpClient;
    using DataContractSerializer = System.Runtime.Serialization.DataContractSerializer;
    using StreamReader = System.IO.StreamReader;
    using StreamWriter = System.IO.StreamWriter;
    using IDisposable = System.IDisposable;

    public interface ICooperative : IDisposable { void Yield(); }
    public partial class Client<CONTRACT> where CONTRACT : class {
        public class MethodNotFoundException : System.ApplicationException {
            public MethodNotFoundException(string method) : base(method) { }
        } //class MethodNotFoundException
        public Client(string hostname, int port) {
            client = new();
            serializer = new(typeof(MethodSchema), Utility.CollectKnownTypes(typeof(CONTRACT)));
            proxy = DispatchProxy.Create<CONTRACT, ClientProxy>();
            ((IClientInfrastructure)proxy).SetupContext(client, serializer, hostname, port);
            session = new((IConnectable)proxy);
        } //Client
        public ICooperative Session { get { return session; } }
        public CONTRACT Proxy { get { return proxy; } }
        public interface IConnectable { void Disconnect(); }
    } //class Client

    #region implementation

    public partial class Client<CONTRACT> {

        sealed class SessionImplementation : ICooperative {
            internal SessionImplementation(IConnectable proxy) { this.proxy = proxy; }
            void ICooperative.Yield() { proxy.Disconnect(); }
            void IDisposable.Dispose() { proxy.Disconnect(); }
            readonly IConnectable proxy;
        } //class CooperationProvider
        readonly SessionImplementation session;

        interface IClientInfrastructure {
            void SetupContext(TcpClient client, DataContractSerializer serializer, string hostname, int port);
        } //interface IClientInfrastructure

        public class ClientProxy : DispatchProxy, IClientInfrastructure, IConnectable {
            void IConnectable.Disconnect() {
                if (reader != null) reader.Dispose();
                if (writer != null) writer.Dispose();
                if (stream != null) stream.Dispose();
                client.Close();
                client.Dispose();
                client = new TcpClient();
            } //Disconnect
            void IClientInfrastructure.SetupContext(TcpClient client, DataContractSerializer serializer, string hostname, int port) {
                this.client = client;
                this.callSerializer = serializer;
                this.hostname = hostname;
                this.port = port;
            } //IClientInfrastructure.SetupContext
            protected override object Invoke(MethodInfo targetMethod, object[] args) {
                if (!client.Connected) {
                    client.Connect(hostname, port);
                    stream = client.GetStream();
                    reader = new(stream);
                    writer = new(stream);
                    writer.AutoFlush = true;
                } //if
                var methodSchema = new MethodSchema(targetMethod.ToString(), args);
                string requestLine = Utility.ObjectToString(callSerializer, methodSchema);
                writer.WriteLine(requestLine);
                string responseLine = reader.ReadLine();
                if (responseLine == DefinitionSet.NullIndicator)
                    return null;
                else if (responseLine == DefinitionSet.InterfaceMethodNotFoundIndicator)
                    throw new MethodNotFoundException(methodSchema.MethodName);
                DataContractSerializer returnSerializer = new(targetMethod.ReturnType);
                return Utility.StringToObject(returnSerializer, responseLine);
            } //Invoke
            TcpClient client;
            string hostname;
            int port;
            DataContractSerializer callSerializer;
            Stream stream;
            StreamReader reader;
            StreamWriter writer;
        } //class ClientProxy

        readonly DataContractSerializer serializer;
        readonly TcpClient client;
        readonly CONTRACT proxy;

    } //class Client

    #endregion implementation
}
