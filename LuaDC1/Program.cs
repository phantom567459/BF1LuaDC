using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;

namespace LuaDC1
{
    class Program
    {
        public static readonly string[] vals = { "function","CREATETABLE","PUSHUPVALUE","PUSHINT","PUSHNUM","PUSHNEGNUM","PUSHSTRING","PUSHSELF",
            "SETMAP","SETLIST","GETGLOBAL","GETINDEXED","SETGLOBAL","CALL","RETURN", //this part is ordered correctly with cases below, everything afterward needs reordered to find cases easier
            "JMP","JMPF","JMPT","JMPLT","JMPLE","JMPGT","JMPGE","JMPONT","JMPONF","JMPEQ","JMPNE","PUSHNILJMP","FORLOOP","FORPREP","LFORPREP","LFORLOOP","NOT",
            "TAILCALL","ADD","MULT","DIV","POW","CONCAT","MINUS","ADDI","SUB","GETLOCAL","GETDOTTED","RETURN","POP",
            "SETTABLE","SETLOCAL","PUSHNIL","CLOSURE","END",};
        static void Main(string[] args)
        {

            //TODO: SETLIST

            Console.WriteLine("Hello World!");
            string filename = "missionlist.txt"; //file to read
            string newname = String.Concat(filename.Substring(0, filename.Length - 4),".lua");
            string[] lines = System.IO.File.ReadAllLines(filename); //each individual line

            // StreamWriter sw = new StreamWriter(newname);

            //luafile is the overarching variable that will write to file at the very end
            string luafile = String.Concat("--generated from Phantom's program",System.Environment.NewLine); 


            //set ALL the variables
            int linecounter = 0;
            int tblDECL = 0;

            int tblCounter = 0;
            int localCounter = 0;
            int globalCounter = 0;
            int globalcalledlast = 0;
            int globalastable = 0;
            int withincall = 0;
            //int insidetable = 0;
            int functioncounter = -1;
            int opentables = 0;
            int internalelementcounter = 0;
            int elementCounter = 0;
            bool storefunctionname = false;
            bool insidefunction = false;
            bool storeglobaltable = true;

            string line;

            //initialize some lists and arrays
            List<string> localvariablelist = new List<string>();
            List<string> functionnamelist = new List<string>();
            List<int> tblVarStatic = new List<int>();
            List<bool> tblswap = new List<bool>();
            List<bool> tblisglobal = new List<bool>();
            List<string> tblGlobalNames = new List<string>();
            List<int> tblElements = new List<int>();
            List<string> globalVars = new List<string>();

            string[] parser = { };

            for (int i = 0; i < lines.GetLength(0); i++) //used i to be able to hop around and iterate through lines a little better
            {
                line = lines[i];
                foreach (string x in vals) //compare against vals list at top
                {
                    if (line.Contains(x)) //did we find an opcode?
                    {
                        int index = line.IndexOf(x);
                        string newline = line.Substring(index);
                        parser = newline.Split(new char[0], StringSplitOptions.RemoveEmptyEntries);
                       
                        //parser goes through and trims the line to only essential info, then we trim the end so that the strings are all one variable instead of multiple based on spaces
                        if (parser.GetLength(0) > 4 & line.Contains("\""))
                        {
                            for (int j = 4; j < parser.GetLength(0); j++){
                                parser[3] = String.Concat(parser[3], " ", parser[j]);
                            }
                        }

                        //this is most of the logic to write the file right here.  if it finds an appropriate value as it runs through the file, do something
                        //only one is called per line
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
                                        luafile = String.Concat(luafile, "var", localCounter);
                                        localvariablelist.Add(String.Concat("var", localCounter));
                                        localCounter += 1;
                                    }
                                    else
                                    {
                                        luafile = String.Concat(luafile, ",var", localCounter);
                                        localvariablelist.Add(String.Concat("var", localCounter));
                                        localCounter += 1;
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
                                                    int internalindex = lines[q + 1].IndexOf("SETGLOBAL");
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
                                            //so I had to write a fix because lua (at least in missionlist...) would cut off a set list at 37 values.
                                            //this results in this program getting confused because there's an extra "SETLIST" that should not be there.
                                            int internalindex = linex.IndexOf("SETLIST");
                                            string internalnewline = linex.Substring(index);
                                            string[] internalparse = internalnewline.Split(new char[0], StringSplitOptions.RemoveEmptyEntries);
                                            /*
                                            if (tblElements[tblCounter - 1] == int.Parse(internalparse[2]))
                                            {
                                                internalopentables -= 1;
                                            }
                                            */
                                            //End weird section
                                            internalopentables -= 1;
                                            if (internalopentables == 0)
                                            {
                                                tblVarStatic.Add(0);
                                                tblswap.Add(false);
                                                if (lines[q + 1].Contains("SETGLOBAL"))
                                                {
                                                    tblisglobal.Add(true);
                                                    internalindex = lines[q + 1].IndexOf("SETGLOBAL");
                                                    internalnewline = lines[q + 1].Substring(index);
                                                    internalparse = internalnewline.Split(new char[0], StringSplitOptions.RemoveEmptyEntries);
                                                    tblGlobalNames.Add(internalparse[3]);

                                                }
                                                else
                                                {
                                                    tblisglobal.Add(false);
                                                    tblGlobalNames.Add("x");
                                                }
                                                break;
                                            }
                                            else if (linex.Contains("SETGLOBAL")) //for global tables that have no values yet
                                            {
                                                internalopentables -= 1;
                                                tblVarStatic.Add(0);
                                                tblswap.Add(false);
                                                tblisglobal.Add(true);
                                                internalindex = lines[q].IndexOf("SETGLOBAL");
                                                internalnewline = lines[q].Substring(index);
                                                internalparse = internalnewline.Split(new char[0], StringSplitOptions.RemoveEmptyEntries);
                                                tblGlobalNames.Add(internalparse[3]);
                                                break;
                                            }
                                        }

                                    }
                                    tblDECL = 1;
                                    opentables += 1;
                                    tblCounter += 1;

                                    if (tblisglobal[opentables - 1] == false)
                                    {
                                        luafile = String.Concat(luafile, System.Environment.NewLine, "local table", tblCounter, " = { ");
                                        Console.WriteLine(String.Concat("local table", tblCounter, " = { "));
                                        localvariablelist.Add(String.Concat("table", tblCounter));
                                        localCounter += 1;
                                    }
                                    else
                                    {
                                        luafile = String.Concat(luafile, System.Environment.NewLine, tblGlobalNames[opentables - 1], " = { ");
                                        Console.WriteLine(String.Concat(tblGlobalNames[opentables - 1], " = { "));
                                    }
                                    //int tblVals = int.Parse(parser[1]) * 2; //table and value associated with it

                                }
                                else if (opentables >= 1)
                                {
                                    //luafile = String.Concat(luafile, System.Environment.NewLine, "{");
                                    //Console.WriteLine(String.Concat("{"));

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
                                                if (lines[q + 1].Contains("SETGLOBAL"))
                                                {
                                                    tblisglobal.Add(true);
                                                    int internalindex = lines[q + 1].IndexOf("SETGLOBAL");
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
                                        }
                                        else if (linex.Contains("SETLIST"))
                                        {
                                            //for arbitrary broken setlist refs
                                            
                                            int internalindex = linex.IndexOf("SETLIST");
                                            string internalnewline = linex.Substring(index);
                                            string[] internalparse = internalnewline.Split(new char[0], StringSplitOptions.RemoveEmptyEntries);
                                            /*
                                            if (tblElements[tblCounter-1] == int.Parse(internalparse[2]))
                                            {
                                                internalopentables -= 1;
                                            }
                                            */
                                            //End weird code section
                                            internalopentables -= 1;
                                            if (internalopentables == 0)
                                            {
                                                tblVarStatic.Add(0);
                                                tblswap.Add(false);
                                                if (lines[q + 1].Contains("SETGLOBAL"))
                                                {
                                                    tblisglobal.Add(true);
                                                    internalindex  = lines[q + 1].IndexOf("SETGLOBAL");
                                                    internalnewline = lines[q + 1].Substring(index);
                                                    internalparse = internalnewline.Split(new char[0], StringSplitOptions.RemoveEmptyEntries);
                                                    tblGlobalNames.Add(internalparse[3]);

                                                }
                                                else
                                                {
                                                    tblisglobal.Add(false);
                                                    tblGlobalNames.Add("x");
                                                }
                                                break;
                                            }
                                        }
                                        else if (linex.Contains("SETGLOBAL"))
                                        {
                                            internalopentables -= 1;
                                            tblVarStatic.Add(0);
                                            tblswap.Add(false);
                                            tblisglobal.Add(true);
                                            int internalindex = lines[q].IndexOf("SETGLOBAL");
                                            string internalnewline = lines[q].Substring(index);
                                            string[] internalparse = internalnewline.Split(new char[0], StringSplitOptions.RemoveEmptyEntries);
                                            tblGlobalNames.Add(internalparse[3]);
                                            break;
                                        }

                                    }

                                    opentables += 1;
                                    tblCounter += 1;

                                    if (tblisglobal[opentables - 1] == false)
                                    {
                                        luafile = String.Concat(luafile, System.Environment.NewLine, " { ");
                                        Console.WriteLine(String.Concat("local table", tblCounter, " { "));
                                        localvariablelist.Add(String.Concat("table", tblCounter));
                                        localCounter += 1;
                                    }
                                    else
                                    {
                                        luafile = String.Concat(luafile, System.Environment.NewLine, tblGlobalNames[opentables - 1], " = { ");
                                        Console.WriteLine(String.Concat(tblGlobalNames[opentables - 1], " = { "));
                                    }
                                }
                                break;
                            case "PUSHUPVALUE":
                                break;
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
                                    luafile = String.Concat(luafile, System.Environment.NewLine, "local var", localCounter, "=", parser[1]);
                                    localvariablelist.Add(String.Concat("var", localCounter));
                                    localCounter += 1;
                                    Console.WriteLine(String.Concat("local var", localCounter, "=", parser[1]));
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
                                    luafile = String.Concat(luafile, System.Environment.NewLine, "local var", localCounter, "=", parser[3]);
                                    localvariablelist.Add(String.Concat("var", localCounter));
                                    localCounter += 1;
                                    Console.WriteLine(String.Concat("local var", localCounter, "=", parser[3]));
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
                                    luafile = String.Concat(luafile, System.Environment.NewLine, "local var", localCounter, "= -", parser[3]);
                                    localvariablelist.Add(String.Concat("var", localCounter));
                                    localCounter += 1;
                                    Console.WriteLine(String.Concat("local var", localCounter, "= -", parser[3]));
                                    break;
                                }
                            case "PUSHSTRING":
                                string finalstr = parser[3].Replace("\"", string.Empty);
                                if (tblDECL == 1)
                                {
                                    if (tblVarStatic[opentables - 1] == 1)
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
                                        if (tblswap[opentables - 1] == true)
                                        {
                                            tblVarStatic[opentables - 1] = 1;
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
                                    luafile = (String.Concat(luafile, System.Environment.NewLine, "local var", localCounter, "=\"", finalstr, "\""));
                                    localvariablelist.Add(String.Concat("var", localCounter));
                                    localCounter += 1;
                                    Console.WriteLine(String.Concat("local var", localCounter, "=\"", finalstr, "\""));
                                    break;
                                }
                            case "PUSHSELF":
                                break;
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
                                //if (tblElements[opentables - 1] != int.Parse(parser[2]))
                                //{
                                    //skip and do nothing
                               //}
                                //else
                               //{
                                    luafile = String.Concat(luafile, "}", System.Environment.NewLine);
                                    Console.WriteLine("}");
                                    //tblDECL = 0;
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

                               // }
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
                                        else if (linex.Contains("SETLOCAL")) 
                                        {
                                            int internalindex = lines[q].IndexOf("SETLOCAL");
                                            string internalnewline = lines[q].Substring(index);
                                            string[] internalparse = internalnewline.Split(new char[0], StringSplitOptions.RemoveEmptyEntries);
                                            luafile = String.Concat(luafile, localvariablelist[int.Parse(internalparse[1])], "=");
                                            Console.WriteLine(localvariablelist[int.Parse(internalparse[1])], "=");
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
                                                    internalindex = lines[q+1].IndexOf("SETLOCAL");
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
                                                    Console.WriteLine("local var", localCounter);
                                                    luafile = String.Concat(luafile, System.Environment.NewLine, "local var", localCounter);
                                                    localvariablelist.Add(String.Concat("var", localCounter));
                                                    localCounter += 1;
                                                }
                                                else
                                                {
                                                    Console.WriteLine(",var", localCounter);
                                                    luafile = String.Concat(luafile, ",var", localCounter);
                                                    localvariablelist.Add(String.Concat("var", localCounter));
                                                    localCounter += 1;
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
                            case "GETINDEXED":
                                break;
                            case "SETGLOBAL":
                                if (storefunctionname == true)
                                {
                                    functionnamelist.Add(parser[3]);
                                    storefunctionname = false;
                                    functioncounter += 1;
                                    break;
                                }
                                if (lines[i+1].Contains("CREATETABLE    0"))
                                {
                                    storeglobaltable = true;
                                    luafile = String.Concat(luafile, "}", System.Environment.NewLine);
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
                            case "RETURN":
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
                                    for (int k = 0; k < numnil; k++)
                                    {
                                        if (k == 0 & numnil == 1)
                                        {
                                            luafile = String.Concat(luafile, System.Environment.NewLine, "local var", localCounter, " = nil", System.Environment.NewLine);
                                            localvariablelist.Add(String.Concat("var", localCounter));
                                            localCounter += 1;

                                        }
                                        else if (k == 0 & numnil > 1)
                                        {
                                            luafile = String.Concat(luafile, System.Environment.NewLine, "local var", localCounter, ",");
                                            localvariablelist.Add(String.Concat("var", localCounter));
                                            localCounter += 1;
                                        }
                                        else if (k < numnil & k != 0)
                                        {
                                            luafile = String.Concat(luafile, "var", localCounter, ",");
                                            localvariablelist.Add(String.Concat("var", localCounter));
                                            localCounter += 1;
                                        }
                                        else
                                        {
                                            luafile = String.Concat(luafile, "var", localCounter, " = nil", System.Environment.NewLine);
                                            localvariablelist.Add(String.Concat("var", localCounter));
                                            localCounter += 1;
                                        }
                                    }
                                }
                                break;
                            case "END":
                                tblDECL = 0;
                                tblVarStatic.Clear();
                                localCounter = 0;
                                globalastable = 0;
                                globalcalledlast = 0;
                                withincall = 0;
                                localvariablelist.Clear();
                                tblGlobalNames.Clear();
                                tblisglobal.Clear();
                                tblswap.Clear();
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
