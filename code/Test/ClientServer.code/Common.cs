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
    //using 
    
    [DataContract(Namespace = "https://www.SAKryukov.org/schema/Remoting.NET")]
    class Graph {
    }

    [DataContract(Namespace = "https://www.SAKryukov.org/schema/Remoting.NET")]
    class Node {

    }

    interface ITestContract : Remoting.IContract {
        string A(int a, int b);
        string P { get; set; }
        string A(string a, int b);
        string A(string a);
        void B(int a, int b);
    }    

}