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

    class Implementation : ITestContract {
        string ITestContract.A(int a, int b) {
            return $"First A: a: {a}, b:{b}, a+b: {a+b} ";
        }
        string ITestContract.P {
            get { return pValue; }
            set { pValue = value; }
        }
        public string A(string a, int b) {
            return $"Second A: a: {a}, b:{b}";
        }
        string pValue = null;
        string ITestContract.A(string a) {
            return $"Third A: a: {a}";
        }
        void ITestContract.B(int a, int b) {
            Console.WriteLine($"B(int, int) called; a: {a}, b:{b}");
        }
        DirectedGraph ITestContract.Connect(DirectedGraph graph, Node tail, Node head) {
            tail.Next = head;
            return graph;
        }
        DirectedGraph ITestContract.Break(DirectedGraph graph, Node tail, Node head) {
            if (head == tail.Next)
                tail.Next = null;
            return graph;
        }
        DirectedGraph ITestContract.Insert(DirectedGraph graph, Node tail, Node head) {
            head.Next = tail.Next;
            tail.Next = head;
            return graph;
        }
    }; //class Implementation

    class Entry {

        static string ClientNumber(int number) {
            string subject = number == 1 ? "client" : "clients";
            return $"{number} {subject}";
        }

        static void Main() {
            var server = new Remoting.Server<ITestContract, Implementation>(Remoting.DefinitionSet.PortAssignmentsIANA.DynamicPrivatePorts.First, new Implementation());
            server.Connected += (sender, eventArgs) => {
                Console.WriteLine($"Client connected, serving {ClientNumber(eventArgs.ClientCount)}");
            };
            server.Disconnected += (sender, eventArgs) => {
                Console.WriteLine($"Client disconnected, serving {ClientNumber(eventArgs.ClientCount)}");
            };
            server.ExecutionPhaseChanged += (sender, eventArgs) => {
                Console.WriteLine($"Server {eventArgs.Phase}");
            };
            server.Start();
            Console.WriteLine("Listening... To stop, press any key...");
            System.Console.ReadKey(true);
            server.Stop();
        } //Main

    } //class Entry

}