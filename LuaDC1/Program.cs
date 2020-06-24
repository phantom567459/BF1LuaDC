using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;

namespace LuaDC1
{
    class Program
    {
        public static readonly string[] vals = { "function","CREATETABLE","PUSHINT","PUSHNUM","PUSHNEGNUM","PUSHSTRING","SETMAP","GETGLOBAL","GETINDEXED","JMP",
        "JMPF","JMPT","JMPE","JMPNE","FORLOOP","FORPREP","SETGLOBAL","CALL","ADDI","SUB","GETLOCAL","GETDOTTED","RETURN","POP",
         "SETTABLE","SETLOCAL","SETLIST","PUSHNIL","CLOSURE","END",};
        static void Main(string[] args)
        {

            //TODO: SETLIST

            Console.WriteLine("Hello World!");
            string filename = "kam1c.txt";
            string newname = String.Concat(filename.Substring(0, filename.Length - 4),".lua");
            string[] lines = System.IO.File.ReadAllLines(filename); //each individual line

           // StreamWriter sw = new StreamWriter(newname);

            string luafile = String.Concat("--generated from Phantom's program",System.Environment.NewLine);

            int linecounter = 0;
            int tblDECL = 0;

            int tblCounter = 0;
            int localcounter = 0;
            int globalcounter = 0;
            int globalcalledlast = 0;
            int globalastable = 0;
            int withincall = 0;
            //int insidetable = 0;
            int functioncounter = -1;
            int opentables = 0;
            int elementCounter = 0;
            bool storefunctionname = false;
            bool insidefunction = false;

            string line;

            List<string> localvariablelist = new List<string>();
            List<string> functionnamelist = new List<string>();
            List<int> tblVarStatic = new List<int>();
            List<bool> tblswap = new List<bool>();
            List<bool> tblisglobal = new List<bool>();
            List<string> tblGlobalNames = new List<string>();
            List<int> tblElements = new List<int>();

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
                        //Console.WriteLine(newline); //only turn on for debug
                        parser = newline.Split(new char[0], StringSplitOptions.RemoveEmptyEntries);
                       
                        if (parser.GetLength(0) > 4 & line.Contains("\""))
                        {
                            for (int j = 4; j < parser.GetLength(0); j++){
                                parser[3] = String.Concat(parser[3], " ", parser[j]);
                            }
                        }

                        switch (parser[0])
                        {
                            case "function":
                                insidefunction = true;
                                luafile = String.Concat(luafile, "function ", functionnamelist[0], "(");
                                //do params
                                int paramnum = int.Parse(lines[i + 1].Substring(0, 1));
                                //Console.WriteLine(String.Concat("LOOK AT ME # OF PARAMS = ",paramnum));
                                for (int p = 1; p <= paramnum; p++)
                                {
                                    if (p == 1)
                                    {
                                        luafile = String.Concat(luafile, "var", localcounter);
                                        localvariablelist.Add(String.Concat("var", localcounter));
                                        localcounter += 1;
                                    }
                                    else
                                    {
                                        luafile = String.Concat(luafile, ",var", localcounter);
                                        localvariablelist.Add(String.Concat("var", localcounter));
                                        localcounter += 1;
                                    }
                                }
                                //something something parameter count, drop in the local var list and have a field day.
                                luafile = String.Concat(luafile, ")");
                                break;
                            case "CREATETABLE":
                                tblElements.Add(int.Parse(parser[1]));
                                if (opentables == 0)
                                {
                                    int internalopentables = 0;
                                    for (int q = i; q < lines.GetLength(0); q++)
                                    {
                                        string linex = lines[q];


                                        if (linex.Contains("CREATETABLE"))
                                        {
                                            internalopentables += 1;
                                            Console.WriteLine("TEST");
                                        }
                                        else if (linex.Contains("SETMAP"))
                                        {
                                            internalopentables -= 1;
                                            if (internalopentables == 0)
                                            {
                                                tblVarStatic.Add(1);
                                                tblswap.Add(true);
                                                if (lines[q + 1].Contains("SETGLOBAL"))
                                                {
                                                    tblisglobal.Add(true);
                                                    int internalindex = internalindex = lines[q + 1].IndexOf("SETGLOBAL");
                                                    string internalnewline = lines[q + 1].Substring(index);
                                                    string[] internalparse = internalnewline.Split(new char[0], StringSplitOptions.RemoveEmptyEntries);
                                                    tblGlobalNames.Add(internalparse[3]);

                                                }
                                                else
                                                {
                                                    tblisglobal.Add(false);
                                                    tblGlobalNames.Add("");
                                                }
                                                break;
                                            }
                                        }
                                        else if (linex.Contains("SETLIST"))
                                        {
                                           // if (!linex.Contains(tblElements[opentables - 1].ToString()))
                                            //{
                                           //     elementCounter += int.Parse(parser[2]);
                                           // }
                                           // else
                                            //{
                                                internalopentables -= 1;
                                                if (internalopentables == 0)
                                                {
                                                    tblVarStatic.Add(0);
                                                    tblswap.Add(false);
                                                    if (lines[q + 1].Contains("SETGLOBAL"))
                                                    {
                                                        tblisglobal.Add(true);
                                                        int internalindex = internalindex = lines[q + 1].IndexOf("SETGLOBAL");
                                                        string internalnewline = lines[q + 1].Substring(index);
                                                        string[] internalparse = internalnewline.Split(new char[0], StringSplitOptions.RemoveEmptyEntries);
                                                        tblGlobalNames.Add(internalparse[3]);

                                                    }
                                                    else
                                                    {
                                                        tblisglobal.Add(false);
                                                        tblGlobalNames.Add("x");
                                                    }
                                                    break;
                                                }
                                           // }
                                        }

                                    }
                                    tblDECL = 1;
                                    globalcounter += 1;
                                    opentables += 1;
                                    tblCounter += 1;

                                    if (tblisglobal[opentables - 1] == false)
                                    {
                                        luafile = String.Concat(luafile, System.Environment.NewLine, "local table", tblCounter, " = { ");
                                        Console.WriteLine(String.Concat("local table", tblCounter, " = { "));
                                        localvariablelist.Add(String.Concat("table", tblCounter));
                                        localcounter += 1;
                                    }
                                    else
                                    {
                                        luafile = String.Concat(luafile, System.Environment.NewLine, tblGlobalNames[opentables-1], " = { ");
                                        Console.WriteLine(String.Concat(tblGlobalNames[opentables - 1], " = { "));
                                    }
                                    //int tblVals = int.Parse(parser[1]) * 2; //table and value associated with it

                                }
                                else if (opentables >= 1)
                                {
                                    luafile = String.Concat(luafile, System.Environment.NewLine, "{");
                                    Console.WriteLine(String.Concat("{"));

                                    int internalopentables = 0;
                                    for (int q = i; q < lines.GetLength(0); q++)
                                    {
                                        string linex = lines[q];

                                        if (linex.Contains("CREATETABLE"))
                                        {
                                            internalopentables += 1;
                                        }
                                        else if (linex.Contains("SETMAP"))
                                        {
                                            internalopentables -= 1;
                                            if (internalopentables == 0)
                                            {
                                                tblVarStatic.Add(1);
                                                tblswap.Add(true);
                                                tblisglobal.Add(false);
                                                tblGlobalNames.Add("");
                                                break;
                                            }
                                        }
                                        else if (linex.Contains("SETLIST"))
                                        {
                                            internalopentables -= 1;
                                            if (internalopentables == 0)
                                            {
                                                tblVarStatic.Add(0);
                                                tblswap.Add(false);
                                                tblisglobal.Add(false);
                                                tblGlobalNames.Add("");
                                                break;
                                            }
                                        }
                                    }
                                    opentables += 1;
                                    tblCounter += 1;
                                }
                                break;
                            case "PUSHSTRING":
                                string finalstr = parser[3].Replace("\"", string.Empty);
                                if (tblDECL == 1) {
                                    if (tblVarStatic[opentables-1] == 1)
                                    {
                                        luafile = (String.Concat(luafile, finalstr, " = "));
                                        Console.WriteLine(String.Concat(finalstr, " = "));
                                        if (tblswap[opentables - 1] == true)
                                        {
                                            tblVarStatic[opentables - 1] = 0;
                                        }
                                        break;
                                    }
                                    else
                                    {
                                        luafile = (String.Concat(luafile, "\"", finalstr, "\", "));
                                        Console.WriteLine(String.Concat("\"", finalstr, "\", "));
                                        if (tblswap[opentables-1] == true)
                                        {
                                            tblVarStatic[opentables-1] = 1;
                                        }
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
                                    //it's a new local variable
                                    luafile = (String.Concat(luafile, System.Environment.NewLine, "local var", localcounter, "=\"", finalstr, "\""));
                                    localvariablelist.Add(String.Concat("var", localcounter));
                                    localcounter += 1;
                                    Console.WriteLine(String.Concat("local var", localcounter, "=\"", finalstr, "\""));
                                    break;
                                }
                            case "PUSHINT":
                                if (tblDECL == 1)
                                {
                                    if (tblVarStatic[opentables-1] == 1)
                                    {
                                        luafile = String.Concat(luafile, parser[1], " = ");
                                        Console.WriteLine(String.Concat(parser[1], " = "));
                                        if (tblswap[opentables - 1] == true)
                                        {
                                            tblVarStatic[opentables - 1] = 0;
                                        }
                                        break;
                                    }
                                    else
                                    {
                                        luafile = String.Concat(luafile, parser[1], ", ");
                                        Console.WriteLine(String.Concat(parser[1], ", "));
                                        if (tblswap[opentables-1] == true)
                                        {
                                            tblVarStatic[opentables-1] = 1;
                                        }
                                        break;
                                    }
                                }
                                else if (tblDECL == 0 & globalcalledlast == 1) {
                                    luafile = String.Concat(luafile, parser[1], ",");
                                    Console.WriteLine(parser[1], ",");
                                    break;
                                }
                                else
                                {
                                    luafile = String.Concat(luafile, System.Environment.NewLine, "local var", localcounter, "=", parser[1]);
                                    localvariablelist.Add(String.Concat("var", localcounter));
                                    localcounter += 1;
                                    Console.WriteLine(String.Concat("local var", localcounter, "=", parser[1]));
                                    break;
                                }
                            case "PUSHNUM":
                                if (tblDECL == 1)
                                {
                                    if (tblVarStatic[opentables-1] == 1)
                                    {
                                        luafile = String.Concat(luafile, parser[3], " = ");
                                        Console.WriteLine(String.Concat(parser[3], " = "));
                                        if (tblswap[opentables - 1] == true)
                                        {
                                            tblVarStatic[opentables - 1] = 0;
                                        }
                                        break;
                                    }
                                    else
                                    {
                                        luafile = String.Concat(luafile, parser[3], ", ");
                                        Console.WriteLine(String.Concat(parser[3], ", "));
                                        if (tblswap[opentables-1] == true)
                                        {
                                            tblVarStatic[opentables-1] = 1;
                                        }
                                        break;
                                    }
                                }
                                else if (tblDECL == 0 & globalcalledlast == 1)
                                {
                                    luafile = String.Concat(luafile, parser[3], ",");
                                    Console.WriteLine(parser[3], ",");
                                    break;
                                }
                                else
                                {
                                    luafile = String.Concat(luafile, System.Environment.NewLine, "local var", localcounter, "=", parser[3]);
                                    localvariablelist.Add(String.Concat("var", localcounter));
                                    localcounter += 1;
                                    Console.WriteLine(String.Concat("local var", localcounter, "=", parser[3]));
                                    break;
                                }
                            case "PUSHNEGNUM":
                                if (tblDECL == 1)
                                {
                                    if (tblVarStatic[opentables-1] == 1)
                                    {
                                        luafile = String.Concat(luafile, "-", parser[3], " = ");
                                        Console.WriteLine(String.Concat("-", parser[3], " = "));
                                        if (tblswap[opentables - 1] == true)
                                        {
                                            tblVarStatic[opentables - 1] = 0;
                                        }
                                        break;
                                    }
                                    else
                                    {
                                        luafile = String.Concat(luafile, "-", parser[3], ", ");
                                        Console.WriteLine(String.Concat("-", parser[3], ", "));
                                        if (tblswap[opentables-1] == true)
                                        {
                                            tblVarStatic[opentables-1] = 1;
                                        }
                                        break;
                                    }
                                }
                                else if (tblDECL == 0 & globalcalledlast == 1)
                                {
                                    luafile = String.Concat(luafile, "-", parser[3], ",");
                                    Console.WriteLine(parser[3], ",");
                                    break;
                                }
                                else
                                {
                                    luafile = String.Concat(luafile, System.Environment.NewLine, "local var", localcounter, "= -", parser[3]);
                                    localvariablelist.Add(String.Concat("var", localcounter));
                                    localcounter += 1;
                                    Console.WriteLine(String.Concat("local var", localcounter, "= -", parser[3]));
                                    break;
                                }

                            case "SETMAP":
                                luafile = String.Concat(luafile, "}", System.Environment.NewLine);
                                Console.WriteLine("}");
                                //tblVarStatic[opentables-1] = 0;
                                opentables -= 1;
                                if (opentables == 0) 
                                { 
                                    tblDECL = 0;
                                    tblVarStatic.Clear();
                                    tblGlobalNames.Clear();
                                    tblisglobal.Clear();
                                }
                                break;
                            case "SETLIST":
                                elementCounter += int.Parse(parser[2]);
                                if (tblElements[opentables - 1] != int.Parse(parser[2]))
                                {
                                }
                                else
                                {
                                    luafile = String.Concat(luafile, "}", System.Environment.NewLine);
                                    Console.WriteLine("}");
                                    tblDECL = 0;
                                    elementCounter = 0;
                                    //tblVarStatic[opentables-1] = 0;
                                    opentables -= 1;
                                    if (opentables == 0)
                                    {
                                        tblDECL = 0;
                                        tblVarStatic.Clear();
                                        tblGlobalNames.Clear();
                                        tblisglobal.Clear();
                                    }
                                }
                                break;
                            case "GETGLOBAL":
                                if (globalcalledlast == 0) {
                                    for (int q = i; q < lines.GetLength(0); q++)
                                    {
                                        string linex = lines[q];
                                        if (linex.Contains("SETTABLE"))
                                        {
                                            //Console.WriteLine(String.Concat("local var", localcounter, "=", parser[3], "("));
                                            //set global as a table rather than just a var argument
                                            globalastable = 1;
                                            break;
                                        }
                                        else if (linex.Contains("CALL"))
                                        {
                                            int internalindex = lines[q].IndexOf("CALL");
                                            string internalnewline = lines[q].Substring(index);
                                            string[] internalparse = internalnewline.Split(new char[0], StringSplitOptions.RemoveEmptyEntries);

                                            int numoflocs = int.Parse(internalparse[2]);
                                            if (numoflocs == 0)
                                            {
                                                break;
                                            }
                                            else 
                                            {
                                                if (lines[q+1].Contains("SETLOCAL"))
                                                {
                                                    internalindex = internalindex = lines[q+1].IndexOf("SETLOCAL");
                                                    internalnewline = lines[q+1].Substring(index);
                                                    internalparse = internalnewline.Split(new char[0], StringSplitOptions.RemoveEmptyEntries);

                                                    luafile = String.Concat(luafile, localvariablelist[int.Parse(internalparse[1])], "=");
                                                    Console.WriteLine(localvariablelist[int.Parse(internalparse[1])], "=");
                                                    break;
                                                }
                                            }

                                            for (int q1 = 0; q1 < numoflocs; q1++)
                                            {
                                                if (q1 == 0)
                                                {
                                                    Console.WriteLine("local var", localcounter);
                                                    luafile = String.Concat(luafile, System.Environment.NewLine, "local var", localcounter);
                                                    localvariablelist.Add(String.Concat("var", localcounter));
                                                    localcounter += 1;
                                                }
                                                else
                                                {
                                                    Console.WriteLine(",var", localcounter);
                                                    luafile = String.Concat(luafile, ",var", localcounter);
                                                    localvariablelist.Add(String.Concat("var", localcounter));
                                                    localcounter += 1;
                                                }
                                            }
                                            Console.Write("=");
                                            luafile = String.Concat(luafile, "=");
                                            withincall += 1;
                                            break;
                                        }

                                    }
                                    if (globalastable == 1) 
                                    {
                                        //accept args
                                        Console.WriteLine(String.Concat(parser[3], "["));
                                        luafile = String.Concat(luafile, parser[3], "[");
                                        globalcalledlast = 1;
                                    }
                                    else
                                    {
                                        if (withincall > 1)
                                        {
                                            Console.WriteLine(parser[3]);
                                            luafile = String.Concat(luafile, parser[3]);
                                            globalcalledlast = 1;
                                        }
                                        else
                                       {
                                            Console.WriteLine(String.Concat(parser[3], "("));
                                            luafile = String.Concat(luafile, System.Environment.NewLine, parser[3], "(");
                                            globalcalledlast = 1;
                                        }
                                    }
                                }
                                else if (globalcalledlast == 1)
                                {
                                    //if (withincall >= 1)
                                    //{
                                        Console.WriteLine(parser[3]);
                                        luafile = String.Concat(luafile, parser[3]);
                                    //}
                                    //else
                                    //{
                                     //   Console.WriteLine(String.Concat(parser[3], "("));
                                        //luafile = String.Concat(luafile, parser[3], "(");
                                     //   luafile = String.Concat(luafile, parser[3]);
                                    //}
                                }
                                break;
                            case "SETGLOBAL":
                                if (storefunctionname == true)
                                {
                                    functionnamelist.Add(parser[3]);
                                    storefunctionname = false;
                                    functioncounter += 1;
                                    break;
                                }
                                
                                break;
                            case "CALL":
                                luafile = String.Concat(luafile.TrimEnd(','),")");
                                Console.WriteLine(")");

                                globalastable = 0;
                                globalcalledlast = 0;
                                withincall = 0;
                                break;
                            case "ADDI":
                                //Console.WriteLine(luafile.Substring(luafile.Length - 2));
                                if (luafile.Substring(luafile.Length - 2) == "]=")
                                {
                                    luafile = luafile.Remove(luafile.Length -2, 2);
                                    luafile = String.Concat(luafile, "+", parser[1], "]=");
                                    Console.WriteLine(String.Concat("+", parser[1],"]="));
                                }
                                else
                                {
                                    luafile = String.Concat(luafile, "+", parser[1]);
                                    Console.WriteLine(String.Concat("+", parser[1]));
                                }
                               
                                break;
                            case "SETTABLE":
                                luafile = String.Concat(luafile, System.Environment.NewLine);
                                globalastable = 0;
                                globalcalledlast = 0;
                                withincall = 0;
                                break;
                            case "SETLOCAL":
                                luafile = String.Concat(luafile, System.Environment.NewLine);
                                globalastable = 0;
                                globalcalledlast = 0;
                                withincall = 0;
                                break;
                            case "GETLOCAL":
                                luafile = String.Concat(luafile, localvariablelist[int.Parse(parser[1])]);
                                Console.WriteLine(localvariablelist[int.Parse(parser[1])]);
                                if (globalcalledlast == 1 & globalastable == 0)
                                {
                                    luafile = String.Concat(luafile,",");
                                    Console.WriteLine(",");
                                    break;
                                }
                                if (globalastable == 1)
                                {
                                    luafile = String.Concat(luafile, "]=");
                                    globalastable = 0;
                                }
                                break;
                            case "CLOSURE":
                                storefunctionname = true;
                                break;
                            case "PUSHNIL":
                                //Console.WriteLine("nil");
                               
                                //Console.WriteLine(i);
                                if (lines[i+1].Contains("SETLOCAL"))
                                {
                                    
                                    int internalindex = lines[i+1].IndexOf(x);
                                    string internalnewline = lines[i + 1].Substring(index);
                                    string[] internalparse = internalnewline.Split(new char[0], StringSplitOptions.RemoveEmptyEntries);

                                    luafile = String.Concat(luafile,localvariablelist[int.Parse(internalparse[1])],"=nil");
                                    Console.WriteLine(String.Concat(localvariablelist[int.Parse(internalparse[1])], "=nil"));
                                }
                                else
                                {
                                    int numnil = int.Parse(parser[1]);
                                    for (int k = 0; k <= numnil; k++)
                                    {
                                        if (k == 0 & numnil == 0)
                                        {
                                            luafile = String.Concat(luafile, System.Environment.NewLine, "local var", localcounter, " = nil", System.Environment.NewLine);
                                            localvariablelist.Add(String.Concat("var", localcounter));
                                            localcounter += 1;

                                        }
                                        else if (k == 0 & numnil > 0)
                                        {
                                            luafile = String.Concat(luafile, System.Environment.NewLine, "local var", localcounter, ",");
                                            localvariablelist.Add(String.Concat("var", localcounter));
                                            localcounter += 1;
                                        }
                                        else if (k < numnil & k != 0)
                                        {
                                            luafile = String.Concat(luafile, "var", localcounter, ",");
                                            localvariablelist.Add(String.Concat("var", localcounter));
                                            localcounter += 1;
                                        }
                                        else
                                        {
                                            luafile = String.Concat(luafile, "var", localcounter, " = nil", System.Environment.NewLine);
                                            localvariablelist.Add(String.Concat("var", localcounter));
                                            localcounter += 1;
                                        }
                                    }
                                }
                                break;
                            case "END":
                                tblDECL = 0;
                                tblVarStatic.Clear();
                                localcounter = 0;
                                globalastable = 0;
                                globalcalledlast = 0;
                                withincall = 0;
                                localvariablelist.Clear();
                                tblGlobalNames.Clear();
                                tblisglobal.Clear();
                                //determine write order
                                if (insidefunction == true)
                                {
                                    luafile = String.Concat(luafile, System.Environment.NewLine, "end");
                                    insidefunction = false;
                                }
                                break;

                        }
                        linecounter = linecounter + 1;
                    }
                }
            }
            //lol @lua for needing an escape character
            luafile = luafile.Replace("\\","\\\\");
            System.IO.File.WriteAllText(newname, luafile);
        }
    }
}
