// Project:         Daggerfall Unity
// Copyright:       Copyright (C) 2009-2024 Daggerfall Workshop
// Web Site:        http://www.dfworkshop.net
// License:         MIT License (http://www.opensource.org/licenses/mit-license.php)
// Source Code:     https://github.com/Interkarma/daggerfall-unity
// Original Author: Vivian V Wing (vwing@multitude.city)
// Contributors:
//
// Notes:
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DaggerfallWorkshop.Utility
{
    /// <summary>
    /// Utility class for unpacking Daggerfall's PACKED.DAT archive.
    /// </summary>
    public static class PackedDatFileUtils
    {
        const int fileRecordSize = 25;
        const int directoryRecordSize = 60;
        const int compressedChunkHeaderSize = 36;

        public static void UnpackFile(string packedDatPath, string outputFolderPath, Action<float> progress = null)
        {
            using (FileStream stream = File.OpenRead(packedDatPath))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                uint recordsStart = reader.ReadUInt32();
                uint directoriesStart = reader.ReadUInt32();
                int fileCount = (int)((directoriesStart - recordsStart) / fileRecordSize);

                List<FileRecord> fileRecords = ReadFileRecords(reader, recordsStart, fileCount);
                List<string> directories = ReadDirectoryRecords(reader, directoriesStart, fileRecords);

                for (int i = 0; i < fileRecords.Count; i++)
                {
                    FileRecord record = fileRecords[i];
                    string directory = directories[(int)record.DirectoryIndex];
                    string destinationDirectory = directory == "."
                        ? outputFolderPath
                        : Path.Combine(outputFolderPath, directory.Replace('\\', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(destinationDirectory);

                    string destinationPath = Path.Combine(destinationDirectory, record.Name);
                    ExtractRecord(reader, record, destinationPath);
                    if (progress != null)
                        progress((float)(i + 1) / fileRecords.Count);
                }
            }
        }

        static List<FileRecord> ReadFileRecords(BinaryReader reader, uint recordsStart, int fileCount)
        {
            List<FileRecord> records = new List<FileRecord>(fileCount);
            for (int i = 0; i < fileCount; i++)
            {
                reader.BaseStream.Position = recordsStart + i * fileRecordSize;
                FileRecord record = new FileRecord();
                record.Length = reader.ReadUInt32();
                record.DirectoryIndex = reader.ReadUInt32();
                record.Name = ReadFixedString(reader, 13);
                record.Start = reader.ReadUInt32();
                records.Add(record);
            }

            return records;
        }

        static List<string> ReadDirectoryRecords(BinaryReader reader, uint directoriesStart, List<FileRecord> fileRecords)
        {
            uint maxDirectoryIndex = 0;
            foreach (FileRecord record in fileRecords)
            {
                if (record.DirectoryIndex > maxDirectoryIndex)
                    maxDirectoryIndex = record.DirectoryIndex;
            }

            List<string> directories = new List<string>((int)maxDirectoryIndex + 1);
            reader.BaseStream.Position = directoriesStart;
            for (int i = 0; i <= maxDirectoryIndex; i++)
            {
                directories.Add(ReadFixedString(reader, directoryRecordSize));
            }

            return directories;
        }

        static void ExtractRecord(BinaryReader reader, FileRecord record, string destinationPath)
        {
            reader.BaseStream.Position = record.Start;
            using (FileStream output = File.Create(destinationPath))
            {
                int bytesWritten = 0;
                while (bytesWritten < record.Length)
                {
                    reader.BaseStream.Position += compressedChunkHeaderSize;
                    byte[] chunk = new PKStream(reader).Decode();
                    int bytesToWrite = Math.Min(chunk.Length, (int)record.Length - bytesWritten);
                    output.Write(chunk, 0, bytesToWrite);
                    bytesWritten += bytesToWrite;
                }
            }
        }

        static string ReadFixedString(BinaryReader reader, int length)
        {
            byte[] bytes = reader.ReadBytes(length);
            int stringLength = 0;
            while (stringLength < bytes.Length && bytes[stringLength] != 0)
                stringLength++;

            return Encoding.ASCII.GetString(bytes, 0, stringLength);
        }

        struct FileRecord
        {
            public uint Length;
            public uint DirectoryIndex;
            public string Name;
            public uint Start;
        }

        class PKStream
        {
            readonly BinaryReader reader;
            readonly byte[] dictionary;
            readonly List<byte> output = new List<byte>();
            int bitsRead;
            int lastByte;
            int currentKey;
            int prefixedLiterals;
            int dictBytes;
            int dictSize;

            public PKStream(BinaryReader reader)
            {
                this.reader = reader;
                prefixedLiterals = Read(8);
                dictBytes = Read(8);
                if (prefixedLiterals != 0 && prefixedLiterals != 1)
                    throw new InvalidDataException("Invalid literal encoding in PACKED.DAT stream.");
                if (dictBytes < 4 || dictBytes > 6)
                    throw new InvalidDataException("Invalid dictionary size in PACKED.DAT stream.");

                dictSize = 1 << (dictBytes + 6);
                dictionary = new byte[dictSize];
            }

            public byte[] Decode()
            {
                int tokenType = 0;
                while (tokenType >= 0)
                {
                    int literal;
                    int length;
                    int offset;
                    GetNextToken(out tokenType, out literal, out length, out offset);

                    if (tokenType == 0)
                        WriteLiteral((byte)literal);
                    else if (tokenType == 1)
                        CopyFromDictionary(length, offset);
                }

                return output.ToArray();
            }

            void WriteLiteral(byte literal)
            {
                output.Add(literal);
                dictionary[currentKey] = literal;
                currentKey++;
                if (currentKey == dictSize)
                    currentKey = 0;
            }

            void CopyFromDictionary(int length, int offset)
            {
                int start = (currentKey - 1 - offset) % dictSize;
                if (start < 0)
                    start += dictSize;

                int index = start;
                int next = currentKey;
                int copies = 0;
                while (copies < length)
                {
                    copies++;
                    byte value = dictionary[index];
                    output.Add(value);
                    dictionary[next] = value;

                    next++;
                    index++;
                    if (index == currentKey)
                        index = start;
                    if (index == dictSize)
                        index = 0;
                    if (next == dictSize)
                        next = 0;
                }

                currentKey = next;
            }

            void GetNextToken(out int tokenType, out int literal, out int length, out int offset)
            {
                if (Read(1) == 0)
                {
                    tokenType = 0;
                    literal = DecodeLiteral();
                    length = 0;
                    offset = 0;
                    return;
                }

                length = DecodeCopyLength();
                if (length == 519)
                {
                    tokenType = -1;
                    literal = 0;
                    offset = 0;
                    return;
                }

                tokenType = 1;
                literal = 0;
                int low = length == 2 ? 2 : dictBytes;
                offset = DecodeCopyOffset(low);
            }

            int ReadByte()
            {
                bitsRead = 8;
                lastByte = reader.ReadByte();
                return lastByte;
            }

            int Read(int bitCount)
            {
                if (bitsRead == 0)
                    ReadByte();

                if (bitCount <= bitsRead)
                {
                    int value = (lastByte >> (8 - bitsRead)) & (0xff >> (8 - bitCount));
                    bitsRead -= bitCount;
                    return value;
                }

                int shift = 8 - bitsRead;
                List<int> values = new List<int>();
                values.Add(lastByte >> shift);
                bitCount -= bitsRead;
                ReadByte();
                while (bitCount > 8)
                {
                    values[values.Count - 1] = values[values.Count - 1] | ((lastByte << (8 - shift)) & 0xff);
                    values.Add(lastByte >> shift);
                    ReadByte();
                    bitCount -= 8;
                }

                bitsRead = 8 - bitCount;
                if (bitCount > shift)
                {
                    values[values.Count - 1] = values[values.Count - 1] | ((lastByte << (8 - shift)) & 0xff);
                    values.Add((lastByte >> shift) & (0xff >> (8 - bitCount + shift)));
                }
                else
                {
                    values[values.Count - 1] = values[values.Count - 1] | ((lastByte << (8 - shift)) & (0xff >> (shift - bitCount)));
                }

                int result = 0;
                for (int i = 0; i < values.Count; i++)
                    result += values[i] << (i * 8);

                return result;
            }

            int ReadRev(int bitCount)
            {
                int value = Read(bitCount);
                int result = 0;
                for (int i = 0; i < bitCount; i++)
                {
                    result = result << 1;
                    result = result | (value & 1);
                    value = value >> 1;
                }

                return result;
            }

            int DecodeLiteral()
            {
                if (prefixedLiterals == 0)
                    return Read(8);

                throw new NotSupportedException("PACKED.DAT streams with prefixed literals are not supported.");
            }

            int DecodeCopyLength()
            {
                int value = ReadRev(2);
                if (value == 1)
                {
                    if (Read(1) != 0)
                        return 5;
                    if (Read(1) != 0)
                        return 6;
                    return 7;
                }

                if (value == 2)
                {
                    if (Read(1) != 0)
                        return 2;
                    return 4;
                }

                if (value == 3)
                    return 3;

                if (Read(1) != 0)
                {
                    if (Read(1) != 0)
                        return 8;
                    if (Read(1) != 0)
                        return 9;
                    return 10 + Read(1);
                }

                if (Read(1) != 0)
                {
                    if (Read(1) != 0)
                        return 12 + Read(2);
                    return 16 + Read(3);
                }

                value = ReadRev(2);
                if (value == 3)
                    return 24 + Read(4);
                if (value == 2)
                    return 40 + Read(5);
                if (value == 1)
                    return 72 + Read(6);
                if (Read(1) != 0)
                    return 136 + Read(7);
                return 264 + Read(8);
            }

            int CalcOffset(int high, int low)
            {
                return (high << low) | Read(low);
            }

            int DecodeCopyOffset(int low)
            {
                int value = Read(2);
                if (value == 3)
                    return CalcOffset(0, low);
                if (value == 1)
                {
                    if (Read(1) != 0)
                    {
                        if (Read(1) != 0)
                            return CalcOffset(1, low);
                        return CalcOffset(2, low);
                    }

                    return CalcOffset(6 - ReadRev(2), low);
                }

                if (value == 2)
                {
                    value = ReadRev(4);
                    if (value != 0)
                        return CalcOffset(0x16 - value, low);
                    return CalcOffset(0x17 - Read(1), low);
                }

                if (Read(1) != 0)
                    return CalcOffset(0x27 - ReadRev(4), low);
                if (Read(1) != 0)
                    return CalcOffset(0x2f - ReadRev(3), low);
                return CalcOffset(0x3f - ReadRev(4), low);
            }
        }
    }
}
