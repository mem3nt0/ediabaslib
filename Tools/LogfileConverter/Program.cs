﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NDesk.Options;

namespace LogfileConverter
{
    class Program
    {
        private static bool _responseFile;
        private static bool _cFormat;
        private static bool _edicCanMode;
        private static int _edicCanAddr;
        private static int _edicCanTesterAddr;
        private static int _edicCanEcuAddr;

        static int Main(string[] args)
        {
            bool sortFile = false;
            bool showHelp = false;
            List<string> inputFiles = new List<string>();
            string outputFile = null;

            var p = new OptionSet()
            {
                { "i|input=", "input file.",
                  v => inputFiles.Add(v) },
                { "o|output=", "output file (if omitted '.conv' is appended to input file).",
                  v => outputFile = v },
                { "c|cformat", "c format for hex values", 
                  v => _cFormat = v != null },
                { "r|response", "create reponse file", 
                  v => _responseFile = v != null },
                { "s|sort", "sort reponse file", 
                  v => sortFile = v != null },
                { "h|help",  "show this message and exit", 
                  v => showHelp = v != null },
            };

            try
            {
                p.Parse(args);
            }
            catch (OptionException e)
            {
                string thisName = Path.GetFileNameWithoutExtension(AppDomain.CurrentDomain.FriendlyName);
                Console.Write(thisName + ": ");
                Console.WriteLine(e.Message);
                Console.WriteLine("Try `" + thisName + " --help' for more information.");
                return 1;
            }

            if (showHelp)
            {
                ShowHelp(p);
                return 0;
            }

            if (inputFiles.Count < 1)
            {
                Console.WriteLine("No input files specified");
                return 1;
            }
            if (outputFile == null)
            {
                outputFile = inputFiles[0] + ".conv";
            }

            foreach (string inputFile in inputFiles)
            {
                if (!File.Exists(inputFile))
                {
                    Console.WriteLine("Input file '{0}' not found", inputFile);
                    return 1;
                }
            }

            if (!ConvertLog(inputFiles, outputFile))
            {
                Console.WriteLine("Conversion failed");
                return 1;
            }
            if (sortFile && _responseFile)
            {
                if (!SortLines(outputFile))
                {
                    Console.WriteLine("Sorting failed");
                    return 1;
                }
            }

            return 0;
        }

        private static bool ConvertLog(List<string> inputFiles, string outputFile)
        {
            try
            {
                using (StreamWriter streamWriter = new StreamWriter(outputFile))
                {
                    foreach (string inputFile in inputFiles)
                    {
                        if (string.Compare(Path.GetExtension(inputFile), ".trc", StringComparison.OrdinalIgnoreCase) == 0)
                        {   // trace file
                            ConvertTraceFile(inputFile, streamWriter);
                        }
                        else
                        {
                            bool ifhLog = false;
                            using (StreamReader streamReader = new StreamReader(inputFile))
                            {
                                string line = streamReader.ReadLine();
                                if (line != null)
                                {
                                    if (Regex.IsMatch(line, @"^dllStartupIFH"))
                                    {
                                        ifhLog = true;
                                    }
                                }
                            }
                            if (ifhLog)
                            {
                                ConvertIfhlogFile(inputFile, streamWriter);
                            }
                            else
                            {
                                ConvertPortLogFile(inputFile, streamWriter);
                            }
                        }
                    }
                }
            }
            catch
            {
                return false;
            }
            return true;
        }

        private static void ConvertPortLogFile(string inputFile, StreamWriter streamWriter)
        {
            _edicCanMode = false;
            using (StreamReader streamReader = new StreamReader(inputFile))
            {
                string line;
                string readString = string.Empty;
                string writeString = string.Empty;
                while ((line = streamReader.ReadLine()) != null)
                {
                    if (line.Length > 0)
                    {
                        if (!Regex.IsMatch(line, @"^\[\\\\"))
                        {
                            line = Regex.Replace(line, @"^[\d]+[\s]+[\d\.]+[\s]+[\w\.]+[\s]*", String.Empty);
                            if (Regex.IsMatch(line, @"IRP_MJ_WRITE"))
                            {
                                line = Regex.Replace(line, @"^IRP_MJ_WRITE.*\:[\s]*", String.Empty);
                                List<byte> lineValues = NumberString2List(line);
#if false
                                if ((lineValues.Count > 1) && (lineValues[1] == 0x56))
                                {
                                    line = string.Empty;
                                }
#endif
                                if (line.Length > 0)
                                {
                                    bool validWrite = ChecksumValid(lineValues);
                                    if (_responseFile)
                                    {
                                        if (validWrite)
                                        {
                                            if (writeString.Length > 0 && readString.Length > 0)
                                            {
                                                List<byte> writeValues = NumberString2List(writeString);
                                                List<byte> readValues = NumberString2List(readString);
                                                if (ValidResponse(writeValues, readValues))
                                                {
                                                    streamWriter.Write(NumberString2String(writeString, _responseFile || !_cFormat));
                                                    StoreReadString(streamWriter, readString);
                                                }
                                            }
                                            writeString = NumberString2String(line, _responseFile || !_cFormat);
                                        }
                                        else
                                        {
                                            writeString = string.Empty;
                                        }
                                    }
                                    else
                                    {
                                        StoreReadString(streamWriter, readString);
                                        if (validWrite)
                                        {
                                            line = "w: " + NumberString2String(line, _responseFile || !_cFormat);
                                        }
                                        else
                                        {
                                            line = "w (Invalid): " + NumberString2String(line, _responseFile || !_cFormat);
                                        }
                                    }
                                    readString = string.Empty;
                                }
                            }
                            else if (Regex.IsMatch(line, @"^Length 1:"))
                            {
                                line = Regex.Replace(line, @"^Length 1:[\s]*", String.Empty);
                                readString += line;
                                line = string.Empty;
                            }
                            else
                            {
                                line = string.Empty;
                            }
                            if (!_responseFile && line.Length > 0)
                            {
                                streamWriter.WriteLine(line);
                            }
                        }
                    }
                }
                if (_responseFile)
                {
                    if (writeString.Length > 0 && readString.Length > 0)
                    {
                        List<byte> writeValues = NumberString2List(writeString);
                        List<byte> readValues = NumberString2List(readString);
                        if (ValidResponse(writeValues, readValues))
                        {
                            streamWriter.Write(NumberString2String(writeString, _responseFile || !_cFormat));
                            StoreReadString(streamWriter, readString);
                        }
                    }
                }
                else
                {
                    StoreReadString(streamWriter, readString);
                }
            }
        }

        private static void ConvertTraceFile(string inputFile, StreamWriter streamWriter)
        {
            _edicCanMode = false;
            using (StreamReader streamReader = new StreamReader(inputFile))
            {
                string line;
                string readString = string.Empty;
                string writeString = string.Empty;
                while ((line = streamReader.ReadLine()) != null)
                {
                    if (line.Length > 0)
                    {
                        if (Regex.IsMatch(line, @"^ \(EDIC CommParameter"))
                        {
                            _edicCanMode = false;
                        }

                        MatchCollection canEdicMatches = Regex.Matches(line, @"^EDIC CAN: (..), Tester: (..), Ecu: (..)");
                        if (canEdicMatches.Count == 1)
                        {
                            if (canEdicMatches[0].Groups.Count == 4)
                            {
                                try
                                {
                                    _edicCanAddr = Convert.ToInt32(canEdicMatches[0].Groups[1].Value, 16);
                                    _edicCanTesterAddr = Convert.ToInt32(canEdicMatches[0].Groups[2].Value, 16);
                                    _edicCanEcuAddr = Convert.ToInt32(canEdicMatches[0].Groups[3].Value, 16);
                                    _edicCanMode = true;
                                }
                                catch (Exception)
                                {
                                    // ignored
                                }
                            }
                        }
                        if (Regex.IsMatch(line, @"^ \((Send|Resp)\):"))
                        {
                            bool send = Regex.IsMatch(line, @"^ \(Send\):");
                            line = Regex.Replace(line, @"^.*\:[\s]*", String.Empty);

                            List<byte> lineValues = NumberString2List(line);
                            if (line.Length > 0)
                            {
                                if (send)
                                {
                                    int sendLength = TelLengthBmwFast(lineValues, 0);
                                    if (sendLength > 0 && sendLength == lineValues.Count)
                                    {
                                        // checksum missing
                                        byte checksum = CalcChecksumBmwFast(lineValues, 0, lineValues.Count);
                                        lineValues.Add(checksum);
                                        line += $" {checksum:X02}";
                                    }
                                    bool validWrite = ChecksumValid(lineValues);
                                    if (_responseFile)
                                    {
                                        if (validWrite)
                                        {
                                            if (writeString.Length > 0 && readString.Length > 0)
                                            {
                                                List<byte> writeValues = NumberString2List(writeString);
                                                List<byte> readValues = NumberString2List(readString);
                                                if (ValidResponse(writeValues, readValues))
                                                {
                                                    streamWriter.Write(NumberString2String(writeString,
                                                        _responseFile || !_cFormat));
                                                    StoreReadString(streamWriter, readString);
                                                }
                                            }
                                            writeString = NumberString2String(line, _responseFile || !_cFormat);
                                        }
                                        else
                                        {
                                            writeString = string.Empty;
                                        }
                                    }
                                    else
                                    {
                                        StoreReadString(streamWriter, readString);
                                        if (validWrite)
                                        {
                                            line = "w: " + NumberString2String(line, _responseFile || !_cFormat);
                                        }
                                        else
                                        {
                                            line = "w (Invalid): " +
                                                   NumberString2String(line, _responseFile || !_cFormat);
                                        }
                                    }
                                    readString = string.Empty;
                                }
                                else
                                {   // receive
                                    bool addResponse = true;
                                    if (_edicCanMode)
                                    {
                                        if (lineValues.Count == 6 && lineValues[1] == 0xF1 && lineValues[2] == 0xF1)
                                        {   // filter adapter responses
                                            addResponse = false;
                                        }
                                    }
                                    if (addResponse)
                                    {
                                        readString += line;
                                    }
                                    line = string.Empty;
                                }
                            }
                            else
                            {
                                readString = string.Empty;
                            }
                            if (!_responseFile && line.Length > 0)
                            {
                                streamWriter.WriteLine(line);
                            }
                        }
                    }
                }
                if (_responseFile)
                {
                    if (writeString.Length > 0 && readString.Length > 0)
                    {
                        List<byte> writeValues = NumberString2List(writeString);
                        List<byte> readValues = NumberString2List(readString);
                        if (ValidResponse(writeValues, readValues))
                        {
                            streamWriter.Write(NumberString2String(writeString, _responseFile || !_cFormat));
                            StoreReadString(streamWriter, readString);
                        }
                    }
                }
                else
                {
                    StoreReadString(streamWriter, readString);
                }
            }
        }

        private static void ConvertIfhlogFile(string inputFile, StreamWriter streamWriter)
        {
            _edicCanMode = false;
            using (StreamReader streamReader = new StreamReader(inputFile))
            {
                string line;
                string writeString = string.Empty;
                bool ignoreResponse = false;
                bool keyBytes = false;
                while ((line = streamReader.ReadLine()) != null)
                {
                    if (line.Length > 0)
                    {
                        if (Regex.IsMatch(line, @"^msgIn:"))
                        {
                            if (Regex.IsMatch(line, @"^.*'ifhRequestKeyBytes"))
                            {
                                keyBytes = true;
                            }
                            if (!Regex.IsMatch(line, @"^.*('ifhSendTelegram'|'ifhGetResult')"))
                            {
                                ignoreResponse = true;
                                writeString = string.Empty;
                            }
                        }
                        if (Regex.IsMatch(line, @"^\((ifhSendTelegram|ifhGetResult)\): "))
                        {
                            bool send = Regex.IsMatch(line, @"^\(ifhSendTelegram\): ");
                            line = Regex.Replace(line, @"^.*\:[\s]*", String.Empty);

                            List<byte> lineValues = NumberString2List(line);
                            if (line.Length > 0)
                            {
                                if (send)
                                {
                                    if (lineValues.Count > 0)
                                    {
                                        lineValues = CreateBmwFastTel(lineValues, 0x00, 0xF1);
                                        line = List2NumberString(lineValues);
                                    }
                                    bool validWrite = ChecksumValid(lineValues);
                                    if (_responseFile)
                                    {
                                        // ReSharper disable once ConvertIfStatementToConditionalTernaryExpression
                                        if (validWrite)
                                        {
                                            writeString = NumberString2String(line, _responseFile || !_cFormat);
                                        }
                                        else
                                        {
                                            writeString = string.Empty;
                                        }
                                    }
                                    else
                                    {
                                        if (validWrite)
                                        {
                                            line = "w: " + NumberString2String(line, _responseFile || !_cFormat);
                                        }
                                        else
                                        {
                                            line = "w (Invalid): " +
                                                   NumberString2String(line, _responseFile || !_cFormat);
                                        }
                                    }
                                }
                                else
                                {   // receive
                                    if (keyBytes)
                                    {
                                        string readString = line;
                                        List<byte> readValues = NumberString2List(readString);
                                        if (readValues.Count >= 5)
                                        {
                                            streamWriter.WriteLine($"CFG: 00 {readValues[0]:X02} {readValues[1]:X02}");
                                        }
                                    }
                                    if (!ignoreResponse)
                                    {
                                        string readString = line;
                                        if (_responseFile)
                                        {
                                            if (writeString.Length > 0 && readString.Length > 0)
                                            {
                                                List<byte> writeValues = NumberString2List(writeString);
                                                List<byte> readValues = NumberString2List(readString);
                                                if (UpdateRequestAddr(writeValues, readValues))
                                                {
                                                    writeString = List2NumberString(writeValues);
                                                    if (ValidResponse(writeValues, readValues))
                                                    {
                                                        streamWriter.Write(NumberString2String(writeString, _responseFile || !_cFormat));
                                                        StoreReadString(streamWriter, readString);
                                                    }
                                                }
                                            }
                                        }
                                        else
                                        {
                                            StoreReadString(streamWriter, readString);
                                        }
                                    }
                                    writeString = string.Empty;
                                    line = string.Empty;
                                }
                            }
                            else
                            {
                                writeString = string.Empty;
                            }
                            if (!_responseFile && line.Length > 0)
                            {
                                streamWriter.WriteLine(line);
                            }
                            ignoreResponse = false;
                            keyBytes = false;
                        }
                    }
                }
            }
        }

        private static int LineComparer(string x, string y)
        {
            string lineX = x.Substring(3);
            string lineY = y.Substring(3);

            return String.Compare(lineX, lineY, StringComparison.Ordinal);
        }

        private static bool SortLines(string fileName)
        {
            try
            {
                string[] lines = File.ReadAllLines(fileName);
                Array.Sort(lines, LineComparer);
                using (StreamWriter streamWriter = new StreamWriter(fileName))
                {
                    string lastLine = string.Empty;
                    foreach (string line in lines)
                    {
                        if (line != lastLine)
                        {
                            streamWriter.WriteLine(line);
                        }
                        lastLine = line;
                    }
                }
            }
            catch
            {
                return false;
            }
            return true;
        }

        private static void StoreReadString(StreamWriter streamWriter, string readString)
        {
            try
            {
                if (readString.Length > 0)
                {
                    List<byte> lineValues = NumberString2List(readString);
                    bool valid = ChecksumValid(lineValues);
                    if (_responseFile)
                    {
                        if (valid)
                        {
                            streamWriter.WriteLine(" : " + NumberString2String(readString, _responseFile || !_cFormat));
                        }
                        else
                        {
                            streamWriter.WriteLine();
                        }
                    }
                    else
                    {
                        if (valid)
                        {
                            streamWriter.WriteLine("r: " + NumberString2String(readString, _responseFile || !_cFormat));
                        }
                        else
                        {
                            streamWriter.WriteLine("r (Invalid): " + NumberString2String(readString, _responseFile || !_cFormat));
                        }
                    }
                }
            }
            catch
            {
                // ignored
            }
        }

        private static List<byte> NumberString2List(string numberString)
        {
            List<byte> values = new List<byte>();
            string[] numberArray = numberString.Split(' ');
            foreach (string number in numberArray)
            {
                if (number.Length > 0)
                {
                    try
                    {
                        int value = Convert.ToInt32(number, 16);
                        values.Add((byte) value);
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }
            return values;
        }

        private static string List2NumberString(List<byte> dataList)
        {
            StringBuilder sr = new StringBuilder();
            foreach (byte data in dataList)
            {
                sr.Append($"{data:X02} ");
            }
            return sr.ToString();
        }

        private static byte CalcChecksumBmwFast(List<byte> data, int offset, int length)
        {
            byte sum = 0;
            for (int i = 0; i < length; i++)
            {
                sum += data[i + offset];
            }
            return sum;
        }

        // telegram length without checksum
        private static int TelLengthBmwFast(List<byte> telegram, int offset)
        {
            if (telegram.Count - offset < 4)
            {
                return 0;
            }
            int telLength = telegram[0 + offset] & 0x3F;
            if (telLength == 0)
            {   // with length byte
                if (telegram[3 + offset] == 0)
                {
                    if (telegram.Count < 6)
                    {
                        return 0;
                    }
                    telLength = ((telegram[4 + offset] << 8) | telegram[5 + offset]) + 6;
                }
                else
                {
                    telLength = telegram[3 + offset] + 4;
                }
            }
            else
            {
                telLength += 3;
            }
            return telLength;
        }

        private static List<byte> CreateBmwFastTel(List<byte> data, byte dest, byte source)
        {
            List<byte> result = new List<byte>();
            if (data.Count > 0x3F)
            {
                result.Add(0x80);
                result.Add(dest);
                result.Add(source);
                result.Add((byte)data.Count);
            }
            else
            {
                result.Add((byte) (0x80 | data.Count));
                result.Add(dest);
                result.Add(source);
            }
            result.AddRange(data);
            result.Add(CalcChecksumBmwFast(result, 0, result.Count));
            return result;
        }

        private static bool ChecksumValid(List<byte> telegram)
        {
            int offset = 0;
            for (; ; )
            {
                int dataLength = TelLengthBmwFast(telegram, offset);
                if (dataLength == 0) return false;
                if (telegram.Count - offset < dataLength + 1)
                {
                    return false;
                }

                byte sum = CalcChecksumBmwFast(telegram, offset, dataLength);
                if (sum != telegram[dataLength + offset])
                {
                    return false;
                }

                offset += dataLength + 1;    // checksum
                if (offset > telegram.Count)
                {
                    return false;
                }
                if (offset == telegram.Count)
                {
                    break;
                }
            }
            return true;
        }

        private static bool ValidResponse(List<byte> request, List<byte> response)
        {
            bool broadcast = (request[0] & 0xC0) != 0x80;
            if (!ChecksumValid(request) || !ChecksumValid(response))
            {
                return false;
            }
            if (!broadcast && !_edicCanMode)
            {
                if (request[1] != response[2])
                {
                    return false;
                }
                if (request[2] != response[1])
                {
                    return false;
                }
            }
            return true;
        }

        private static bool UpdateRequestAddr(List<byte> request, List<byte> response)
        {
            if (!ChecksumValid(request) || !ChecksumValid(response))
            {
                return false;
            }
            if (request.Count < 4)
            {
                return false;
            }
            request[1] = response[2];
            request[2] = response[1];
            request[request.Count - 1] = CalcChecksumBmwFast(request, 0, request.Count - 1);
            return true;
        }

        private static string NumberString2String(string numberString, bool simpleFormat)
        {
            string result = string.Empty;

            List<byte> values = NumberString2List(numberString);

            if (_edicCanMode && values.Count > 0)
            {

                int offset = 0;
                for (;;)
                {
                    int dataLength = TelLengthBmwFast(values, offset);
                    if (dataLength == 0) return string.Empty;
                    if (values.Count - offset < dataLength + 1)
                    {   // error
                        break;
                    }

                    bool updateChecksum = false;
                    if (values[1 + offset] == _edicCanAddr && values[2 + offset] == _edicCanTesterAddr)
                    {
                        values[1 + offset] = (byte)_edicCanEcuAddr;
                        updateChecksum = true;
                    }
                    else if (values[1 + offset] == 0x00 && values[2 + offset] == _edicCanAddr)
                    {
                        values[1 + offset] = (byte)_edicCanTesterAddr;
                        values[2 + offset] = (byte)_edicCanEcuAddr;
                        updateChecksum = true;
                    }
                    if (updateChecksum)
                    {
                        byte sum = CalcChecksumBmwFast(values, offset, dataLength);
                        values[dataLength + offset] = sum;
                    }

                    offset += dataLength + 1;    // checksum
                    if (offset > values.Count)
                    {   // error
                        break;
                    }
                    if (offset == values.Count)
                    {
                        break;
                    }
                }
            }

            foreach (byte value in values)
            {
                if (simpleFormat)
                {
                    if (result.Length > 0)
                    {
                        result += " ";
                    }
                    result += $"{value:X02}";
                }
                else
                {
                    if (result.Length > 0)
                    {
                        result += ", ";
                    }
                    result += $"0x{value:X02}";
                }
            }

            return result;
        }

        static void ShowHelp(OptionSet p)
        {
            Console.WriteLine("Usage: " + Path.GetFileNameWithoutExtension(AppDomain.CurrentDomain.FriendlyName) + " [OPTIONS]");
            Console.WriteLine("Convert OBD log files");
            Console.WriteLine();
            Console.WriteLine("Options:");
            p.WriteOptionDescriptions(Console.Out);
        }
    }
}
