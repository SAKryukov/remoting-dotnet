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
    using System.IO;
    using DataContractSerializer = System.Runtime.Serialization.DataContractSerializer;

    class Entry {

        static void Main() {
            var remotingClient = new Remoting.Client<ITestContract>(
                "localhost",
                Remoting.DefinitionSet.PortAssignmentsIANA.DynamicPrivatePorts.First);
            void TestDataContract() {
                var graph = DirectedGraph.GraphSample();
                remotingClient.Implementation.Insert(graph, graph.AccessNode, new Node("new!!"));
            } //TestDataContract()
            TestDataContract();
            Console.WriteLine("Ready to connect and call first method remotely... To quit, press any key...");
            try {
                using Remoting.ICooperative partner = remotingClient.Partner;
                remotingClient.Implementation.P = "My property P value";
                Console.WriteLine(remotingClient.Implementation.P);
                remotingClient.Implementation.B(1, 2);
                var a = remotingClient.Implementation.A(3, 11);
                Console.WriteLine(a);
                a = remotingClient.Implementation.A(13, 'b');
                Console.WriteLine(a);
                partner.Yield();
                a = remotingClient.Implementation.A("This is", 1313);
                Console.WriteLine(a);
                a = remotingClient.Implementation.A("Just line");
                Console.WriteLine(a);
                remotingClient.Implementation.B(2, 1);
                Console.WriteLine("B(int, int) called");
                Console.WriteLine("Done!");
            } catch (System.Exception e) {
                Console.WriteLine($"Is the server started?\n{e.GetType().Name}:\n{e.Message}");
            }
            System.Console.ReadKey(true);
        } //Main

    } //class Entry

}