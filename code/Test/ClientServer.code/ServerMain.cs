﻿// Remoting for .NET Core, .NET 5, and later
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
        string ITestContract.A(int a, char b) {
            return $"First A: a: {a}, b:{b}";
        }
        string ITestContract.P {
            get { return pValue; }
            set { pValue = value; }
        }
        public string A(string a, int b, int c) {
            return $"Second A: a: {a}, b:{b}";
        }
        string pValue = null;
        /*
        string ITestContract.A(string a) {
            return $"Third A: a: {a}";
        }
        */
        void ITestContract.B(int a, int b) {
            Console.WriteLine("B called.");
        }
    };

    class Entry {

        static void Main() {
            var server = new Remoting.Server<ITestContract, Implementation>(Remoting.DefinitionSet.PortAssignmentsIANA.DynamicPrivatePorts.First, new Implementation());
            server.Start();
            Console.WriteLine("Listening... To stop, press any key...");
            System.Console.ReadKey(true);
            server.Stop();
        }

    } //class Entry

}