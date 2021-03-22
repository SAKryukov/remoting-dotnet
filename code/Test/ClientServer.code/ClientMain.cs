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
            Console.WriteLine("Ready to connect and call first method remotely... To quit, press any key...");
            try {
                /*
                remotingClient.Implementation.P = "my value";
                Console.WriteLine(remotingClient.Implementation.P);
                remotingClient.Implementation.B(1, 2);
                var a = remotingClient.Implementation.A(3, 11);
                Console.WriteLine(a);
                */
                var a = remotingClient.Implementation.A(13, 'b');
                Console.WriteLine(a);
                /*
                a = remotingClient.Implementation.A("This is", 1313);
                Console.WriteLine(a);
                a = remotingClient.Implementation.A("Just line");
                Console.WriteLine(a);
                */
                Console.WriteLine("Done!");
            } catch (System.Exception e) {
                Console.WriteLine($"Is the server started?\n{e.GetType().Name}:\n{e.Message}");
            }
            System.Console.ReadKey(true);
        } //Main

    } //class Entry

}