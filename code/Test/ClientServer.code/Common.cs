// Remoting for .NET Core, .NET 5, and later
//
// Copyright (c) Sergey A Kryukov, 2017-2021
//
// http://www.SAKryukov.org
// http://www.codeproject.com/Members/SAKryukov
// https://github.com/SAKryukov
//

namespace Test {


    interface ITestContract : Remoting.IContract {
        string A(int a, char b);
        string P { get; set; }
        string A(string a, int b, int c);
        /*
        string A(string a);
        */
        void B(int a, int b);
    }

}