:: It is really weird to have the executable output broken up into multiple parts
:: this command will compile the program into a single .exe

:: you may not have 'csc.exe' at the location used below; if not you can locatate one on your machine.
:: The following locations are common for the c# compiler to live:
:: C:\Windows\Microsoft.NET\Framework\v2.0.50727\csc.exe
:: C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe
:: C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
:: C:\Windows\Microsoft.NET\Framework64\v2.0.50727\csc.exe
:: C:\Windows\Microsoft.NET\Framework64\v3.5\csc.exe
:: C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

:: without targeting .netCore, compiling to a 32-bit program allows for the most broad use

C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe /out:LuaDC1.exe *.cs
