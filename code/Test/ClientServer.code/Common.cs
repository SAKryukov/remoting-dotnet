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
    using StringBuilder = System.Text.StringBuilder;
    using NodeSet = System.Collections.Generic.HashSet<Node>;

    [DataContract(Namespace = "https://www.SAKryukov.org/schema/Remoting.NET", IsReference = true)]
    class Node {
        internal Node() { }
        internal Node(string name) { Name = name; }
        [DataMember]
        internal Node Next;
        [DataMember]
        internal string Name;
    } //class Node

    [DataContract(Namespace = "https://www.SAKryukov.org/schema/Remoting.NET", IsReference = true)]
    class DirectedGraph {
        internal DirectedGraph() { }
        internal DirectedGraph(string name) { AccessNode = Add(name); }
        internal Node Add(string name) {
            Node node = new(name);
            NodeSet.Add(node);
            return node;
        }
        [DataMember]
        readonly NodeSet nodeSet = new();    
        NodeSet NodeSet { get { return nodeSet; } }
        [DataMember]
        internal Node AccessNode;
        internal string Visualize() {
            NodeSet visualizedSet = new();
            string VisualizeChain(Node node) {
                StringBuilder sb = new();
                while (node != null) {
                    sb.Append($"{node.Name} > ");
                    if (visualizedSet.Contains(node)) {
                        sb.Append($"...");
                        break;
                    } else
                        visualizedSet.Add(node);
                    node = node.Next;
                }
                return sb.ToString();
            }
            StringBuilder sb = new();
            foreach (var node in NodeSet) {
                if (visualizedSet.Contains(node)) continue;
                sb.Append($"[ {VisualizeChain(node)} ] ");
                visualizedSet.Add(node);
            }
            return sb.ToString();
        } //Visualize
        internal static DirectedGraph DemoSample {
            get {
                DirectedGraph graph = new("first");
                second = graph.Add("second");
                third = graph.Add("third");
                fourth = graph.Add("fourth");
                graph.AccessNode.Next = second;
                second.Next = third;
                third.Next = fourth;
                fourth.Next = graph.AccessNode;
                return graph;
            }
        } //GraphSample
        internal static Node second, third, fourth;
    } //DirectedGraph

    [DataContract(Namespace = "https://www.SAKryukov.org/schema/Remoting.NET", IsReference = true)]
    class DynamicParameter {
        internal DynamicParameter(int c) { this.c = c; }
        [DataMember]
        public int c;
    }

    interface IDynamicTest : Remoting.IDynamic {
        int DynamicTestMethod(int b, DynamicParameter c);
    } //interface IDynamicTest

    interface ITestContract {
        IDynamicTest DynamicTestMethod();
        string A(int a, int b);
        string P { get; set; }
        string A(string a, int b);
        string A(string a);
        void B(int a, int b);
        DirectedGraph Connect(DirectedGraph graph, Node tail, Node head);
        DirectedGraph Disconnect(DirectedGraph graph, Node tail, Node head);
        DirectedGraph Insert(DirectedGraph graph, Node tail, Node head);
    } //interface ITestContract

}
