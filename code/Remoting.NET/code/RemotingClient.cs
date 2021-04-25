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
    using Stream = System.IO.Stream;
    using TcpClient = System.Net.Sockets.TcpClient;
    using DataContractSerializer = System.Runtime.Serialization.DataContractSerializer;
    using StreamReader = System.IO.StreamReader;
    using StreamWriter = System.IO.StreamWriter;
    using IDisposable = System.IDisposable;
    using UniqueId = System.Int64;
    using ProxyDictionary = System.Collections.Generic.Dictionary<System.Int64, object>;
    using Debug = System.Diagnostics.Debug;

    public interface ICooperative : IDisposable { void Yield(); }
    public partial class Client<CONTRACT> where CONTRACT : class {
        public class MethodNotFoundException : System.ApplicationException {
            public MethodNotFoundException(string method) : base(method) { }
        } //class MethodNotFoundException
        public Client(string hostname, int port) {
            var dispathProxyCreatorMethods = typeof(DispatchProxy).GetMethods(BindingFlags.Public | BindingFlags.Static);
            Debug.Assert(dispathProxyCreatorMethods != null && dispathProxyCreatorMethods.Length == 1);
            dispathProxyCreator = dispathProxyCreatorMethods[0];
            this.hostname = hostname;
            this.port = port;
            client = new();
            serializer = new(typeof(MethodSchema), Utility.CollectKnownTypes(typeof(CONTRACT)));
            proxy = DispatchProxy.Create<CONTRACT, ClientProxy>();
            ((IClientInfrastructure)proxy).Context = this;
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
            Client<CONTRACT> Context { set; }
        } //interface IClientInfrastructure

        public class ClientProxy : DispatchProxy, IClientInfrastructure, IConnectable {
            Client<CONTRACT> IClientInfrastructure.Context { set { this.context = value; } }
            void IConnectable.Disconnect() {
                if (reader != null) reader.Dispose();
                if (writer != null) writer.Dispose();
                if (stream != null) stream.Dispose();
                context.client.Close();
                context.client.Dispose();
                context.client = new TcpClient();
            } //Disconnect
            protected override object Invoke(MethodInfo targetMethod, object[] args) {
                if ((!context.client.Connected) || writer == null || reader == null) {
                    if ((!context.client.Connected))
                        context.client.Connect(context.hostname, context.port);
                    stream = context.client.GetStream();
                    reader = new(stream);
                    writer = new(stream);
                    writer.AutoFlush = true;
                } //if
                var methodSchema = new MethodSchema(targetMethod.ToString(), args);
                string requestLine = Utility.ObjectToString(context.serializer, methodSchema);
                writer.WriteLine(requestLine);
                string responseLine = reader.ReadLine();
                if (responseLine == DefinitionSet.NullIndicator)
                    return null;
                else if (responseLine == DefinitionSet.InterfaceMethodNotFoundIndicator)
                    throw new MethodNotFoundException(methodSchema.MethodName);
                if (responseLine != null && responseLine.Length > 0 && char.IsDigit(responseLine[0])) { //IDynamic UniqueID
                    var uniqueId = UniqueId.Parse(responseLine);
                    if (!context.dynamicProxyDictionary.TryGetValue(uniqueId, out object response)) {
                        var instantiatedMethod = context.dispathProxyCreator.MakeGenericMethod(new System.Type[] { targetMethod.ReturnType, typeof(ClientProxy)});
                        var dynamicProxy = instantiatedMethod.Invoke(null, null);
                        ((IClientInfrastructure)dynamicProxy).Context = this.context;
                        context.dynamicProxyDictionary.Add(uniqueId, dynamicProxy);
                        return dynamicProxy;
                    } else
                        return response;
                } //if IDynamic
                DataContractSerializer returnSerializer = new(targetMethod.ReturnType);
                return Utility.StringToObject(returnSerializer, responseLine);
            } //Invoke
            Client<CONTRACT> context;
            Stream stream;
            StreamReader reader;
            StreamWriter writer;
        } //class ClientProxy

        readonly DataContractSerializer serializer;
        TcpClient client;
        readonly CONTRACT proxy;
        readonly string hostname;
        readonly int port;
        readonly ProxyDictionary dynamicProxyDictionary = new();
        readonly MethodInfo dispathProxyCreator;

    } //class Client

    #endregion implementation
}
