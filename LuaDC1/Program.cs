using System;
using System.IO;
using System.Threading;

namespace LuaDC1
{
    class Program
    {
        public static readonly string[] vals = { "CREATETABLE","PUSHINT","PUSHSTRING","SETMAP","GETGLOBAL","CALL","ADDI","GETLOCAL",
         "SETTABLE","SETLOCAL","PUSHNIL","END",};
        static void Main(string[] args)
        {

            

            Console.WriteLine("Hello World!");
            string filename = "addme.txt";
            string newname = filename.Substring(0, filename.Length - 4);
            string[] lines = System.IO.File.ReadAllLines(filename); //each individual line

            StreamWriter sw = new StreamWriter(newname);

            string luafile = String.Concat("--generated from Phantom's program",System.Environment.NewLine);

            int linecounter = 0;
            int tblDECL = 0;
            int tblVarStatic = 0;
            int tblCounter = 0;
            int localcounter = 0;
            int globalcounter = 0;
            int globalcalledlast = 0;

            string line;
            string[] localvariablelist = { };
            string[] parser = { };

            //foreach (string line in lines)
            for (int i = 0; i < lines.GetLength(0); i++) //used i to be able to hop around and iterate through lines a little better
            {
                line = lines[i];
                foreach (string x in vals)
                {
                    if (line.Contains(x))
                    {
                        int index = line.IndexOf(x);
                        string newline = line.Substring(index);
                        Console.WriteLine(newline);
                        parser = newline.Split(new char[0], StringSplitOptions.RemoveEmptyEntries);
                       
                        if (parser.GetLength(0) > 4)
                        {
                            for (int j = 4; j < parser.GetLength(0); j++){
                                parser[3] = String.Concat(parser[3], " ", parser[j]);
                            }
                        }
                        
                        switch (parser[0])
                        {
                            case "CREATETABLE":
                                luafile = String.Concat(luafile,System.Environment.NewLine,"local table", tblCounter, " = { ");
                                Console.WriteLine(String.Concat("local table",tblCounter," = { "));
                                //localvariablelist[localcounter] = String.Concat("table", tblCounter);
                                int tblVals = int.Parse(parser[1]) * 2; //table and value associated with it
                                tblDECL = 1;
                                tblVarStatic = 1;
                                localcounter += 1;
                                globalcounter += 1;
                                tblCounter += 1;
                                Console.WriteLine("Test");
                                break;
                            case "PUSHSTRING":
                                string finalstr = parser[3].Replace("\"",string.Empty);
                                if (tblDECL == 1) {
                                    if (tblVarStatic == 1)
                                    {
                                        luafile = (String.Concat(luafile,finalstr, " = "));
                                        Console.WriteLine(String.Concat(finalstr, " = "));
                                        tblVarStatic = 0;
                                        break;
                                    }
                                    else
                                    {
                                        luafile = (String.Concat(luafile, "\"", finalstr, "\", "));
                                        Console.WriteLine(String.Concat("\"",finalstr,"\", "));
                                        tblVarStatic = 1;
                                        break;
                                    }
                                 }
                                else if (tblDECL == 0 & globalcalledlast == 1)
                                {
                                    luafile = (String.Concat(luafile, "\"", finalstr, "\","));
                                    Console.WriteLine(String.Concat("\"", finalstr, "\","));
                                    break;
                                }
                                else
                                {
                                    luafile = (String.Concat(luafile, "\"", finalstr, "\""));
                                    Console.WriteLine(String.Concat("\"", finalstr, "\""));
                                    break;
                                }
                            case "PUSHINT":
                                if (tblDECL == 1)
                                {
                                    if (tblVarStatic == 1)
                                    {
                                        luafile = String.Concat(luafile, parser[1], " = ");
                                        Console.WriteLine(String.Concat(parser[1], " = "));
                                        tblVarStatic = 0;
                                        break;
                                    }
                                    else
                                    {
                                        luafile = String.Concat(luafile, parser[1], ", ");
                                        Console.WriteLine(String.Concat(parser[1], ", "));
                                        tblVarStatic = 1;
                                        break;
                                    }
                                }
                                else
                                {
                                    luafile = String.Concat(luafile, parser[1]);
                                    Console.WriteLine(String.Concat(parser[1]));
                                    break;
                                }
                            case "SETMAP":
                                luafile = String.Concat(luafile,"}", System.Environment.NewLine);
                                Console.WriteLine("}");
                                tblDECL = 0;
                                tblVarStatic = 0;
                                break;
                            case "GETGLOBAL":
                                if (globalcalledlast == 0) {
                                    Console.WriteLine(String.Concat(parser[3],"("));
                                    globalcalledlast = 1;
                                }
                                else if (globalcalledlast == 1)
                                {
                                    Console.WriteLine(String.Concat(parser[3], ")"));
                                    globalcalledlast = 0;
                                }
                                break;
                            case "CALL":
                                luafile = String.Concat(luafile.TrimEnd(','),")", System.Environment.NewLine);
                                Console.WriteLine(")");

                                globalcalledlast = 0;
                                break;
                            case "ADDI":
                                break;
                            case "SETTABLE":
                                break;
                            case "SETLOCAL":

                                break;
                            case "PUSHNIL":
                                Console.WriteLine("nil");
                                int numnil = int.Parse(parser[1]);
                                for (int k = 0; k <= numnil; k++)
                                {
                                    if (k == 0 & numnil > 0)
                                    {
                                        luafile = String.Concat(luafile,"local local",localcounter,",");
                                        localcounter += 1;
                                    }
                                    else if (k == 0 & numnil == 0)
                                    {
                                        luafile = String.Concat(luafile, "local local", localcounter, " = nil");
                                        localcounter += 1;
                                    }
                                    else if (k < numnil & k != 0)
                                    {
                                        luafile = String.Concat(luafile, "local", localcounter, ",");
                                        localcounter += 1;
                                    }
                                    else 
                                    {
                                        luafile = String.Concat(luafile, "local", localcounter, " = nil");
                                        localcounter += 1;
                                    }
                                }
                                break;
                            case "END":
                                tblDECL = 0;
                                tblVarStatic = 0;
                                localcounter = 0;
                                //determine write order
                                break;

                        }
                        linecounter = linecounter + 1;
                    }
                }
            }

        }
    }
}
