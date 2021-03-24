@numbering {
    enable: false
}

{title}New Remoting for New .NET

<!--
Original publication:
-->

[*Sergey A Kryukov*](https://www.SAKryukov.org){.author}

TCP-based remoting to replace deprecated remoting or WCF for .NET Core, .NET 5, and later versions --- in only 3 C# files

*As binary serializer is deprecated, marshalling is based on DataContract. It allows to pass any data structures as method parameters and return values, event the structures with cyclic references. Multiple clients and parallel execution are supported, as well as properties and methods with any input parameters. The service contract is based on a custom interface provide by a developer; absolutely no attributes or base interfaces/classes are required. Required are only two attributes for structural parameter/return types: [DataContract] and [DataMember].*

<!-- copy to CodeProject from here ------------------------------------------->

<ul class="download"><li><a href="5291705/Working/JavaScript-Playground.zip">Download source code — 23.4 KB</a></li></ul>

![Sample](diagram.png)

<blockquote id="epigraph" class="FQ"><div class="FQA">Epigraph:</div>
<p><i>He who controls the remote, controls the world</i></p>
<dd><a href="https://juliegarwood.com">Julie Garwood</a></dd>
</blockquote>

## Content{no-toc}
    
@toc

## Preface

SA???

## Usage


## Parallel Execution and Cooperation Model


## Console

## Implementation

## Ex
