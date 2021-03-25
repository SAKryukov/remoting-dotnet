// Remoting for .NET Core, .NET 5, and later
//
// Copyright (c) Sergey A Kryukov, 2017-2021
//
// http://www.SAKryukov.org
// http://www.codeproject.com/Members/SAKryukov
// https://github.com/SAKryukov
//

namespace Test {
    using Console = System.Console;

    class Entry {

        static void Main() {

            var remotingClient = new Remoting.Client<ITestContract>(
                "localhost",
                Remoting.DefinitionSet.PortAssignmentsIANA.DynamicPrivatePorts.First);

            void SimpleDemonstration() {
                using Remoting.ICooperative partner = remotingClient.Session;
                Console.WriteLine();
                Console.WriteLine("String and primitive-type parameters demo:");
                remotingClient.Proxy.P = "My property P value";
                Console.WriteLine(remotingClient.Proxy.P);
                remotingClient.Proxy.B(1, 2);
                var a = remotingClient.Proxy.A(3, 11);
                Console.WriteLine(a);
                a = remotingClient.Proxy.A(13, 'b');
                Console.WriteLine(a);
                partner.Yield();
                a = remotingClient.Proxy.A("First parameter is string", 1313);
                Console.WriteLine(a);
                a = remotingClient.Proxy.A("Only one parameter");
                Console.WriteLine(a);
                remotingClient.Proxy.B(2, 1);
                Console.WriteLine("B(int, int) called");
                Console.WriteLine("Done!");
            } //SimpleDemonstration

            void GraphDemonstration() {
                using Remoting.ICooperative partner = remotingClient.Session;
                Console.WriteLine();
                Console.WriteLine("Directed graph demo:");
                var graph = DirectedGraph.DemoSample;
                Console.WriteLine(graph.Visualize());
                graph = remotingClient.Proxy.Insert(graph, graph.AccessNode, new Node("new"));
                Console.WriteLine("Insert:");
                Console.WriteLine(graph.Visualize());
                graph = DirectedGraph.DemoSample;
                graph = remotingClient.Proxy.Disconnect(graph, DirectedGraph.second, DirectedGraph.third);
                Console.WriteLine("Disconnect:");
                Console.WriteLine(graph.Visualize());
                graph = DirectedGraph.DemoSample;
                graph = remotingClient.Proxy.Connect(graph, DirectedGraph.second, graph.AccessNode);
                Console.WriteLine("Reconnect:");
                Console.WriteLine(graph.Visualize());
                Console.WriteLine();
            } //GraphDemonstration

            Console.WriteLine("Ready to connect and call first method remotely... To quit, press any key...");
            try {
                SimpleDemonstration();
                GraphDemonstration();
            } catch (System.Exception e) {
                Console.WriteLine($"Is the server started?\n{e.GetType().Name}:\n{e.Message}");
            } //exception

            System.Console.ReadKey(true);

        } //Main

    } //class Entry

}
