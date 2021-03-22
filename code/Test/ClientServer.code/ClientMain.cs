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
            //remotingClient.Implementation.B(13, 33);
            var a = remotingClient.Implementation.A(3, 11);
            Console.WriteLine(a);
            a = remotingClient.Implementation.A(13, 111);
            Console.WriteLine(a);
            Console.WriteLine("Done!");
            System.Console.ReadKey();
        } //Main

    } //class Entry

}