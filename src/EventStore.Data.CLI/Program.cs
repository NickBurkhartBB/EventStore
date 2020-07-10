using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using EventStore.Core.Data;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Common.Log;
using System.Text.RegularExpressions;

namespace EventStore.Data.CLI
{
    class Program 
    {
        static ChunkHeader ReadChunkHeader(string file)
        {
            ChunkHeader chunkHeader;
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (fs.Length < ChunkFooter.Size + ChunkHeader.Size)
                {
                    throw new Exception(
                        string.Format("Chunk file '{0}' is bad. It does not have enough size for header and footer. File size is {1} bytes.",
                                      file, fs.Length));
                }
                chunkHeader = ChunkHeader.FromStream(fs);
            }
            return chunkHeader;
        }

        private static ChunkFooter ReadChunkFooter(string chunkFileName)
        {
            ChunkFooter chunkFooter;
            using (var fs = new FileStream(chunkFileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (fs.Length < ChunkFooter.Size + ChunkHeader.Size)
                {
                    throw new Exception(
                        string.Format("Chunk file '{0}' is bad. It does not have enough size for header and footer. File size is {1} bytes.",
                                      chunkFileName, fs.Length));
                }
                fs.Seek(-ChunkFooter.Size, SeekOrigin.End);
                chunkFooter = ChunkFooter.FromStream(fs);
            }
            return chunkFooter;
        }

        static byte[] EmptyJson(int size)
        {
            List<char> emptyJson = new List<char>();
            emptyJson.Add('{');
            for (int i = 0; i < size - 2; i++)
            {
                emptyJson.Add(' ');
            }
            emptyJson.Add('}');
            return Encoding.UTF8.GetBytes(emptyJson.ToArray());
        }

        static string ReadArg(string name, string[] args)
        {
            for(int i = 0; i < args.Length; i++)
            {
                if (args[i] == name && i + 1 < args.Length)
                {
                    return args[i + 1];
                }
            }
            return "";
        }

        static void Main(string[] args)
        {
            LogManager.Init("data-cli", "", "");
            var file = ReadArg("--input", args);
            if (file == "")
            {
                Console.WriteLine("you must specify input file: --input");
                Environment.Exit(1);
            }
            var newFile = ReadArg("--output", args);
            if (newFile == "")
            {
                Console.WriteLine("you must specify output file: --output");
                Environment.Exit(1);
            }
            var match = ReadArg("--match", args);
            if (match == "")
            {
                Console.WriteLine("you must specify text to scrub: --match");
                Environment.Exit(1);
            }
            var printJsonField = ReadArg("--print-json-field", args);

            Console.WriteLine("removing match {0} from chunk {1}", match, file);
            ChunkHeader header = ReadChunkHeader(file);
            var footer = ReadChunkFooter(file);
            ;
            if (!footer.IsCompleted)
            {
                Console.WriteLine("you can only modify completed chunks with this tool.");
                Environment.Exit(1);
                return;
            }

            TFChunk chunk = TFChunk.FromCompletedFile(file, false, unbufferedRead: false, initialReaderCount: 1, optimizeReadSideCache: false);
            
            var newChunk = TFChunk.CreateNew(newFile,
                                             header.ChunkSize,
                                             header.ChunkStartNumber,
                                             header.ChunkEndNumber,
                                             false,
                                             false,
                                             false,
                                             false,
                                             1);
            var result = chunk.TryReadFirst();
            var keepGoing = result.Success;
            while (keepGoing)
            {
                var record = result.LogRecord;
                switch (record.RecordType)
                {
                    case LogRecordType.Prepare:
                        {
                            var prepare = (PrepareLogRecord)record;
                            var dataStr = Encoding.UTF8.GetString(prepare.Data);
                            if (dataStr.Contains(match))
                            {
                                Console.WriteLine("scrubbing event {0} {1}", prepare.EventType, prepare.EventId);
                                if (printJsonField != "") {
                                    var oldRecordText = Encoding.UTF8.GetString(prepare.Data);
                                    string pattern = String.Format("\"{0}\": ?(\"[^\"]+\"|[0-9.]+)", printJsonField);
		                            foreach (Match patternMatch in Regex.Matches(oldRecordText, pattern, RegexOptions.IgnoreCase)) {
                                        Console.WriteLine(patternMatch.Value);
                                    }
                                }
                                var newRecord = LogRecord.Prepare(prepare.LogPosition, prepare.CorrelationId, prepare.EventId, prepare.TransactionPosition, prepare.TransactionOffset,
                                    prepare.EventStreamId, prepare.ExpectedVersion, prepare.Flags, prepare.EventType, EmptyJson(prepare.Data.Length), EmptyJson(prepare.Metadata.Length), prepare.TimeStamp);
                                newChunk.TryAppend(newRecord);
                            }
                            else
                            {
                                newChunk.TryAppend(prepare);
                            }
                            break;
                        }
                    case LogRecordType.Commit:
                        {
                            var commit = (CommitLogRecord)record;
                            newChunk.TryAppend(commit);
                            break;
                        }
                    case LogRecordType.System:
                        {
                            var system = (SystemLogRecord)record;
                            newChunk.TryAppend(system);
                            break;
                        }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                try
                {
                    result = chunk.TryReadClosestForward(result.NextPosition);
                    keepGoing = result.Success;
                }
                catch { 
                    keepGoing = false; 
                }
            }
            newChunk.Complete();
        }
    }
}
