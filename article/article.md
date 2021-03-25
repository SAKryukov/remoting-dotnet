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

The fact that the class `Implementation` implements `IMyContract` is guaranteed by the [constraints on the generic type parameters](https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/generics/constraints-on-type-parameters). The fact that the service contract type is an interface is [validated during runtime](#heading-rejection-of-invalid-contract-interfaces).

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

### Server: Reflection

For the server side, we need to dynamically [emit the code](#heading-server3a-emitting-the-method-code) for some object which can identify an implementation class method by the data received from network and call this method with appropriate actual parameters obtained from this data. Reflection is more expensive than other parts of this communication, so using `System.Reflection.Emit` is important to make sure the information about detail of all the methods is reused during remote method calls.

Before emitting code, we need to collect all required information. One problem is that we need to reflect only the methods of the contract interface. On the other hands, the code should be emitted based on `System.Reflection.MethodInfo` of the implementing class, not the interface. Do to so, we need to reflect all the methods of the contract interface and, for each method, reflect a corresponding method of the implementing class. The notion of the "corresponding method" here is somewhat tricky. The following technique is used:

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

In this fragment, the collection `methods` is obtained from the contract interface type recursively, to include all the methods of its base interface types. Note that it transparently includes interface properties, because each property is accessed by its getter, or setter, or both.

One problem in the identification of the implementation method is that different methods can have identical names. This problem is solved by supplying fully-qualified name of the interface method with the method profiles, also identified by the methods' parameter information.

The nastier problem is this: the same implementation class can still implement two different methods implementing the same interface methods. This situation is possible in only one case, when one methods is an implicit implementation of an interface method, and another one is an explicit implementation.

When we program a call to the method in a "usual" way, we can see, that the priority is given to the implicit implementation. When we call the method using "System.Reflection" and "System.Reflection.Emit", the preference is our choice. In the code [shown above](#code-reflection), we mimic the "usual" way.

Based on the collected metadata, we emit some code for each method and store it in some dictionary for fast access during actual communication with the clients. The call to the method `CreateCaller` [shown above](#code-reflection) is about emitting the code. [Let's see how it is done](#code-reflection-emit).

### Server: Emitting the Method Code

Each method created with `System.Reflection.Emit` should do only one thing: call the corresponding method of the contract interface implementing class. This is important, because we don't know statically which method is called, as we receive the request from a client in the form of some data. We emit the required methods in the form of [System.Reflection.Emit.DynamicMethod](https://docs.microsoft.com/en-us/dotnet/api/system.reflection.emit.dynamicmethod?view=net-5.0).

The emitted code is fairly simple: we put the method arguments identified by their types on the evaluation stack and emit a call or a virtual method call. As all methods, being based on an interface, are always the instance methods, we also put "this" as the first argument:

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

Both client and server parts need an addition portion of reflection. It is needed to support structural data types passed as method parameters. All the parameters, with the fully-qualified interface method names and profiles are marshalled using the same type passed to the constructor of `System.Runtime.Serialization.DataContractSerializer`:

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

This data structure is fixed because the actual data type are not statically known during the communication. The workaround is to collect all these types in advance from the service contract interface type and pass the collection as the `knownType` parameter to the constructor of `DataContractSerializer`:

~~~{lang=C#}{id=code-create-serialize}
System.Runtime.Serialization.DataContractSerializer serializer
=     new(typeof(MethodSchema), Utility.CollectKnownTypes(typeof(CONTRACT)));
~~~

The reflection performed by the method `Utility.CollectKnownTypes` is trivial. It recursively traverses the interface type `typeof(CONTRACT)` and all its parent interfaces, all the interface methods, and all the parameters of these methods, but not the return types.

### Rejection of Invalid Contract Interfaces

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

In addition to this limitation, `out` or `ref` parameters cannot be used. This is also validated. Given the set of `System.Type` instances `typeSet`, we have:

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

### Session Control

## Compatibility, Build and Testing

## Limitations

* This framework represents a pure dumb client-server system and the [pull technology](https://en.wikipedia.org/wiki/Pull_technology): there are no callbacks and no [server push](https://en.wikipedia.org/wiki/Push_technology).
* Therefore, it does not support .NET events
* The parameters of the contract interface methods [cannot](#heading-rejection-of-invalid-contract-interfaces) be `out` or `ref` parameters.
* The parameter types are treated as pure data types; in particular, they cannot be types, delegate instances, lambda expressions and the like; in other words, all the parameters and return objects should be serializable via the `DataContract`.

## Conclusion

Even though I've started to work at similar architectures years ago, the code presented here is still highly experimental.

I would be much grateful for any kinds of feedback, and especially for criticism.
