@numbering {
    enable: false
}

{title}New Remoting for New .NET

<!--
Original publication:
-->

[*Sergey A Kryukov*](https://www.SAKryukov.org){.author}

TCP-based remoting to replace deprecated remoting or WCF for .NET Core, .NET 5, and later versions --- in only 3 C# files

*As binary serializer is deprecated, marshalling is based on DataContract. It allows to pass any data structures as method parameters and return values, even the structures with cyclic references. Multiple clients and parallel execution are supported, as well as properties and methods with any input parameters. The service contract is based on a custom interface provide by a developer; absolutely no attributes or base interfaces/classes are required. Required are only two attributes for structural parameter/return types: [DataContract] and [DataMember].*

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

How to develope software without remoting? Well, there are 3rd-party remoting or RPC frameworks, but 3rd-party is 3rd-party. But how about taking just 3 my C# files and having it all in no time, with pretty universal and flexible features?

## Usage

First, the developer of applications needs to create some definition common for the client and server parts. This is some *service contract* and a port number.

The service contract is just the application-specific interface type. It does not need any attributes and does not have to be derived from any particular interface.

Then, on the server side this interface should be implemented by some class that can be called, for example, `Implementation`. A single instance of this class needs to be created. Now the generic `Server` class should connect this implementation with a network. Assuming the `port` is the service port number, and `Implementation` implements the interface `IMyContract`, this is how it is done:

~~~
var server = new Remoting.Server<IMyContract, Implementation>(
    port,
    new Implementation());
~~~

The fact that the class `Implementation` implements `IMyContract` is guaranteed by the [constraints on the generic type parameters](https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/generics/constraints-on-type-parameters). The fact that the contract type is an interface is [validated during runtime](#heading-rejection-of-invalid-contract-interfaces).

This is a constructor call which performs the reflection of both `IMyContract` and `Implementation` types and emits the network-enabled proxy object on the fly using `Reflection.Emit`. Essentially, this object can convert data into the calls to the methods of the `Implementation` instance passed as a second parameter of the `Implementation` constructor, obtain the return object of each call and convert it to data. This input data is received from or sent to a network stream.

After the call to the constructor, the `server.Start()` can be called. This call is non-blocking, as all the operations are performed in two separate threads, one for connection and another for the remote method call protocol.

On the client side, a client object is created based on the same interface and a port number:

~~~
var client = new Remoting.Client<IMyContract>(serverHostName, port);
~~~

Then the client `Proxy` property can be used for the remote calls corresponding to the contract interface method:

~~~
client.Proxy.A(/* ... */);
client.Proxy.B(/* ... */);
// ...
var objectGraph = client.Proxy.Transform(/* ... */)
~~~

The structured data for the parameters or the returned object should be defined via some `DataContract`. The client and server are totally agnostic to the detail of the data contract, they are discovered automatically using reflection.

The contract interface can use any data types for the parameters, returned objects, and properties. However, please see the [limitations](#heading-limitations).

Typically, the flow of remote calls should be wrapped in some functions grouping several calls and representing some session:

~~~
void SomeSession() {
    using Remoting.ICooperative session = remotingClient.Session;
    client.Proxy.A(/* ... */);
    client.Proxy.B(/* ... */);
    partner.Yield(); // optional
    // ...
    var objectGraph = client.Proxy.Transform(/* ... */)
}
~~~

The usage of sessions helps to improve performance of the service and needs a separate discussion. It represents the cooperation model.

## Parallel Execution and Cooperation Model

It is obvious that when we have more than one client running in parallel, one client can block the execution of another one. It happens because the `Stream.Read` methods are blocking. When a server protocol thread is blocked at a `Stream.Read` call, another client can be blocked at a `Stream.Read` call of a proxy. This situation will continue until the blocking client completes the operations on its remote call.

There are different solutions to this problem. Usually we can see the advice to create a separate thread per client at the moment when a client connects. So a listening thread should create unlimited number of different threads. I also used this approach, but it was developed for a particular system, when the maximum number of clients was known in advance.

If is good or bad? In the situation when the maximum number of clients is known or predictable, this is quite good. However, what can happen in a general case? It is possible to block the functionality of such a server by overwhelming number of threads caused by a big number of connections to the same TCP channel, and it can happen by accident. This kind of accidental attack would not require too much resources on the client side, because a client thread can just connect and write nothing to its network stream, staying permanently in [a wait state](https://en.wikipedia.org/wiki/Wait_state).

There is also a near-opposite approach: a client connects to the server only for a short period of time. This approach could be called cooperative: one client waits until another thread completes the operation, but all the clients should guarantee reasonably fast execution. Note that nothing bad can happen if a client application breaks or its system, say, looses power or otherwise disconnects: it disconnects the client, and other clients still can connect. In terms of RPC it can be just one remote call, which means one write and one read operation. This model is pretty reliable, but wasteful in terms of the usage of the network resource. Indeed, the traffic can become way too fine-granular. Typically, more optimal network performance is achieved for much bigger data packages. Of course, in our case it depends on the number of parameters and the size of data.

Also, some combined approaches are possible: a fixed-size thread pool, and the like.

For the remoting presenting in this article, I developed a combined cooperative approach. A client should cooperate and disconnect when the service is not used, but the session can last longer than it is required for a single remote method call. The session is controlled in an automated way. First of all, the connection is done on a call. When a remote call needs a connection, the proxy implementation checks up if a connection is already open, otherwise the client is connected or reconnected. A disconnection can happen in different ways. First of all, a session object implements the interface `System.IDisposable`, so it can automatically disconnect at the end of the current context if created via the `using` statement. Additionally, it implements the interface `ICooperative` and can call the method `ICooperative.Yield` to allow other clients to pass possibly somewhere in the middle of some client method. The server's protocol thread count the number of "served" calls per client and prioritizes the clients based on this count.

## Implementation

The [diagram on top](#image-diagram) roughly illustrates the principles of the implementation.

### Reflection

### Collection of Known Types

### Rejection of Invalid Contract Interfaces

### Server: Emitting the Method Code

### Client: Creation of Proxy

### Session Control
`
## Limitations

* This framework represents a pure dumb client-server system and the [pull technology](https://en.wikipedia.org/wiki/Pull_technology): there are no callbacks and no [server push](https://en.wikipedia.org/wiki/Push_technology).
* Therefore, it does not support .NET events
* The parameters of the contract interface methods cannot be `out` or `ref` parameters.
* The parameter types are treated as pure data types; in particular, they cannot be types, delegate instances, lambda expressions and the like; in other words, all the parameters and return objects should be serializable via the `DataContract`.
