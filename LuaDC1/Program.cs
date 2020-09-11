﻿using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;

namespace LuaDC1
{
    class Program
    {
        public static readonly string[] vals = { "function","CREATETABLE","PUSHUPVALUE","PUSHINT","PUSHNUM","PUSHNEGNUM","PUSHSTRING","PUSHSELF",
            "SETMAP","SETLIST","GETGLOBAL","GETINDEXED","SETGLOBAL","CALL","RETURN", 
            "JMP","JMPF","JMPT","JMPLT","JMPLE","JMPGT","JMPGE","JMPONT","JMPONF","JMPEQ","JMPNE","PUSHNILJMP","FORLOOP","FORPREP","LFORPREP","LFORLOOP","NOT",
            "TAILCALL","ADD","MULT","DIV","POW","CONCAT","MINUS","SUB","ADDI","SETTABLE","SETLOCAL","GETLOCAL","GETDOTTED","PUSHNIL",
            "POP","CLOSURE","END",};
        static void Main(string[] args)
        {

            //TODO: SETLIST

            //Console.WriteLine("Hello World!");
           // Console.WriteLine("Arguments:");

            string filename;
            List<string> help_args = new List<string>(new string[] { "-h","--help", "/?",  "/h" });  // these are often used for program help messages
            if (args.Length > 0 && help_args.IndexOf(args[0]) > -1)
            {
                PrintHelp();
                return;
            }
            if (args.Length > 0)
            {
                filename = args[0];
            }
            else
            {
                PrintHelp();
                return; //stop further execution
            }
            if (!File.Exists(filename))
            {
                Console.WriteLine("Error! File '{0}' Does not exist\n", filename);
                return;
            }

            string outputFilename = GetOutputFileName(args);
            if( outputFilename == null)
            {
                PrintHelp();
                return;
            }

            string[] lines = System.IO.File.ReadAllLines(filename); //each individual line

            // StreamWriter sw = new StreamWriter(newname);

            //luafile is the overarching variable that will write to file at the very end
            string luafile = String.Concat("--generated from Phantom's program",System.Environment.NewLine); 


            //set ALL the variables
            int linecounter = 0;
            int tblDECL = 0;
            int jumpLines = 0;
            int tblCounter = 0;
            int localCounter = 0;
            int globalcalledlast = 0;
            int globalastable = 0;
            int withincall = 0;
            int setListSkip = 0;
            int functioncounter = -1;
            int functionNameAssigner = 0;
            int opentables = 0;
            int retNum = 0;
            bool storefunctionname = false;
            bool insidefunction = false;
            bool storeglobaltable = false;
            bool setListCalled = false;
            bool inIf = false;
            bool pleaseReturn = false;

            string line;

            //initialize some lists and arrays
            List<string> localvariablelist = new List<string>();
            List<string> functionnamelist = new List<string>();
            List<int> tblVarStatic = new List<int>();
            List<bool> tblswap = new List<bool>();
            List<bool> tblisglobal = new List<bool>();
            List<string> tblGlobalNames = new List<string>();
            //List<int> tblElements = new List<int>();
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
                                luafile = String.Concat(luafile,System.Environment.NewLine, "function ", functionnamelist[functionNameAssigner], "(");
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
                                functionNameAssigner += 1;
                                break;
                            case "CREATETABLE":
                                //tblElements.Add(int.Parse(parser[1]));
                                int tableElements = int.Parse(parser[1]);
                                int countElements = 0;
                                if (opentables == 0)
                                {
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

                                            countElements += int.Parse(internalparse[2]);
                                            if (tableElements == countElements)
                                            {
                                                internalopentables -= 1;
                                                countElements = 0;
                                            }
                                            else
                                            {
                                                setListSkip += 1;
                                            }

                                            //End weird section
                                            //internalopentables -= 1;
                                            if (internalopentables == 0)
                                            {
                                                setListCalled = true;
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
                                        }
                                        else if (linex.Contains("SETGLOBAL")) //for global tables that have no values yet
                                        {
                                            internalopentables -= 1;
                                            storeglobaltable = true;
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
                                tblDECL = 1;
                                opentables += 1;
                                tblCounter += 1;

                                    //I seriously have no clue why I have to call this again but it works
                                    //I swear the code about 40 lines up does this EXACT same thing
                                if (setListCalled == true)
                                    {
                                        tblswap[opentables - 1] = false;
                                    }

                                if (tblisglobal[opentables - 1] == false)
                                {
                                    luafile = String.Concat(luafile, System.Environment.NewLine, "local table", tblCounter, " = { ");
                                    //Console.WriteLine(String.Concat("local table", tblCounter, " = { "));
                                    localvariablelist.Add(String.Concat("table", tblCounter));
                                    localCounter += 1;
                                }
                                else
                                {
                                    luafile = String.Concat(luafile, System.Environment.NewLine, tblGlobalNames[opentables - 1], " = { ");
                                    //Console.WriteLine(String.Concat(tblGlobalNames[opentables - 1], " = { "));
                                }
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

                                            countElements += int.Parse(internalparse[2]);
                                            if (tableElements == countElements)
                                            {
                                                internalopentables -= 1;
                                                countElements = 0;
                                            }
                                            else
                                            {
                                                setListSkip += 1;
                                            }
                                            //End weird code section
                                            //internalopentables -= 1;
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
                                            storeglobaltable = true;
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
                                        //Console.WriteLine(String.Concat("local table", tblCounter, " { "));
                                        localvariablelist.Add(String.Concat("table", tblCounter));
                                        localCounter += 1;
                                    }
                                    else
                                    {
                                        luafile = String.Concat(luafile, System.Environment.NewLine, tblGlobalNames[opentables - 1], " = { ");
                                        //Console.WriteLine(String.Concat(tblGlobalNames[opentables - 1], " = { "));
                                    }
                                }
                                break;
                            case "PUSHUPVALUE":
                                break;
                            case "PUSHINT":
                                for (int q = i; q < lines.GetLength(0); q++)
                                {
                                    string linex = lines[q];
                                    if (linex.Contains("JMPF"))
                                    {
                                        luafile = String.Concat(luafile, "if (");
                                        inIf = true;
                                        break;
                                    }
                                    else if (linex.Contains("RETURN"))
                                    {
                                        if (retNum == 0)
                                        {
                                            luafile = String.Concat(luafile, "return ");
                                            retNum = int.Parse(linex.Substring(linex.Length - 1, 1));
                                            pleaseReturn = true;
                                        }
                                        break;
                                    }
                                    else if (linex.Contains("SET"))
                                    {
                                        break;
                                    }
                                    else if (linex.Contains("FORPREP"))
                                    {
                                        break;
                                    }
                                }


                                if (tblDECL == 1)
                                {
                                    if (tblVarStatic[opentables-1] == 1)
                                    {
                                        luafile = String.Concat(luafile, parser[1], " = ");
                                        //Console.WriteLine(String.Concat(parser[1], " = "));
                                        if (tblswap[opentables - 1] == true)
                                        {
                                            tblVarStatic[opentables - 1] = 0;
                                        }
                                        break;
                                    }
                                    else
                                    {
                                        luafile = String.Concat(luafile, parser[1], ", ");
                                       // Console.WriteLine(String.Concat(parser[1], ", "));
                                        if (tblswap[opentables-1] == true)
                                        {
                                            tblVarStatic[opentables-1] = 1;
                                        }
                                        break;
                                    }
                                }
                                else if (tblDECL == 0 & globalcalledlast == 1) {
                                    luafile = String.Concat(luafile, parser[1], ",");
                                   // Console.WriteLine(parser[1], ",");
                                    break;
                                }
                                else if (pleaseReturn == true)
                                {
                                    luafile = String.Concat(luafile, parser[1]);
                                    retNum -= 1;
                                    if (retNum > 0)
                                    {
                                        luafile = String.Concat(luafile,",");

                                    }
                                    break;
                                }
                                else
                                {
                                    luafile = String.Concat(luafile, System.Environment.NewLine, "local var", localCounter, "=", parser[1]);
                                    localvariablelist.Add(String.Concat("var", localCounter));
                                    localCounter += 1;
                                    //Console.WriteLine(String.Concat("local var", localCounter, "=", parser[1]));
                                    break;
                                }
                            case "PUSHNUM":
                                if (tblDECL == 1)
                                {
                                    if (tblVarStatic[opentables-1] == 1)
                                    {
                                        luafile = String.Concat(luafile, parser[3], " = ");
                                        //Console.WriteLine(String.Concat(parser[3], " = "));
                                        if (tblswap[opentables - 1] == true)
                                        {
                                            tblVarStatic[opentables - 1] = 0;
                                        }
                                        break;
                                    }
                                    else
                                    {
                                        luafile = String.Concat(luafile, parser[3], ", ");
                                        //Console.WriteLine(String.Concat(parser[3], ", "));
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
                                   // Console.WriteLine(parser[3], ",");
                                    break;
                                }
                                else
                                {
                                    luafile = String.Concat(luafile, System.Environment.NewLine, "local var", localCounter, "=", parser[3]);
                                    localvariablelist.Add(String.Concat("var", localCounter));
                                    localCounter += 1;
                                  //  Console.WriteLine(String.Concat("local var", localCounter, "=", parser[3]));
                                    break;
                                }
                            case "PUSHNEGNUM":
                                if (tblDECL == 1)
                                {
                                    if (tblVarStatic[opentables-1] == 1)
                                    {
                                        luafile = String.Concat(luafile, "-", parser[3], " = ");
                                      //  Console.WriteLine(String.Concat("-", parser[3], " = "));
                                        if (tblswap[opentables - 1] == true)
                                        {
                                            tblVarStatic[opentables - 1] = 0;
                                        }
                                        break;
                                    }
                                    else
                                    {
                                        luafile = String.Concat(luafile, "-", parser[3], ", ");
                                      //  Console.WriteLine(String.Concat("-", parser[3], ", "));
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
                                   // Console.WriteLine(parser[3], ",");
                                    break;
                                }
                                else
                                {
                                    luafile = String.Concat(luafile, System.Environment.NewLine, "local var", localCounter, "= -", parser[3]);
                                    localvariablelist.Add(String.Concat("var", localCounter));
                                    localCounter += 1;
                                  //  Console.WriteLine(String.Concat("local var", localCounter, "= -", parser[3]));
                                    break;
                                }
                            case "PUSHSTRING":
                                string finalstr = parser[3].Replace("\"", string.Empty);
                                if (tblDECL == 1)
                                {
                                    if (tblVarStatic[opentables - 1] == 1)
                                    {
                                        luafile = (String.Concat(luafile, finalstr, " = "));
                                        //Console.WriteLine(String.Concat(finalstr, " = "));
                                        if (tblswap[opentables - 1] == true)
                                        {
                                            tblVarStatic[opentables - 1] = 0;
                                        }
                                        break;
                                    }
                                    else
                                    {
                                        luafile = (String.Concat(luafile, "\"", finalstr, "\", "));
                                       // Console.WriteLine(String.Concat("\"", finalstr, "\", "));
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
                                   // Console.WriteLine(String.Concat("\"", finalstr, "\","));
                                    break;
                                }
                                else
                                {
                                    //it's a new local variable
                                    luafile = (String.Concat(luafile, System.Environment.NewLine, "local var", localCounter, "=\"", finalstr, "\""));
                                    localvariablelist.Add(String.Concat("var", localCounter));
                                    localCounter += 1;
                                   // Console.WriteLine(String.Concat("local var", localCounter, "=\"", finalstr, "\""));
                                    break;
                                }
                            case "PUSHSELF":
                                break;
                            case "SETMAP":
                                luafile = String.Concat(luafile.TrimEnd(',',' '), "}");
                                //Console.WriteLine("}");
                                //tblVarStatic[opentables-1] = 0;
                                opentables -= 1;
                                if (opentables == 0) 
                                { 
                                    tblDECL = 0;
                                    tblVarStatic.Clear();
                                    tblGlobalNames.Clear();
                                    tblisglobal.Clear();
                                }
                                else
                                {
                                    luafile = String.Concat(luafile,",");
                                }
                                break;
                            case "SETLIST":

                                if (setListSkip > 0)
                                {
                                    setListSkip -= 1;
                                    //skip and do nothing
                                }
                                else
                               {
                                    luafile = String.Concat(luafile.TrimEnd(',',' '), "}");
                                    //Console.WriteLine("}");
                                    //tblDECL = 0;
                                    countElements = 0;
                                    setListCalled = false;
                                    //tblVarStatic[opentables-1] = 0;
                                    opentables -= 1;
                                    if (opentables == 0)
                                    {
                                        luafile = String.Concat(luafile, System.Environment.NewLine);

                                        tblDECL = 0;
                                        tblVarStatic.Clear();
                                        tblGlobalNames.Clear();
                                        tblisglobal.Clear();
                                    }
                                    else
                                    {
                                        luafile = String.Concat(luafile, ",");
                                    }

                                }
                                break;
                            case "GETGLOBAL":
                                for (int q = i; q < lines.GetLength(0); q++)
                                {
                                    string linex = lines[q];
                                    if (linex.Contains("JMPF"))
                                    {
                                        if (inIf == false) 
                                        {
                                            luafile = String.Concat(luafile, "if (");
                                            inIf = true; 
                                        }
                                        else
                                        {
                                            luafile = String.Concat(luafile, "elseif (");
                                        }

                                        break;
                                    }
                                    else if (linex.Contains("RETURN"))
                                    {
                                        luafile = String.Concat(luafile, "return ");
                                        break;
                                    }
                                    else if (linex.Contains("FORPREP"))
                                    {
                                        break;
                                    }
                                    else if (linex.Contains("SET") || (linex.Contains("CALL")))
                                    {
                                        break;
                                    }
                                }
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
                                        else if (linex.Contains("JMPF"))
                                        {
                                            break;
                                        }
                                        else if (linex.Contains("SETLOCAL")) 
                                        {
                                            int internalindex = lines[q].IndexOf("SETLOCAL");
                                            string internalnewline = lines[q].Substring(index);
                                            string[] internalparse = internalnewline.Split(new char[0], StringSplitOptions.RemoveEmptyEntries);
                                            luafile = String.Concat(luafile, localvariablelist[int.Parse(internalparse[1])], "=");
                                            //Console.WriteLine(localvariablelist[int.Parse(internalparse[1])], "=");
                                            break;
                                        }
                                        else if (linex.Contains("CALL"))
                                        {
                                            int internalindex = lines[q].IndexOf("CALL");
                                            string internalnewline = lines[q].Substring(index);
                                            string[] internalparse = internalnewline.Split(new char[0], StringSplitOptions.RemoveEmptyEntries);

                                            int numoflocs = int.Parse(internalparse[2]);
                                            if (numoflocs == 0 || numoflocs == 255)
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
                                                    //Console.WriteLine(localvariablelist[int.Parse(internalparse[1])], "=");
                                                    break;
                                                }
                                            }

                                            for (int q1 = 0; q1 < numoflocs; q1++)
                                            {
                                                if (q1 == 0)
                                                {
                                                    //Console.WriteLine("local var", localCounter);
                                                    luafile = String.Concat(luafile, System.Environment.NewLine, "local var", localCounter);
                                                    localvariablelist.Add(String.Concat("var", localCounter));
                                                    localCounter += 1;
                                                }
                                                else
                                                {
                                                   // Console.WriteLine(",var", localCounter);
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
                                        else if (linex.Contains("GETINDEXED"))
                                        {
                                            globalastable = 1;

                                        }

                                    }
                                    if (globalastable == 1) 
                                    {
                                        //accept args
                                        //Console.WriteLine(String.Concat(parser[3], "["));
                                        luafile = String.Concat(luafile, parser[3], "[");
                                        globalcalledlast = 1;
                                    }
                                    else
                                    {
                                        if (withincall > 1)
                                        {
                                            //Console.WriteLine(parser[3]);
                                            luafile = String.Concat(luafile, parser[3]);
                                            globalcalledlast = 1;
                                        }
                                        else if (inIf == true)
                                        {
                                            luafile = String.Concat(luafile, parser[3],") then");
                                            inIf = false;
                                        }
                                        else
                                       {
                                            //Console.WriteLine(String.Concat(parser[3], "("));
                                            luafile = String.Concat(luafile, System.Environment.NewLine, parser[3], "(");
                                            globalcalledlast = 1;
                                        }
                                    }
                                }
                                else if (globalcalledlast == 1)
                                {
                                    //if (withincall >= 1)
                                    //{
                                       // Console.WriteLine(parser[3]);
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
                                globalastable = 0;
                                luafile = String.Concat(luafile, localvariablelist[int.Parse(parser[1])], "]");
                                break;
                            case "SETGLOBAL":
                                if (storefunctionname == true)
                                {
                                    functionnamelist.Add(parser[3]);
                                    storefunctionname = false;
                                    functioncounter += 1;
                                    break;
                                }
                                if (storeglobaltable == true)
                                {
                                    storeglobaltable = false;
                                    luafile = String.Concat(luafile, "}", System.Environment.NewLine);
                                }
                                break;
                            case "CALL":
                                luafile = String.Concat(luafile.TrimEnd(','),")");
                                //Console.WriteLine(")");

                                globalastable = 0;
                                globalcalledlast = 0;
                                withincall = 0;
                                break;
                            case "RETURN":
                                retNum = 0;
                                pleaseReturn = false;
                                break;
                            case "JMP":
                                jumpLines = int.Parse(parser[1]);
                                break;
                            case "JMPF":
                                //store how many to go before putting else
                                jumpLines = int.Parse(parser[1]);
                                //inIf = true;
                                break;
                            case "JMPT":
                                jumpLines = int.Parse(parser[1]);
                                break;
                            case "JMPLT":
                                jumpLines = int.Parse(parser[1]);
                                break;
                            case "JMPLE":
                                jumpLines = int.Parse(parser[1]);
                                break;
                            case "JMPGT":
                                jumpLines = int.Parse(parser[1]);
                                break;
                            case "JMPGE":
                                jumpLines = int.Parse(parser[1]);
                                break;
                            case "JMPONT":
                                break;
                            case "JMPONF":
                                break;
                            case "JMPEQ":
                                jumpLines = int.Parse(parser[1]);
                                break;
                            case "JMPNE":
                                jumpLines = int.Parse(parser[1]);
                                break;
                            case "PUSHNILJMP":
                                break;
                            case "FORLOOP":
                                break;
                            case "FORPREP":
                                break;
                            case "LFORPREP":
                                break;
                            case "LFORLOOP":
                                break;
                            case "NOT":
                                break;
                            case "TAILCALL":
                                break;
                            case "ADD":
                                break;
                            case "MULT":
                                break;
                            case "DIV":
                                break;
                            case "POW":
                                break;
                            case "CONCAT":
                                break;
                            case "MINUS":
                                break;
                            case "SUB":
                                break;
                            case "ADDI":
                                //Console.WriteLine(luafile.Substring(luafile.Length - 2));
                                if (luafile.Substring(luafile.Length - 2) == "]=")
                                {
                                    luafile = luafile.Remove(luafile.Length -2, 2);
                                    luafile = String.Concat(luafile, "+", parser[1], "]=");
                                    //Console.WriteLine(String.Concat("+", parser[1],"]="));
                                }
                                else
                                {
                                    luafile = String.Concat(luafile, "+", parser[1]);
                                   // Console.WriteLine(String.Concat("+", parser[1]));
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
                                for (int q = i; q < lines.GetLength(0); q++)
                                {
                                    string linex = lines[q];
                                    if (linex.Contains("JMPF"))
                                    {
                                        luafile = String.Concat(luafile, "if (");
                                        inIf = true;
                                        break;
                                    }
                                    else if (linex.Contains("RETURN"))
                                    {
                                        if (retNum == 0)
                                        {
                                            luafile = String.Concat(luafile, "return ");
                                            retNum = int.Parse(linex.Substring(linex.Length - 1, 1));
                                            pleaseReturn = true;
                                        }
                                        break;
                                    }
                                    else if (linex.Contains("SET"))
                                    {
                                        break;
                                    }
                                    else if (linex.Contains("FORPREP"))
                                    {
                                        break;
                                    }
                                }
                                luafile = String.Concat(luafile, localvariablelist[int.Parse(parser[1])]);
                                if (retNum > 0)
                                {
                                    luafile = String.Concat(luafile, ",");
                                    retNum -= 1;
                                }

                                if (globalcalledlast == 1 & globalastable == 0)
                                {
                                    luafile = String.Concat(luafile,",");
                                    //Console.WriteLine(",");
                                    break;
                                }
                                if (globalastable == 1)
                                {
                                    luafile = String.Concat(luafile, "]=");
                                    globalastable = 0;
                                }
                                if (inIf == true)
                                {
                                    luafile = String.Concat(luafile, ") then",System.Environment.NewLine);
                                    inIf = false;
                                }
                                break;
                            case "GETDOTTED":
                                luafile = String.Concat(luafile, ".", parser[3]);
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
                                    //Console.WriteLine(String.Concat(localvariablelist[int.Parse(internalparse[1])], "=nil"));
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
                            case "POP":
                                break;
                            case "CLOSURE":
                                storefunctionname = true;
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
                                luafile = String.Concat(luafile, System.Environment.NewLine);
                                break;

                        }
                        linecounter = linecounter + 1;
                        if (jumpLines > 0)
                        {
                            jumpLines -= 1;
                            if (jumpLines == 0 & !parser[0].Contains("JMP"))
                            {
                                luafile = String.Concat(luafile, System.Environment.NewLine, "end", System.Environment.NewLine);
                            }
                            else if (jumpLines == 0 & parser[0].Contains("JMP"))
                            {
                                luafile = String.Concat(luafile, System.Environment.NewLine, "else", System.Environment.NewLine);
                            }
                        }
                    }
                }
            }
            //lol @lua for needing an escape character
            luafile = luafile.Replace("\\","\\\\");
            Console.WriteLine("Writing file: '{0}'", outputFilename); // tell the user the filename we are writing.
            System.IO.File.WriteAllText(outputFilename, luafile);
        }

        private static string GetOutputFileName(string[] args)
        {
            string retVal = null;
            if (args.Length == 3 && args[1].ToLower() == "-o")
                retVal = args[2];
            else if (args.Length == 1)
            {
                string filename = args[0];
                int dotIndex = filename.LastIndexOf('.');
                if (dotIndex > -1)
                    retVal = filename.Substring(0, dotIndex) + ".lua";
                else  // input file has no extension
                    retVal = filename + ".lua";
            }
            return retVal;
        }
        private static void PrintHelp()
        {
            //Console.WriteLine("Please enter argument in this fashion - program.exe <NAME-IN>");
            string help =
@"USAGE:
  LuaDC1.exe <input file>
  Saves the decompiled file to the <input file>.lua (replaces input file extension with '.lua')
or
  LuaDC1.exe <input file> -o <outfile>
  Saves the decompiled file to the output file specified

Options:
  -o <outfile>  (optional) Write (successful) decompiled data to the specified file (defaults to <input file>.lua ).
  -h --help /? /h Prints help message

Requirements:
  The input file must be a lua 4.0 listing, (usually) created with luac.exe (found at 'BFBuilder\ToolsFL\bin\luac.exe')
      'luac.exe -l <compiled lua 4.0 file>'
";
            Console.WriteLine(help);
        }
    }
}
