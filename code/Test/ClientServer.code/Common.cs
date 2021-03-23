// Remoting for .NET Core, .NET 5, and later
//
// Copyright (c) Sergey A Kryukov, 2017-2021
//
// http://www.SAKryukov.org
// http://www.codeproject.com/Members/SAKryukov
// https://github.com/SAKryukov
//

namespace Test {
    using System.Runtime.Serialization;
    using Next = System.Collections.Generic.List<Node>;
    using NodeSet = System.Collections.Generic.HashSet<Node>;

    [KnownType(typeof(Node))]
    [DataContract(Namespace = "https://www.SAKryukov.org/schema/Remoting.NET", IsReference = true)]
    class DirectedGraph {
        internal DirectedGraph() { }
        internal DirectedGraph(string name) { AccessNode = new Node(name); }
        [DataMember]
        internal Node AccessNode;
        internal static void Visualize(DirectedGraph graph) {
            NodeSet nodeSet = new();
            Visualize(graph.AccessNode);
            void Visualize(Node node) {
                System.Console.Write($"{node.Name}");
                if (nodeSet.Contains(node)) return;
                nodeSet.Add(node);
                System.Console.Write(" > ");
                if (node.Next.Count > 1)
                    System.Console.Write("(");
                for (var index = 0; index < node.Next.Count; ++index) {
                    Visualize(node.Next[index]);
                    if (index < node.Next.Count - 1)
                        System.Console.Write(", ");
                }
                if (node.Next.Count > 1)
                    System.Console.Write(")");
            }
        } //Visualize
        internal static DirectedGraph GraphSample() {
            DirectedGraph graph = new("first");
            second = graph.AccessNode.Add("second");
            third = second.Add("third");
            fourth = third.Add("fourth");
            fourth.Next.Add(second);
            third.Next.Add(graph.AccessNode);
            return graph;
        } //GraphSample
        internal static Node second, third, fourth;
    } //DirectedGraph

    [KnownType(typeof(Node))]
    [DataContract(Namespace = "https://www.SAKryukov.org/schema/Remoting.NET", IsReference = true)]
    class Node {
        internal Node() { }
        internal Node(string name) { Name = name; }
        internal Node Add(string name) {
            Node newNode = new(name);
            Next.Add(newNode);
            return newNode;
        }
        internal Next Next { get { return next; } }
        [DataMember]
        Next next = new();
        [DataMember]
        internal string Name;
    }

    interface ITestContract : Remoting.IContract {
        string A(int a, int b);
        string P { get; set; }
        string A(string a, int b);
        string A(string a);
        void B(int a, int b);
        DirectedGraph Stich(DirectedGraph graph, Node tail, Node head);
        DirectedGraph Break(DirectedGraph graph, Node tail, Node head);
        DirectedGraph Insert(DirectedGraph graph, Node tail, Node head);
    }    

}