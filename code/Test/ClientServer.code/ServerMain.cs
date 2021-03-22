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
            return $"a: {a}, b:{b}, a+b: {a+b}";
        }
    };

    class Entry {

        static void Main() {
            var server = new Remoting.Server<ITestContract, Implementation>(Remoting.DefinitionSet.PortAssignmentsIANA.DynamicPrivatePorts.First, new Implementation());
            server.Start();
            System.Console.ReadKey();
        }

    } //class Entry

}