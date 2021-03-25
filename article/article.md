@numbering {
    enable: false
}

{title}New Remoting for New .NET

<!--
Original publication:
-->

[*Sergey A Kryukov*](https://www.SAKryukov.org){.author}

TCP-based remoting to replace deprecated remoting or WCF for .NET Core, .NET 5, and later versions --- in only 3 C# files

*As binary serializer is deprecated, marshalling is based on DataContract. It allows to pass any data structures as method parameters and return values, even object graphs with cyclic references. Multiple clients and parallel execution are supported, as well as properties and methods with any input parameters. The service contract is based on a custom interface provide by a developer; absolutely no attributes or base interfaces/classes are required. Required are only two attributes for structural parameter/return types: [DataContract] and [DataMember].*

<!-- copy to CodeProject from here ------------------------------------------->

<ul class="download"><li><a href="5291705/Working/JavaScript-Playground.zip">Download source code — 23.4 KB</a></li></ul>

![Sample](diagram.png) {id=image-diagram}

<blockquote id="epigraph" class="FQ"><div class="FQA">Epigraph:</div>
<p><i>He who controls the remote, controls the world</i></p>
<dd><a href="https://juliegarwood.com">Julie Garwood</a></dd>
</blockquote>

## Content{no-toc}
    
@toc

## Introduction

In .NET Core and .NET 5, old good remoting and WCF have been deprecated.

How to develop software without remoting? Well, there are 3rd-party remoting or RPC frameworks, but 3rd-party is 3rd-party. But how about taking just 3 my C# files and having it all in no time, with pretty universal and flexible features?

## Usage

First, the developer of applications needs to create some definitions common for the client and server parts. This is some *service contract* and a port number.

The service contract is just the application-specific interface type. It does not need any attributes and does not have to be derived from any particular interface.

Then, on the server side, this interface should be implemented by some class that can be called, for example, `Implementation`. A single instance of this class needs to be created. Now the generic `Server` class should connect this implementation with a network. Assuming the `port` is the service port number, and `Implementation` implements the interface `IMyContract`, this is how it is done:

~~~{lang=C#}{id=code-new-server}
var server = new Remoting.Server<IMyContract, Implementation>(
    port,
    new Implementation());
~~~

The fact that the class `Implementation` implements `IMyContract` is guaranteed by the [constraints on the generic type parameters](https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/generics/constraints-on-type-parameters). The fact that the service contract type is an interface is [validated during runtime](#heading-rejection-of-invalid-contract-interfaces).

The call to the `Remoting.Server<>` constructor performs the reflection of both `IMyContract` and `Implementation` types and emits the network-enabled proxy object on the fly using `Reflection.Emit`. Essentially, this object can convert data into the calls to the methods of the `Implementation` instance passed as a second parameter of the `Implementation` constructor, obtain the return object of each call and convert it to data. This input data is received from or sent to a network stream.

After the call to the constructor, the `server.Start()` can be called. This call is non-blocking, as all the operations are performed in two separate threads, one for connection and another for the remote method call protocol.

On the client side, a client object is created based on the same interface and a port number:

~~~{lang=C#}{id=code-new-client}
var client = new Remoting.Client<IMyContract>(serverHostName, port);
~~~

Then the client `Proxy` property can be used for the remote calls corresponding to the service contract interface method:

~~~{lang=C#}{id=code-sample-client}
client.Proxy.A(/* ... */);
client.Proxy.B(/* ... */);
// ...
var objectGraph = client.Proxy.Transform(/* ... */)
~~~

The structured data for the parameters or the returned object should be defined via some `DataContract`. The client and server are agnostic to the detail of the data contract, they are discovered automatically using reflection.

The service contract interface can use any data types for the parameters, returned objects, and properties. However, please see the [limitations](#heading-limitations).

Typically, the flow of remote calls should be wrapped in some functions grouping several calls and representing some session:

~~~{lang=C#}{id=code-sample-session}
void SomeSession() {
    using Remoting.ICooperative session = remotingClient.Session;
    client.Proxy.A(/* ... */);
    client.Proxy.B(/* ... */);
    partner.Yield(); // optional
    // ...
    var objectGraph = client.Proxy.Transform(/* ... */)
}
~~~

The usage of sessions helps to improve the performance of the service and needs a separate discussion. It represents the cooperation model.

## Parallel Execution and Cooperation Model

It is obvious that when we have more than one client running in parallel, one client can block the execution of another one. It happens because the `Stream.Read` methods are blocking. When a server protocol thread is blocked at a `Stream.Read` call, another client can be blocked at a call to `Stream.Read` of a proxy. This situation will continue until the blocking client completes the operations on its remote call.

There are different solutions to this problem. Usually, we can see the advice to create a separate thread per client at the moment when a client connects. So a listening thread should create an unlimited number of different threads. I also used this approach, but it was developed for a particular system, for which the maximum number of clients was known in advance.

Is it good or bad? In the situation when the maximum number of clients is known or predictable, this is quite good. However, what can happen in a general case? It is possible to block the functionality of such a server by an overwhelming number of threads created in response to a big number of connections to the same TCP channel, and it can happen by accident. This kind of accidental or malicious attack would not even require too expensive resources on the client side, because a client thread can just connect and write nothing to its network stream, staying permanently in [a wait state](https://en.wikipedia.org/wiki/Wait_state).

There is also a near-opposite approach: a client connects to the server only for a short time. This approach could be called *cooperative*: one client waits until another thread completes the operation, but all the clients should guarantee reasonably fast execution of the fragment of code dealing with the service. Note that nothing bad can happen if a client application breaks or its system, say, loses power or otherwise disconnects: it disconnects the client, and other clients still can connect. In terms of [RPC](https://en.wikipedia.org/wiki/RPC), it can be just one remote call, which means one write and one read operation. This model can be pretty reliable but wasteful in terms of the usage of the network resources. Indeed, the traffic can become way too fine-granular. Typically, more optimal network performance is achieved for much bigger data packages. Of course, in our case, it depends on the number of parameters and the size of the data.

Also, some combined approaches are possible: a fixed-size thread pool and the like.

For the remoting presenting in this article, I developed a combined cooperative approach. On the server side, only two threads are added during runtime: a listening thread and a protocol thread.

A client should cooperate and disconnect when the service is not used, but the session can last longer than it is required for a single remote method call. The session is controlled in an automated way. First of all, the connection is done on a call. When a remote call needs a connection, the proxy implementation checks up if a connection is already open, otherwise. the client is connected or re-connected. Disconnection can happen in different ways. First of all, a session object implements the interface `System.IDisposable`, so it can automatically disconnect at the end of the current context if created via the `using` statement. Additionally, it implements the interface `ICooperative` and can call the method `ICooperative.Yield` to allow other clients to pass possibly somewhere in the middle of some client method. The server's protocol thread counts the number of "served" calls per client and prioritizes the clients based on this count.

## Implementation

The [diagram on top](#image-diagram) roughly illustrates the principles of the implementation.

### Server: Reflection

For the server side, we need to dynamically [emit the code](#heading-server3a-emitting-the-method-code) for some object which can identify an implementation class method by the data received from the network and call this method with appropriate actual parameters obtained from this data. Reflection is more expensive than other parts of this communication, so using `System.Reflection.Emit` is important to make sure the information about the detail of all the methods is reused during remote method calls.

Before emitting code, we need to collect all required information. One problem is that we need to reflect only the methods of the service contract interface. On the other hands, the code should be emitted based on `System.Reflection.MethodInfo` of the implementing class, not the interface. Do to so, we need to reflect all the methods of the service contract interface and, for each method, reflect a corresponding method of the implementing class. The notion of the "corresponding method" here is somewhat tricky. The following technique is used:

~~~{lang=C#}{id=code-reflection}
foreach (var method in methods) {
    // here, method is an interface method
    var parameterInfo = method.GetParameters();
    var methodName = $"{interfaceType.FullName}.{method.Name}";
    var parameters = System.Array.ConvertAll(
        method.GetParameters(),
        new System.Converter&lt;ParameterInfo, System.Type&gt;(
            el => el.ParameterType));
    // first, looking for the public implementation method
    // implicitly implementing required interface method:
    var implementorMethod = implementorType.GetMethod(
            method.Name,
            BindingFlags.Public | BindingFlags.Instance,
            null,
            parameters,
            null);
    // see if there is an explicit method implementation
    if (implementorMethod == null)
        implementorMethod = implementorType.GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            parameters,
            null);
    Debug.Assert(implementorMethod != null);
    dictionary.Add(
        method.ToString(),
        CreateCaller(implementorType, implementorMethod));
}
~~~

In this fragment, the collection `methods` are obtained from the service contract interface type recursively, to include all the methods of its base interface types. Note that it transparently includes interface properties, because each property is accessed by its getter, or setter, or both.

One problem in the identification of the implementation method is that different methods can have identical names. This problem is solved by supplying a fully-qualified name of the interface method with the method profiles, also identified by the methods' parameter information.

The nastier problem is this: the same implementation class can still implement two different methods implementing the same interface methods. This situation is possible in only one case: when one method is an implicit implementation of an interface method, and another one is an explicit implementation of the same method.

When we program a call to the method in a "usual" way, we can see, that the priority is given to the implicit implementation. When we call the method using "System.Reflection" and "System.Reflection.Emit", the preference is our choice. In the code [shown above](#code-reflection), we mimic the "usual" way.

Based on the collected metadata, we emit some code for each method and store it in some dictionary for fast access during actual communication with the clients. The call to the method `CreateCaller` [shown above](#code-reflection) is about emitting the code. [Let's see how it is done](#code-reflection-emit).

### Server: Emitting the Method Code

Each method created with `System.Reflection.Emit` should do only one thing: call the corresponding method of the service contract interface implementing class. This is important because we don't know statically which method is called, as we receive the request from a client in the form of some data. We emit the required methods in the form of [System.Reflection.Emit.DynamicMethod](https://docs.microsoft.com/en-us/dotnet/api/system.reflection.emit.dynamicmethod?view=net-5.0).

The emitted code is fairly simple: we put the method arguments identified by their types on the evaluation stack and emit a call or a virtual method call. As all methods based on an interface are always the instance methods, we also put "this" as the first argument:

~~~{lang=C#}{id=code-reflection-emit}
static DynamicMethod CreateCaller(System.Type implementor, MethodInfo method)
{
    var parameterInfo = method.GetParameters();
    System.Type[] parameters = new System.Type[parameterInfo.Length + 1];
    parameters[0] = implementor;
    for (var index = 1; index &lt; parameters.Length; ++index)
        parameters[index] = parameterInfo[index - 1].ParameterType;
    DynamicMethod result = new(
        $"{method.DeclaringType.FullName}.{method.Name}",
        method.ReturnType,
        parameters);
    var generator = result.GetILGenerator();
    for (var index = 0; index &lt; parameters.Length; ++index)
        generator.Emit(OpCodes.Ldarg_S, index);
    generator.Emit(method.IsVirtual ?
        OpCodes.Callvirt : OpCodes.Call, method);
    generator.Emit(OpCodes.Ret);
    return result;
}
~~~

### Collection of Known Types

Both client and server parts need an additional portion of reflection. It is needed to support structural data types passed as method parameters. All the parameters, with the fully-qualified interface method names and profiles, are marshalled using the same type passed to the constructor of `System.Runtime.Serialization.DataContractSerializer`:

~~~{lang=C#}{id=code-data-contract}
using System.Runtime.Serialization;

//...

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

~~~

This data structure is fixed because the actual data types are not statically known during the communication. The workaround is to collect all these types in advance from the service contract interface type and pass the collection as the `knownType` parameter to the constructor of `DataContractSerializer`:

~~~{lang=C#}{id=code-create-serialize}
System.Runtime.Serialization.DataContractSerializer serializer
=     new(typeof(MethodSchema), Utility.CollectKnownTypes(typeof(CONTRACT)));
~~~

The reflection performed by the method `Utility.CollectKnownTypes` is trivial. It recursively traverses the interface type `typeof(CONTRACT)` and all its parent interfaces, all the interface methods, and all the parameters of these methods, but not the return types.

### Rejection of Invalid Service Contract Interfaces

During the reflection using the method `Utility.CollectKnownTypes`, the service contract interface is validated. Some validations are possible only during runtime. It is done on both server and client sides, but only once.

First of all, it should be validated that the service contract interface type is the interface type. Generic parameter constrain cannot ensure that. The only useful constraint for this type is `class`:

~~~{lang=C#}{id=code-genertic-constraints-server}
public class Server<CONTRACT, IMPLEMENTATION>
    where IMPLEMENTATION : CONTRACT, new() where CONTRACT : class {
    /* ... */
}
~~~

~~~{lang=C#}{id=code-genertic-constraints-client}
public class Client<CONTRACT> where CONTRACT : class {
    /* ... */
}
~~~

On the other hand, there a very good reasons to limit the service contract to the interface type, so it is validated:

~~~{lang=C#}{id=code-validate-interface}
if (!interfaceType.IsInterface)
    throw new InvalidInterfaceException(interfaceType);
~~~

In addition to this limitation, `out` or `ref` parameters cannot be used. This is also validated. Given the set of `System.Type` instances `typeSet` and a collection of parameters, we have:

~~~{lang=C#}{id=code-validate-interface-parameter-types}
static void AddType(TypeSet typeSet, System.Type type) {
    if (type == typeof(void)) return;
    if (type.IsPrimitive) return;
    if (type == typeof(string)) return;
    if (!typeSet.Contains(type))
        typeSet.Add(type);
}

// ...

foreach (var parameter in parameters)
    if (!parameter.IsOut && !parameter.ParameterType.IsByRef)
        AddType(typeSet, parameter.ParameterType);
    else
        throw new InvalidInterfaceException(type, method, parameter);
~~~

Note that the primitive types and the string type are valid but not collected as known types. They are already known to the serialization.

### Client: Creation of Proxy

We could create a proxy implementing the service contract interface using `System.Reflection` and `System.Reflection.Emit`, pretty much the same way we emitted [dynamic methods](https://docs.microsoft.com/en-us/dotnet/api/system.reflection.emit.dynamicmethod?view=net-5.0) on the server side. With the client site, we got a lot more luck though.

For .NET Core and .NET 5, this work is already done in the upgraded version of reflection. The newer abstract class [System.Reflection.DispatchProxy](https://docs.microsoft.com/en-us/dotnet/api/system.reflection.dispatchproxy?view=net-5.0) does it all.

We need to override the method `System.Reflection.DispatchProxy.Invoke` and implement the network transport per each call:

~~~{lang=C#}{id=code-client-proxy}
interface IClientInfrastructure { /* ... */ }
// ...
public interface IConnectable { void Disconnect(); }

public class ClientProxy :
    DispatchProxy, IClientInfrastructure, IConnectable {
    void IConnectable.Disconnect() {
        if (reader !=null) reader.Dispose();
        if (writer != null) writer.Dispose();
        if (stream != null) stream.Dispose();
        client.Close();
        client.Dispose();
        client = new TcpClient();
    }
    void IClientInfrastructure.SetupContext(
        TcpClient client,
        DataContractSerializer serializer,
        string hostname, int port)
    {
        this.client = client;
        // ...
    }
    protected override object Invoke(MethodInfo targetMethod, object[] args) {
        if (!client.Connected) {
            client.Connect(hostname, port);
            stream = client.GetStream();
            reader = new(stream);
            writer = new(stream);   
            writer.AutoFlush = true;
        } //if
        var methodSchema = new MethodSchema(targetMethod.ToString(), args);
        string requestLine =
            Utility.ObjectToString(callSerializer, methodSchema);
        writer.WriteLine(requestLine);
        string responseLine = reader.ReadLine();
        if (responseLine == DefinitionSet.NullIndicator)
            return null;
        else
        if (responseLine == DefinitionSet.InterfaceMethodNotFoundIndicator)
            throw new MethodNotFoundException(methodSchema.MethodName);
        DataContractSerializer returnSerializer =
            new(targetMethod.ReturnType);
        return Utility.StringToObject(returnSerializer, responseLine);
    } //Invoke
    TcpClient client;
    string hostname;
    int port;
    DataContractSerializer callSerializer;
    Stream stream;
    StreamReader reader;
    StreamWriter writer;
}
~~~

Note two additional interfaces implemented by this class: `IClientInfrastructure` and `IConnectable`.

The interface `IClientInfrastructure` is used to pass required context objects to the proxy instance. It cannot be done directly, because the instance is created by `System.Reflection.DispatchProxy`. So, we have:

~~~{lang=C#}{id=code-client-proxy-instance}
proxy = DispatchProxy.Create<CONTRACT, ClientProxy>();
((IClientInfrastructure)proxy).
    SetupContext(client, serializer, hostname, port);
~~~

Another interface, `IConnectable`, is used to add some session control.

### Session Control

From the `ClientProxy` [code](#code-client-proxy), we can see that the client is connected to the server TCP channel on the remote method call if it is detected that the connection is currently not available.

A temporary disconnection is implemented via the interface `IConnectable`. And, finally, the temporary disconnection can be requeste    d be the code using `Remoting.Client` ether explicitly, via the interface `ICooperative` or automatically, via the interface `System.IDisposable`:

~~~{lang=C#}{id=code-client-proxy-instance}
public sealed class SessionImplementation : ICooperative {
    internal SessionImplementation(IConnectable proxy) { this.proxy = proxy; }
    void ICooperative.Yield() { proxy.Disconnect(); }
    void IDisposable.Dispose() { proxy.Disconnect(); }
    readonly IConnectable proxy;
} //class CooperationProvider
readonly SessionImplementation session;
public ICooperative Session { get { return session; } }

//...

public interface ICooperative : IDisposable { void Yield(); }
~~~

This explains the client behavior in the code sample [shown above](#code-sample-session) and the [cooperation model](#heading-parallel-execution-and-cooperation-model).

## Illustration: Directed Graph

Let's test that we can implement any data types and pass them through the remoting service contract, even the object graphs with cyclic referenced. A natural example for such object graphs would be... well, some model of directed graphs.

Let's look at the definition of two classes, `DirectedGraph` and `Node` found in "code/Test/ClientServer.code/Common.cs". They are `DataContract`-enabled and sufficient for the simplest implementation of the directed graph model.

Let's build an initial graph on the client side and define some simple graph transformations, the methods `Insert`, `Connect` and `Disconnect`. It would be the easiest to implement them on the client side, too, but we are not looking the easy ways. Our goal is to test the power of `Remoting`. So, we add these methods in the service contract interface and implement them on the server side. Here are the declarations:

~~~{lang=C#}{id=code-ITestContract}
interface ITestContract {
    DirectedGraph Connect(DirectedGraph graph, Node tail, Node head);
    DirectedGraph Disconnect(DirectedGraph graph, Node tail, Node head);
    DirectedGraph Insert(DirectedGraph graph, Node tail, Node head);
}
~~~

The operations are pretty much self-explaining, and their implementations are trivial. We should remember that in *marshalling* the objects lose their *referential identity*: for the instance of a directed graph returned from the remote call, the node originally passed as a method parameter is not the same node found in the returned graph.

Let's look at the results of these tree transformations:

Original directed graph sample:

![directed graph sample](directed-graph-original.png)

Starting from the original sample, let's insert a new node after `first`:

![directed graph after insert](directed-graph-insert.png)

...or disconnect `second`:

![directed graph after disconnect](directed-graph-disconnect.png)

... or connect `second` to `first`; it removes the original arc between `second` and `third`:

![directed graph after reconnect](directed-graph-reconnect.png)

## Compatibility and Build

The code is tested on .NET v.5 and should be compatible with the earlier .NET Core versions on any platform.

The build does not require Visual Studio or any other IDE, only .NET is required. The batch build is created for Windows and can easily be added to any other system, such as Linux. Essentially, this is just

~~~
dotnet build %solution% -c Release
dotnet build %solution% -c Debug
~~~

where `solution` is the .sln file name.

## Limitations

* This framework represents a pure dumb client-server system and the [pull technology](https://en.wikipedia.org/wiki/Pull_technology): there are no callbacks and no [server push](https://en.wikipedia.org/wiki/Push_technology).
* Therefore, it does not support .NET events
* The parameters of the contract interface methods [cannot](#code-validate-interface-parameter-types) be `out` or `ref` parameters.
* The parameter types are treated as pure data types; in particular, they cannot be `System.Types` objects, delegate instances, lambda expressions, and the like; in other words, all the parameters and return objects should be serializable via the `DataContract`.

## Conclusion

Even though I've started to work at similar architectures years ago, the code presented here is still highly experimental.

I would be much grateful for any kind of feedback, and especially for criticism.
