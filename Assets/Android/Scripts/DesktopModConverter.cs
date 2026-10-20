using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DaggerfallWorkshop
{
    /// <summary>
    /// Converts mod assetbundles built for another platform, usually desktop, so that Unity will load them on Android.
    /// Unity refuses any bundle whose serialized files name another build target, but text, code, textures and meshes
    /// load fine once that one field says Android. Shaders are the exception: a desktop bundle only carries desktop
    /// shader programs. Desktop builds of DFU include OpenGL Core programs, so the shaders and compute shaders of a
    /// bundle are rewritten with OpenGL ES versions of those (see DesktopModConverter.Shaders.cs), and the result
    /// counts the ones that had none to convert.
    /// Plain C# with no Unity calls, so it can run on a worker thread.
    /// </summary>
    public static partial class DesktopModConverter
    {
        /// <summary>
        /// UnityEditor.BuildTarget.Android, as stored in serialized files.
        /// </summary>
        public const int AndroidPlatform = 13;

        public class Result
        {
            /// <summary>
            /// The build target the bundle was made for, as a UnityEditor.BuildTarget value.
            /// </summary>
            public int OriginalPlatform;

            /// <summary>
            /// True if the bundle contains shaders or compute shaders, or if its type table couldn't be read.
            /// </summary>
            public bool HasShaders;

            /// <summary>
            /// Shaders and compute shaders given OpenGL ES programs made from their desktop OpenGL ones.
            /// </summary>
            public int ConvertedShaders;

            /// <summary>
            /// Shaders and compute shaders left with only desktop programs, which Android can't run.
            /// </summary>
            public int UnconvertedShaders;
        }

        const string Signature = "UnityFS";
        const int ShaderClassId = 48;
        const int ComputeShaderClassId = 72;
        const int MonoBehaviourClassId = 114;
        const uint CompressionMask = 0x3F;
        const uint BlocksInfoAtTheEnd = 0x80;
        const uint BlockInfoNeedPaddingAtStart = 0x200;
        const uint SerializedFileNodeFlag = 4;
        const int CompressionNone = 0;
        const int CompressionLzma = 1;
        const int CompressionLz4 = 2;
        const int CompressionLz4HC = 3;
        const int UncompressedBlockSize = 0x20000;

        /// <summary>
        /// Reads the build target of the bundle's first serialized file.
        /// </summary>
        /// <returns>False if the file isn't a bundle this class can read.</returns>
        public static bool TryGetPlatform(string path, out int platform)
        {
            platform = -1;
            try
            {
                using (var archive = new Archive(path))
                {
                    foreach (Node node in archive.Nodes)
                    {
                        if ((node.Flags & SerializedFileNodeFlag) != 0)
                        {
                            platform = ReadSerializedFile(archive, node, false).Platform;
                            return true;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            return false;
        }

        /// <summary>
        /// Writes a copy of a bundle with every serialized file retargeted to <paramref name="targetPlatform"/>.
        /// Source and destination can be the same file. Throws if the bundle can't be read or written.
        /// </summary>
        /// <returns>Null, without writing anything, if the bundle is already built for the target platform.</returns>
        public static Result Convert(string sourcePath, string destinationPath, int targetPlatform = AndroidPlatform)
        {
            string tempPath = destinationPath + ".converting";
            string blocksPath = destinationPath + ".blocks";
            Result result = null;
            try
            {
                using (var archive = new Archive(sourcePath))
                {
                    var patches = new List<KeyValuePair<long, byte[]>>();
                    var rewrittenFiles = new Dictionary<Node, byte[]>();
                    bool hasShaders = false;
                    int convertedShaders = 0;
                    int unconvertedShaders = 0;
                    foreach (Node node in archive.Nodes)
                    {
                        if ((node.Flags & SerializedFileNodeFlag) == 0)
                            continue;

                        SerializedFile file = ReadSerializedFile(archive, node, true);
                        hasShaders |= file.HasShaders != false;
                        if (file.Platform == targetPlatform)
                            continue;

                        if (result == null)
                            result = new Result { OriginalPlatform = file.Platform };

                        if (file.HasShaders == true)
                        {
                            ShaderConversion conversion = ConvertShaders(archive.ReadData(node.Offset, (int)node.Size), targetPlatform);
                            if (conversion != null)
                            {
                                convertedShaders += conversion.Converted;
                                unconvertedShaders += conversion.Unconverted;
                                if (conversion.File != null)
                                {
                                    rewrittenFiles[node] = conversion.File;
                                    continue;
                                }
                            }
                        }

                        patches.Add(new KeyValuePair<long, byte[]>(file.PlatformOffset, file.Encode(targetPlatform)));
                    }

                    if (result == null)
                        return null;
                    result.HasShaders = hasShaders;
                    result.ConvertedShaders = convertedShaders;
                    result.UnconvertedShaders = unconvertedShaders;

                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(tempPath)));
                    using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite))
                    {
                        if (rewrittenFiles.Count > 0)
                            archive.WriteRewritten(output, rewrittenFiles, patches, blocksPath);
                        else if (archive.HasLzmaBlocks)
                            archive.WriteDecompressed(output, patches);
                        else
                            archive.WritePatchedBlocks(output, patches);
                    }
                }

                if (File.Exists(destinationPath))
                    File.Replace(tempPath, destinationPath, null);
                else
                    File.Move(tempPath, destinationPath);
                return result;
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
                if (File.Exists(blocksPath))
                    File.Delete(blocksPath);
            }
        }

        /// <summary>
        /// A short name for a build target, for messages to the player.
        /// </summary>
        public static string GetPlatformName(int platform)
        {
            switch (platform)
            {
                case 5:
                case 19:
                    return "Windows";
                case 2:
                case 27:
                    return "macOS";
                case 17:
                case 24:
                case 25:
                    return "Linux";
                case 9:
                    return "iOS";
                case 13:
                    return "Android";
                case 20:
                    return "WebGL";
                default:
                    return "desktop";
            }
        }

        #region Serialized files

        class SerializedFile
        {
            public int Platform;
            public long PlatformOffset;     // offset in the bundle's uncompressed data
            public bool BigEndian;
            public bool? HasShaders;        // null if the type table couldn't be read

            public byte[] Encode(int platform)
            {
                byte[] bytes = BitConverter.GetBytes(platform);
                if (BitConverter.IsLittleEndian == BigEndian)
                    Array.Reverse(bytes);
                return bytes;
            }
        }

        static SerializedFile ReadSerializedFile(Archive archive, Node node, bool readTypes)
        {
            byte[] header = archive.ReadData(node.Offset, (int)Math.Min(48, node.Size));
            var reader = new DataReader(header, true);
            long metadataSize = reader.ReadUInt32();
            reader.ReadUInt32();                                    // file size
            uint version = reader.ReadUInt32();
            reader.ReadUInt32();                                    // data offset
            if (version < 9)
                throw new NotSupportedException($"Serialized file version {version} is too old");

            bool bigEndian = reader.ReadByte() != 0;
            reader.Skip(3);
            if (version >= 22)
            {
                metadataSize = reader.ReadUInt32();
                reader.Skip(8 + 8 + 8);                             // file size, data offset, reserved
            }

            long metadataOffset = node.Offset + reader.Position;
            int bytesNeeded = readTypes ? (int)metadataSize : 256;
            byte[] metadata = archive.ReadData(metadataOffset, (int)Math.Min(bytesNeeded, node.Size - reader.Position));
            reader = new DataReader(metadata, bigEndian);
            reader.ReadCString();                                   // Unity version

            var file = new SerializedFile();
            file.BigEndian = bigEndian;
            file.PlatformOffset = metadataOffset + reader.Position;
            file.Platform = reader.ReadInt32();
            if (readTypes)
            {
                try
                {
                    // A desktop compute shader fails outright on Android (World of Daggerfall - Terrain's
                    // "Kernel not found"), and the game has none to swap in
                    file.HasShaders = HasClass(reader, version, ShaderClassId, ComputeShaderClassId);
                }
                catch (Exception)
                {
                    file.HasShaders = null;
                }
            }

            return file;
        }

        // Reads the type table that follows the build target, see UnityPy's SerializedFile.read_serialized_type
        static bool HasClass(DataReader reader, uint version, params int[] classIds)
        {
            bool enableTypeTree = version < 13 || reader.ReadByte() != 0;
            if (enableTypeTree && version < 12 && version != 10)
                throw new NotSupportedException($"Type trees of serialized file version {version} aren't supported");

            int typeCount = reader.ReadInt32();
            bool found = false;
            for (int i = 0; i < typeCount; i++)
            {
                int typeClassId = reader.ReadInt32();
                found |= Array.IndexOf(classIds, typeClassId) >= 0;
                if (version >= 16)
                    reader.Skip(1);                                 // is stripped type
                if (version >= 17)
                    reader.Skip(2);                                 // script type index
                if (version >= 13)
                {
                    if ((version < 16 && typeClassId < 0) || (version >= 16 && typeClassId == MonoBehaviourClassId))
                        reader.Skip(16);                            // script id
                    reader.Skip(16);                                // old type hash
                }

                if (enableTypeTree)
                {
                    int nodeCount = reader.ReadInt32();
                    int stringBufferSize = reader.ReadInt32();
                    reader.Skip(nodeCount * (version >= 19 ? 32 : 24) + stringBufferSize);
                    if (version >= 21)
                        reader.Skip(reader.ReadInt32() * 4);        // type dependencies
                }
            }

            return found;
        }

        /// <summary>
        /// A serialized file read in full, so that some of its types and objects can be replaced and the file written again.
        /// Reads file versions 17 and later (Unity 2017.3 on), see UnityPy's SerializedFile, SerializedType and ObjectReader.
        /// </summary>
        class SerializedFileData
        {
            public uint Version;
            public bool BigEndian;
            public string UnityVersion;
            public readonly List<TypeEntry> Types = new List<TypeEntry>();
            public readonly List<ObjectEntry> Objects = new List<ObjectEntry>();

            byte[] data;
            byte[] reserved;
            long unknown;               // last field of the version 22 header
            long dataOffset;
            bool enableTypeTree;
            readonly List<KeyValuePair<int, long>> scripts = new List<KeyValuePair<int, long>>();
            byte[] tail;                // externals, reference types and user information, which hold nothing to align

            public bool HasTypeTrees
            {
                get { return enableTypeTree; }
            }

            public class TypeEntry
            {
                public int ClassId;
                public byte[] Raw;          // the whole entry as it was read
                public int TreeOffset;      // where its type tree starts in Raw, or -1 if the file has none
                public byte[] Replacement;  // written instead of Raw if set
            }

            public class ObjectEntry
            {
                public long PathId;
                public long ByteStart;
                public uint ByteSize;
                public int TypeIndex;
                public byte[] Replacement;  // written instead of the object's data if set
            }

            public static SerializedFileData Read(byte[] data)
            {
                var file = new SerializedFileData { data = data };
                var header = new DataReader(data, true);
                header.ReadUInt32();                                // metadata size
                header.ReadUInt32();                                // file size
                file.Version = header.ReadUInt32();
                file.dataOffset = header.ReadUInt32();
                if (file.Version < 17)
                    throw new NotSupportedException($"Serialized file version {file.Version} is too old to rewrite");

                file.BigEndian = header.ReadByte() != 0;
                file.reserved = header.ReadBytes(3);
                if (file.Version >= 22)
                {
                    header.ReadUInt32();                            // metadata size
                    header.ReadInt64();                             // file size
                    file.dataOffset = header.ReadInt64();
                    file.unknown = header.ReadInt64();
                }

                var reader = new DataReader(data, file.BigEndian) { Position = header.Position };
                file.UnityVersion = reader.ReadCString();
                reader.ReadInt32();                                 // build target, replaced when written
                file.enableTypeTree = reader.ReadByte() != 0;
                int typeCount = reader.ReadInt32();
                for (int i = 0; i < typeCount; i++)
                    file.Types.Add(ReadTypeEntry(reader, file.Version, file.enableTypeTree, false));

                int objectCount = reader.ReadInt32();
                for (int i = 0; i < objectCount; i++)
                {
                    reader.Align(4);
                    file.Objects.Add(new ObjectEntry
                    {
                        PathId = reader.ReadInt64(),
                        ByteStart = file.Version >= 22 ? reader.ReadInt64() : reader.ReadUInt32(),
                        ByteSize = reader.ReadUInt32(),
                        TypeIndex = reader.ReadInt32(),
                    });
                }

                int scriptCount = reader.ReadInt32();
                for (int i = 0; i < scriptCount; i++)
                {
                    int fileIndex = reader.ReadInt32();
                    reader.Align(4);
                    file.scripts.Add(new KeyValuePair<int, long>(fileIndex, reader.ReadInt64()));
                }

                int tailStart = reader.Position;
                int externalCount = reader.ReadInt32();
                for (int i = 0; i < externalCount; i++)
                {
                    reader.ReadCString();                           // empty
                    reader.Skip(16 + 4);                            // guid, type
                    reader.ReadCString();                           // path
                }
                if (file.Version >= 20)
                {
                    int refTypeCount = reader.ReadInt32();
                    for (int i = 0; i < refTypeCount; i++)
                        ReadTypeEntry(reader, file.Version, file.enableTypeTree, true);
                }
                reader.ReadCString();                               // user information
                file.tail = reader.Slice(tailStart, reader.Position);
                return file;
            }

            static TypeEntry ReadTypeEntry(DataReader reader, uint version, bool enableTypeTree, bool isRefType)
            {
                int start = reader.Position;
                var entry = new TypeEntry { ClassId = reader.ReadInt32(), TreeOffset = -1 };
                reader.Skip(1);                                     // is stripped type
                short scriptTypeIndex = (short)reader.ReadUInt16();
                if (isRefType && scriptTypeIndex >= 0 || entry.ClassId == MonoBehaviourClassId)
                    reader.Skip(16);                                // script id
                reader.Skip(16);                                    // old type hash
                if (enableTypeTree)
                {
                    entry.TreeOffset = reader.Position - start;
                    int nodeCount = reader.ReadInt32();
                    int stringBufferSize = reader.ReadInt32();
                    reader.Skip(nodeCount * (version >= 19 ? 32 : 24) + stringBufferSize);
                    if (version >= 21)
                    {
                        if (isRefType)
                        {
                            reader.ReadCString();                   // class name
                            reader.ReadCString();                   // namespace
                            reader.ReadCString();                   // assembly name
                        }
                        else
                        {
                            reader.Skip(reader.ReadInt32() * 4);    // type dependencies
                        }
                    }
                }

                entry.Raw = reader.Slice(start, reader.Position);
                return entry;
            }

            /// <summary>
            /// Gets an object's data as stored in the file.
            /// </summary>
            public byte[] GetObjectData(ObjectEntry entry)
            {
                var bytes = new byte[entry.ByteSize];
                Buffer.BlockCopy(data, (int)(dataOffset + entry.ByteStart), bytes, 0, bytes.Length);
                return bytes;
            }

            /// <summary>
            /// Writes the file with its replacements, retargeted to platform.
            /// </summary>
            public byte[] Write(int platform)
            {
                // Objects keep the order they were stored in, each aligned to 8 bytes as Unity writes them
                var objectData = new DataBuilder(BigEndian);
                var starts = new long[Objects.Count];
                var sizes = new uint[Objects.Count];
                foreach (int i in Enumerable.Range(0, Objects.Count).OrderBy(i => Objects[i].ByteStart))
                {
                    objectData.Align(8);
                    ObjectEntry entry = Objects[i];
                    starts[i] = objectData.Length;
                    if (entry.Replacement != null)
                    {
                        objectData.Write(entry.Replacement);
                        sizes[i] = (uint)entry.Replacement.Length;
                    }
                    else
                    {
                        objectData.Write(data, (int)(dataOffset + entry.ByteStart), (int)entry.ByteSize);
                        sizes[i] = entry.ByteSize;
                    }
                }

                // The header's size is a multiple of 4, so aligning within the metadata aligns within the file
                var metadata = new DataBuilder(BigEndian);
                metadata.WriteCString(UnityVersion);
                metadata.Write(platform);
                metadata.Write((byte)(enableTypeTree ? 1 : 0));
                metadata.Write(Types.Count);
                foreach (TypeEntry entry in Types)
                    metadata.Write(entry.Replacement ?? entry.Raw);
                metadata.Write(Objects.Count);
                for (int i = 0; i < Objects.Count; i++)
                {
                    metadata.Align(4);
                    metadata.Write(Objects[i].PathId);
                    if (Version >= 22)
                        metadata.Write(starts[i]);
                    else
                        metadata.Write((uint)starts[i]);
                    metadata.Write(sizes[i]);
                    metadata.Write(Objects[i].TypeIndex);
                }
                metadata.Write(scripts.Count);
                foreach (KeyValuePair<int, long> script in scripts)
                {
                    metadata.Write(script.Key);
                    metadata.Align(4);
                    metadata.Write(script.Value);
                }
                metadata.Write(tail);

                int headerSize = Version >= 22 ? 48 : 20;
                long newDataOffset = Align16(headerSize + metadata.Length);
                long fileSize = newDataOffset + objectData.Length;
                var header = new DataBuilder(true);
                if (Version >= 22)
                {
                    header.Write(0u);
                    header.Write(0u);
                    header.Write(Version);
                    header.Write(0u);
                }
                else
                {
                    header.Write((uint)metadata.Length);
                    header.Write((uint)fileSize);
                    header.Write(Version);
                    header.Write((uint)newDataOffset);
                }
                header.Write((byte)(BigEndian ? 1 : 0));
                header.Write(reserved);
                if (Version >= 22)
                {
                    header.Write((uint)metadata.Length);
                    header.Write(fileSize);
                    header.Write(newDataOffset);
                    header.Write(unknown);
                }

                var output = new DataBuilder(true);
                output.Write(header.ToArray());
                output.Write(metadata.ToArray());
                output.Align(16);
                output.Write(objectData.ToArray());
                return output.ToArray();
            }
        }

        #endregion

        #region UnityFS archives

        struct Block
        {
            public uint UncompressedSize;
            public uint CompressedSize;
            public ushort Flags;
            public long FileOffset;     // where its compressed bytes start in the bundle file
            public long DataOffset;     // where its uncompressed bytes start in the bundle's data

            public int Compression
            {
                get { return (int)(Flags & CompressionMask); }
            }
        }

        class Node
        {
            public long Offset;
            public long Size;
            public uint Flags;
            public string Path;
        }

        /// <summary>
        /// A UnityFS bundle: a header, a table of compressed blocks and the files stored in them, then the blocks.
        /// See UnityPy's BundleFile.read_fs and save_fs.
        /// </summary>
        class Archive : IDisposable
        {
            readonly FileStream stream;
            readonly uint formatVersion;
            readonly string unityVersion;
            readonly string unityRevision;
            readonly uint flags;
            readonly bool alignedHeader;
            readonly bool paddedData;
            readonly Block[] blocks;
            readonly Dictionary<int, byte[]> blockCache = new Dictionary<int, byte[]>();

            public readonly List<Node> Nodes = new List<Node>();

            public bool HasLzmaBlocks
            {
                get { return Array.Exists(blocks, block => block.Compression == CompressionLzma); }
            }

            public Archive(string path)
            {
                stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                try
                {
                    if (ReadCString(stream) != Signature)
                        throw new NotSupportedException("Not a UnityFS assetbundle");

                    var header = new DataReader(ReadExactly(stream, 4), true);
                    formatVersion = header.ReadUInt32();
                    unityVersion = ReadCString(stream);
                    unityRevision = ReadCString(stream);
                    header = new DataReader(ReadExactly(stream, 8 + 4 + 4 + 4), true);
                    header.ReadInt64();                             // bundle size
                    uint compressedBlocksInfoSize = header.ReadUInt32();
                    uint uncompressedBlocksInfoSize = header.ReadUInt32();
                    flags = header.ReadUInt32();

                    // Before these versions 0x200 meant Unity China's encryption rather than padding
                    bool newFlags = IsAtLeast(unityRevision, 2022, 1, 1) || IsAtLeast(unityRevision, 2021, 3, 2) && !IsAtLeast(unityRevision, 2022, 0, 0) ||
                        IsAtLeast(unityRevision, 2020, 3, 34) && !IsAtLeast(unityRevision, 2021, 0, 0);
                    if (!newFlags && (flags & BlockInfoNeedPaddingAtStart) != 0)
                        throw new NotSupportedException("Encrypted assetbundles aren't supported");

                    alignedHeader = formatVersion >= 7 || IsAtLeast(unityRevision, 2019, 4, 15) && !IsAtLeast(unityRevision, 2020, 0, 0);
                    paddedData = newFlags && (flags & BlockInfoNeedPaddingAtStart) != 0;
                    if (alignedHeader)
                        stream.Position = Align16(stream.Position);

                    byte[] blocksInfo;
                    if ((flags & BlocksInfoAtTheEnd) != 0)
                    {
                        long position = stream.Position;
                        stream.Position = stream.Length - compressedBlocksInfoSize;
                        blocksInfo = ReadExactly(stream, (int)compressedBlocksInfoSize);
                        stream.Position = position;
                    }
                    else
                    {
                        blocksInfo = ReadExactly(stream, (int)compressedBlocksInfoSize);
                    }

                    blocksInfo = Decompress(blocksInfo, (int)uncompressedBlocksInfoSize, (int)(flags & CompressionMask));
                    var reader = new DataReader(blocksInfo, true);
                    reader.Skip(16);                                // uncompressed data hash
                    blocks = new Block[reader.ReadInt32()];
                    for (int i = 0; i < blocks.Length; i++)
                    {
                        blocks[i].UncompressedSize = reader.ReadUInt32();
                        blocks[i].CompressedSize = reader.ReadUInt32();
                        blocks[i].Flags = reader.ReadUInt16();
                    }

                    int nodeCount = reader.ReadInt32();
                    for (int i = 0; i < nodeCount; i++)
                    {
                        Nodes.Add(new Node
                        {
                            Offset = reader.ReadInt64(),
                            Size = reader.ReadInt64(),
                            Flags = reader.ReadUInt32(),
                            Path = reader.ReadCString(),
                        });
                    }

                    if (paddedData)
                        stream.Position = Align16(stream.Position);

                    long fileOffset = stream.Position;
                    long dataOffset = 0;
                    for (int i = 0; i < blocks.Length; i++)
                    {
                        blocks[i].FileOffset = fileOffset;
                        blocks[i].DataOffset = dataOffset;
                        fileOffset += blocks[i].CompressedSize;
                        dataOffset += blocks[i].UncompressedSize;
                    }

                    if (fileOffset > stream.Length)
                        throw new InvalidDataException("Assetbundle is truncated");
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }
            }

            public void Dispose()
            {
                stream.Dispose();
            }

            /// <summary>
            /// Reads uncompressed bundle data, decompressing only the blocks it covers.
            /// </summary>
            public byte[] ReadData(long offset, int count)
            {
                var result = new byte[count];
                int copied = 0;
                for (int i = 0; i < blocks.Length && copied < count; i++)
                {
                    long blockEnd = blocks[i].DataOffset + blocks[i].UncompressedSize;
                    long position = offset + copied;
                    if (blockEnd <= position)
                        continue;

                    int start = (int)(position - blocks[i].DataOffset);
                    int length = (int)Math.Min(count - copied, blockEnd - position);
                    byte[] data = GetBlock(i, start + length);
                    Buffer.BlockCopy(data, start, result, copied, length);
                    copied += length;
                }

                if (copied < count)
                    throw new InvalidDataException("Read past the end of the assetbundle's data");
                return result;
            }

            // Decompresses at least the first minLength bytes of a block
            byte[] GetBlock(int index, int minLength)
            {
                byte[] data;
                if (blockCache.TryGetValue(index, out data) && data.Length >= minLength)
                    return data;

                Block block = blocks[index];
                stream.Position = block.FileOffset;
                if (block.Compression == CompressionLzma)
                {
                    // LZMA blocks can be the whole bundle, so only decompress as far as needed
                    var output = new MemoryStream();
                    new LzmaDecoder(new BoundedReader(stream, block.CompressedSize)).Decode(output, Math.Max(minLength, Math.Min(0x10000, (int)block.UncompressedSize)));
                    data = output.ToArray();
                }
                else
                {
                    data = Decompress(ReadExactly(stream, (int)block.CompressedSize), (int)block.UncompressedSize, block.Compression);
                }

                blockCache[index] = data;
                return data;
            }

            /// <summary>
            /// Copies the bundle, storing the blocks that hold patched bytes uncompressed and copying the rest as they are.
            /// </summary>
            public void WritePatchedBlocks(FileStream output, List<KeyValuePair<long, byte[]>> patches)
            {
                var outputBlocks = (Block[])blocks.Clone();
                var patchedBlocks = new Dictionary<int, byte[]>();
                for (int i = 0; i < blocks.Length; i++)
                {
                    long blockEnd = blocks[i].DataOffset + blocks[i].UncompressedSize;
                    foreach (KeyValuePair<long, byte[]> patch in patches)
                    {
                        if (patch.Key >= blockEnd || patch.Key + patch.Value.Length <= blocks[i].DataOffset)
                            continue;

                        byte[] data;
                        if (!patchedBlocks.TryGetValue(i, out data))
                        {
                            data = (byte[])GetBlock(i, (int)blocks[i].UncompressedSize).Clone();
                            patchedBlocks[i] = data;
                            outputBlocks[i].CompressedSize = outputBlocks[i].UncompressedSize;
                            outputBlocks[i].Flags = (ushort)(blocks[i].Flags & ~CompressionMask);
                        }

                        for (int j = 0; j < patch.Value.Length; j++)
                        {
                            long position = patch.Key + j - blocks[i].DataOffset;
                            if (position >= 0 && position < data.Length)
                                data[position] = patch.Value[j];
                        }
                    }
                }

                WriteArchive(output, outputBlocks, () =>
                {
                    var buffer = new byte[0x10000];
                    for (int i = 0; i < blocks.Length; i++)
                    {
                        byte[] data;
                        if (patchedBlocks.TryGetValue(i, out data))
                        {
                            output.Write(data, 0, data.Length);
                            continue;
                        }

                        stream.Position = blocks[i].FileOffset;
                        CopyBytes(stream, output, blocks[i].CompressedSize, buffer);
                    }
                });
            }

            /// <summary>
            /// Writes the bundle with all of its data uncompressed, then applies the patches.
            /// Used when the bundle has LZMA blocks, which can't be recompressed here.
            /// </summary>
            public void WriteDecompressed(FileStream output, List<KeyValuePair<long, byte[]>> patches)
            {
                long dataSize = 0;
                foreach (Block block in blocks)
                    dataSize += block.UncompressedSize;

                var outputBlocks = new Block[(int)((dataSize + UncompressedBlockSize - 1) / UncompressedBlockSize)];
                for (int i = 0; i < outputBlocks.Length; i++)
                {
                    outputBlocks[i].UncompressedSize = (uint)Math.Min(UncompressedBlockSize, dataSize - (long)i * UncompressedBlockSize);
                    outputBlocks[i].CompressedSize = outputBlocks[i].UncompressedSize;
                }

                long dataStart = 0;
                WriteArchive(output, outputBlocks, () =>
                {
                    dataStart = output.Position;
                    for (int i = 0; i < blocks.Length; i++)
                    {
                        stream.Position = blocks[i].FileOffset;
                        if (blocks[i].Compression == CompressionLzma)
                        {
                            long written = new LzmaDecoder(new BoundedReader(stream, blocks[i].CompressedSize)).Decode(output, blocks[i].UncompressedSize);
                            if (written != blocks[i].UncompressedSize)
                                throw new InvalidDataException("LZMA block is shorter than expected");
                        }
                        else
                        {
                            byte[] data = Decompress(ReadExactly(stream, (int)blocks[i].CompressedSize), (int)blocks[i].UncompressedSize, blocks[i].Compression);
                            output.Write(data, 0, data.Length);
                        }
                    }
                });

                long end = output.Position;
                foreach (KeyValuePair<long, byte[]> patch in patches)
                {
                    output.Position = dataStart + patch.Key;
                    output.Write(patch.Value, 0, patch.Value.Length);
                }
                output.Position = end;
            }

            /// <summary>
            /// Writes the bundle with some of its files replaced and patches applied to the rest. Replacing a file moves
            /// the ones after it, so every block is written again, LZ4-compressed. The blocks go through a temporary file
            /// at blocksPath first, since the blocks info that lists them comes before them.
            /// </summary>
            public void WriteRewritten(FileStream output, Dictionary<Node, byte[]> replacements, List<KeyValuePair<long, byte[]>> patches, string blocksPath)
            {
                List<Node> ordered = Nodes.OrderBy(node => node.Offset).ToList();
                var newNodes = new Dictionary<Node, Node>();
                long offset = 0;
                foreach (Node node in ordered)
                {
                    byte[] replacement;
                    long size = replacements.TryGetValue(node, out replacement) ? replacement.Length : node.Size;
                    newNodes[node] = new Node { Offset = offset, Size = size, Flags = node.Flags, Path = node.Path };
                    offset += size;
                }

                var outputBlocks = new List<Block>();
                using (var blocksFile = new FileStream(blocksPath, FileMode.Create, FileAccess.ReadWrite))
                {
                    var writer = new BlockWriter(blocksFile, outputBlocks);
                    long position = 0;
                    int nodeIndex = 0;
                    var written = new HashSet<Node>();
                    ReadAllData((data, start, count) =>
                    {
                        while (count > 0)
                        {
                            while (nodeIndex < ordered.Count && ordered[nodeIndex].Offset + ordered[nodeIndex].Size <= position)
                                nodeIndex++;
                            if (nodeIndex == ordered.Count)
                                return;                         // data after the last file

                            Node node = ordered[nodeIndex];
                            int length;
                            if (position < node.Offset)
                            {
                                length = (int)Math.Min(count, node.Offset - position);    // a gap between files
                            }
                            else
                            {
                                length = (int)Math.Min(count, node.Offset + node.Size - position);
                                byte[] replacement;
                                if (replacements.TryGetValue(node, out replacement))
                                {
                                    if (written.Add(node))
                                        writer.Write(replacement, 0, replacement.Length);
                                }
                                else
                                {
                                    ApplyPatches(data, start, length, position, patches);
                                    writer.Write(data, start, length);
                                }
                            }

                            position += length;
                            start += length;
                            count -= length;
                        }
                    });
                    foreach (KeyValuePair<Node, byte[]> replacement in replacements)
                    {
                        if (written.Add(replacement.Key))
                            writer.Write(replacement.Value, 0, replacement.Value.Length);
                    }
                    writer.Flush();

                    WriteArchive(output, outputBlocks.ToArray(), Nodes.Select(node => newNodes[node]).ToList(), () =>
                    {
                        blocksFile.Position = 0;
                        CopyBytes(blocksFile, output, blocksFile.Length, new byte[0x10000]);
                    });
                }
            }

            // Passes all of the bundle's uncompressed data to sink in order, one block at a time
            void ReadAllData(Action<byte[], int, int> sink)
            {
                for (int i = 0; i < blocks.Length; i++)
                {
                    stream.Position = blocks[i].FileOffset;
                    if (blocks[i].Compression == CompressionLzma)
                    {
                        long written = new LzmaDecoder(new BoundedReader(stream, blocks[i].CompressedSize)).Decode(new SinkStream(sink), blocks[i].UncompressedSize);
                        if (written != blocks[i].UncompressedSize)
                            throw new InvalidDataException("LZMA block is shorter than expected");
                    }
                    else
                    {
                        byte[] data = Decompress(ReadExactly(stream, (int)blocks[i].CompressedSize), (int)blocks[i].UncompressedSize, blocks[i].Compression);
                        sink(data, 0, data.Length);
                    }
                }
            }

            static void ApplyPatches(byte[] data, int start, int count, long dataPosition, List<KeyValuePair<long, byte[]>> patches)
            {
                foreach (KeyValuePair<long, byte[]> patch in patches)
                {
                    for (int j = 0; j < patch.Value.Length; j++)
                    {
                        long index = patch.Key + j - dataPosition;
                        if (index >= 0 && index < count)
                            data[start + index] = patch.Value[j];
                    }
                }
            }

            void WriteArchive(FileStream output, Block[] outputBlocks, Action writeData)
            {
                WriteArchive(output, outputBlocks, Nodes, writeData);
            }

            // Writes the header and an uncompressed blocks info for outputBlocks and nodes around the block data written by writeData
            void WriteArchive(FileStream output, Block[] outputBlocks, List<Node> nodes, Action writeData)
            {
                var blocksInfo = new MemoryStream();
                var writer = new DataWriter(blocksInfo);
                writer.Write(new byte[16]);                         // uncompressed data hash, which Unity doesn't check
                writer.Write(outputBlocks.Length);
                foreach (Block block in outputBlocks)
                {
                    writer.Write(block.UncompressedSize);
                    writer.Write(block.CompressedSize);
                    writer.Write(block.Flags);
                }
                writer.Write(nodes.Count);
                foreach (Node node in nodes)
                {
                    writer.Write(node.Offset);
                    writer.Write(node.Size);
                    writer.Write(node.Flags);
                    writer.WriteCString(node.Path);
                }
                byte[] blocksInfoBytes = blocksInfo.ToArray();

                writer = new DataWriter(output);
                writer.WriteCString(Signature);
                writer.Write(formatVersion);
                writer.WriteCString(unityVersion);
                writer.WriteCString(unityRevision);
                long sizePosition = output.Position;
                writer.Write(0L);
                writer.Write(blocksInfoBytes.Length);
                writer.Write(blocksInfoBytes.Length);
                writer.Write(flags & ~CompressionMask);
                if (alignedHeader)
                    WritePadding(output);

                bool blocksInfoAtTheEnd = (flags & BlocksInfoAtTheEnd) != 0;
                if (!blocksInfoAtTheEnd)
                    output.Write(blocksInfoBytes, 0, blocksInfoBytes.Length);
                if (paddedData)
                    WritePadding(output);
                writeData();
                if (blocksInfoAtTheEnd)
                    output.Write(blocksInfoBytes, 0, blocksInfoBytes.Length);

                long end = output.Position;
                output.Position = sizePosition;
                writer.Write(end);
                output.Position = end;
            }
        }

        /// <summary>
        /// Cuts the data written to it into blocks of UncompressedBlockSize, stored LZ4-compressed unless that doesn't help.
        /// </summary>
        class BlockWriter
        {
            readonly Stream output;
            readonly List<Block> blocks;
            readonly byte[] buffer = new byte[UncompressedBlockSize];
            int length;

            public BlockWriter(Stream output, List<Block> blocks)
            {
                this.output = output;
                this.blocks = blocks;
            }

            public void Write(byte[] data, int start, int count)
            {
                while (count > 0)
                {
                    int n = Math.Min(count, buffer.Length - length);
                    Buffer.BlockCopy(data, start, buffer, length, n);
                    length += n;
                    start += n;
                    count -= n;
                    if (length == buffer.Length)
                        Flush();
                }
            }

            public void Flush()
            {
                if (length == 0)
                    return;

                byte[] compressed = Lz4Encode(buffer, length);
                var block = new Block { UncompressedSize = (uint)length };
                if (compressed.Length < length)
                {
                    output.Write(compressed, 0, compressed.Length);
                    block.CompressedSize = (uint)compressed.Length;
                    block.Flags = CompressionLz4;
                }
                else
                {
                    output.Write(buffer, 0, length);
                    block.CompressedSize = (uint)length;
                }
                blocks.Add(block);
                length = 0;
            }
        }

        // A write-only stream that hands what's written to it to a callback
        class SinkStream : Stream
        {
            readonly Action<byte[], int, int> sink;

            public SinkStream(Action<byte[], int, int> sink)
            {
                this.sink = sink;
            }

            public override bool CanRead { get { return false; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return true; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }

            public override void Write(byte[] buffer, int offset, int count)
            {
                // The LZMA decoder reuses its window, so hand over a copy the sink can patch
                var copy = new byte[count];
                Buffer.BlockCopy(buffer, offset, copy, 0, count);
                sink(copy, 0, count);
            }
        }

        static bool IsAtLeast(string unityRevision, int major, int minor, int patch)
        {
            string[] parts = unityRevision.Split('.');
            int[] version = new int[3];
            for (int i = 0; i < 3 && i < parts.Length; i++)
            {
                int digits = 0;
                while (digits < parts[i].Length && char.IsDigit(parts[i][digits]))
                    digits++;
                int.TryParse(parts[i].Substring(0, digits), out version[i]);
            }

            if (version[0] != major)
                return version[0] > major;
            if (version[1] != minor)
                return version[1] > minor;
            return version[2] >= patch;
        }

        static byte[] Decompress(byte[] data, int uncompressedSize, int compression)
        {
            switch (compression)
            {
                case CompressionNone:
                    return data;
                case CompressionLz4:
                case CompressionLz4HC:
                    return Lz4Decode(data, uncompressedSize);
                case CompressionLzma:
                    var output = new MemoryStream(uncompressedSize);
                    new LzmaDecoder(new BoundedReader(new MemoryStream(data), data.Length)).Decode(output, uncompressedSize);
                    return output.ToArray();
                default:
                    throw new NotSupportedException($"Assetbundle compression {compression} isn't supported");
            }
        }

        #endregion

        #region LZ4

        static byte[] Lz4Decode(byte[] source, int uncompressedSize)
        {
            var output = new byte[uncompressedSize];
            int src = 0;
            int dst = 0;
            while (src < source.Length)
            {
                int token = source[src++];
                int literalLength = token >> 4;
                if (literalLength == 15)
                {
                    int b;
                    do
                    {
                        b = source[src++];
                        literalLength += b;
                    } while (b == 255);
                }

                Buffer.BlockCopy(source, src, output, dst, literalLength);
                src += literalLength;
                dst += literalLength;
                if (src >= source.Length)
                    break;

                int offset = source[src] | (source[src + 1] << 8);
                src += 2;
                int matchLength = token & 15;
                if (matchLength == 15)
                {
                    int b;
                    do
                    {
                        b = source[src++];
                        matchLength += b;
                    } while (b == 255);
                }
                matchLength += 4;

                int match = dst - offset;
                if (offset == 0 || match < 0 || dst + matchLength > output.Length)
                    throw new InvalidDataException("Corrupt LZ4 block");
                if (offset >= matchLength)
                {
                    Buffer.BlockCopy(output, match, output, dst, matchLength);
                    dst += matchLength;
                }
                else
                {
                    for (int i = 0; i < matchLength; i++)
                        output[dst++] = output[match++];
                }
            }

            if (dst != uncompressedSize)
                throw new InvalidDataException("LZ4 block is shorter than expected");
            return output;
        }

        /// <summary>
        /// Compresses the first length bytes of source as one LZ4 block, greedily matching 4-byte sequences.
        /// Follows the LZ4 block format's end rules: the last 5 bytes are literals and no match starts in the last 12.
        /// </summary>
        static byte[] Lz4Encode(byte[] source, int length)
        {
            const int MinMatch = 4;
            const int HashBits = 16;
            const int MaxDistance = 0xFFFF;
            var output = new MemoryStream(length + length / 255 + 16);
            var table = new int[1 << HashBits];             // last position + 1 of each hashed sequence
            int anchor = 0;
            int position = 0;
            int matchLimit = length - 5;
            int startLimit = length - 12;
            int misses = 0;
            while (position < startLimit)
            {
                uint sequence = (uint)(source[position] | source[position + 1] << 8 | source[position + 2] << 16 | source[position + 3] << 24);
                int hash = (int)((sequence * 2654435761u) >> (32 - HashBits));
                int candidate = table[hash] - 1;
                table[hash] = position + 1;
                if (candidate < 0 || position - candidate > MaxDistance ||
                    source[candidate] != source[position] || source[candidate + 1] != source[position + 1] ||
                    source[candidate + 2] != source[position + 2] || source[candidate + 3] != source[position + 3])
                {
                    // Skip ahead faster through data that doesn't compress, as LZ4's reference encoder does
                    position += 1 + (misses++ >> 6);
                    continue;
                }

                misses = 0;
                int matchLength = MinMatch;
                while (position + matchLength < matchLimit && source[candidate + matchLength] == source[position + matchLength])
                    matchLength++;

                WriteLz4Sequence(output, source, anchor, position - anchor, position - candidate, matchLength);
                position += matchLength;
                anchor = position;
            }

            WriteLz4Sequence(output, source, anchor, length - anchor, 0, 0);
            return output.ToArray();
        }

        // Writes a token, literals and, unless matchLength is 0 (the last sequence), a match
        static void WriteLz4Sequence(MemoryStream output, byte[] source, int literalStart, int literalLength, int distance, int matchLength)
        {
            int matchCode = matchLength == 0 ? 0 : matchLength - 4;
            output.WriteByte((byte)((Math.Min(literalLength, 15) << 4) | Math.Min(matchCode, 15)));
            WriteLz4Length(output, literalLength);
            output.Write(source, literalStart, literalLength);
            if (matchLength == 0)
                return;

            output.WriteByte((byte)distance);
            output.WriteByte((byte)(distance >> 8));
            WriteLz4Length(output, matchCode);
        }

        // Writes the part of a length that doesn't fit in its 4 bits of the token
        static void WriteLz4Length(MemoryStream output, int length)
        {
            if (length < 15)
                return;

            length -= 15;
            while (length >= 255)
            {
                output.WriteByte(255);
                length -= 255;
            }
            output.WriteByte((byte)length);
        }

        #endregion

        #region LZMA

        /// <summary>
        /// Reads a stream byte by byte without going past the end of one block.
        /// </summary>
        class BoundedReader
        {
            readonly Stream stream;
            readonly byte[] buffer = new byte[0x10000];
            long remaining;
            int position;
            int length;

            public BoundedReader(Stream stream, long length)
            {
                this.stream = stream;
                remaining = length;
            }

            public byte ReadByte()
            {
                if (position == length)
                {
                    int count = (int)Math.Min(buffer.Length, remaining);
                    if (count == 0)
                        throw new InvalidDataException("LZMA block ended early");
                    length = stream.Read(buffer, 0, count);
                    if (length <= 0)
                        throw new EndOfStreamException();
                    remaining -= length;
                    position = 0;
                }

                return buffer[position++];
            }
        }

        /// <summary>
        /// Decodes an LZMA block as Unity stores it: 5 bytes of properties, then the raw stream.
        /// Follows the LZMA SDK's reference decoder (LzmaSpec.cpp, public domain).
        /// </summary>
        class LzmaDecoder
        {
            const int NumStates = 12;
            const int NumPosBitsMax = 4;
            const int NumLenToPosStates = 4;
            const int NumAlignBits = 4;
            const int StartPosModelIndex = 4;
            const int EndPosModelIndex = 14;
            const int NumFullDistances = 1 << (EndPosModelIndex >> 1);
            const int MatchMinLen = 2;
            const ushort ProbInit = 1 << 10;

            readonly BoundedReader input;
            readonly int lc;
            readonly int lp;
            readonly int pb;
            readonly uint dictionarySize;
            uint range;
            uint code;

            readonly ushort[] literalProbs;
            readonly ushort[] isMatch = NewProbs(NumStates << NumPosBitsMax);
            readonly ushort[] isRep = NewProbs(NumStates);
            readonly ushort[] isRepG0 = NewProbs(NumStates);
            readonly ushort[] isRepG1 = NewProbs(NumStates);
            readonly ushort[] isRepG2 = NewProbs(NumStates);
            readonly ushort[] isRep0Long = NewProbs(NumStates << NumPosBitsMax);
            readonly ushort[][] posSlot = new ushort[NumLenToPosStates][];
            readonly ushort[] posDecoders = NewProbs(1 + NumFullDistances - EndPosModelIndex);
            readonly ushort[] align = NewProbs(1 << NumAlignBits);
            readonly LengthDecoder lengthDecoder = new LengthDecoder();
            readonly LengthDecoder repLengthDecoder = new LengthDecoder();

            public LzmaDecoder(BoundedReader input)
            {
                this.input = input;
                int properties = input.ReadByte();
                if (properties >= 9 * 5 * 5)
                    throw new InvalidDataException("Bad LZMA properties");
                lc = properties % 9;
                properties /= 9;
                lp = properties % 5;
                pb = properties / 5;
                for (int i = 0; i < 4; i++)
                    dictionarySize |= (uint)input.ReadByte() << (8 * i);

                literalProbs = NewProbs(0x300 << (lc + lp));
                for (int i = 0; i < NumLenToPosStates; i++)
                    posSlot[i] = NewProbs(1 << 6);

                range = 0xFFFFFFFF;
                for (int i = 0; i < 5; i++)
                    code = (code << 8) | input.ReadByte();
            }

            static ushort[] NewProbs(int count)
            {
                var probs = new ushort[count];
                for (int i = 0; i < count; i++)
                    probs[i] = ProbInit;
                return probs;
            }

            /// <summary>
            /// Decodes up to outputSize bytes into output, stopping early at an end marker.
            /// </summary>
            /// <returns>The number of bytes written.</returns>
            public long Decode(Stream output, long outputSize)
            {
                var window = new OutWindow(output, (int)Math.Max(1 << 12, Math.Min(dictionarySize, Math.Max(outputSize, 1))));
                uint rep0 = 0, rep1 = 0, rep2 = 0, rep3 = 0;
                int state = 0;
                int posMask = (1 << pb) - 1;
                while (window.TotalPosition < outputSize)
                {
                    int posState = (int)(window.TotalPosition & posMask);
                    if (DecodeBit(isMatch, (state << NumPosBitsMax) + posState) == 0)
                    {
                        DecodeLiteral(window, state, rep0);
                        state = state < 4 ? 0 : state < 10 ? state - 3 : state - 6;
                        continue;
                    }

                    int length;
                    if (DecodeBit(isRep, state) != 0)
                    {
                        if (window.IsEmpty)
                            throw new InvalidDataException("Corrupt LZMA data");
                        if (DecodeBit(isRepG0, state) == 0)
                        {
                            if (DecodeBit(isRep0Long, (state << NumPosBitsMax) + posState) == 0)
                            {
                                state = state < 7 ? 9 : 11;
                                window.PutByte(window.GetByte(rep0 + 1));
                                continue;
                            }
                        }
                        else
                        {
                            uint distance;
                            if (DecodeBit(isRepG1, state) == 0)
                            {
                                distance = rep1;
                            }
                            else
                            {
                                if (DecodeBit(isRepG2, state) == 0)
                                {
                                    distance = rep2;
                                }
                                else
                                {
                                    distance = rep3;
                                    rep3 = rep2;
                                }
                                rep2 = rep1;
                            }
                            rep1 = rep0;
                            rep0 = distance;
                        }

                        length = repLengthDecoder.Decode(this, posState);
                        state = state < 7 ? 8 : 11;
                    }
                    else
                    {
                        rep3 = rep2;
                        rep2 = rep1;
                        rep1 = rep0;
                        length = lengthDecoder.Decode(this, posState);
                        state = state < 7 ? 7 : 10;
                        rep0 = DecodeDistance(length);
                        if (rep0 == 0xFFFFFFFF)
                            break;                                  // end marker
                        if (rep0 >= dictionarySize || !window.CheckDistance(rep0))
                            throw new InvalidDataException("Corrupt LZMA data");
                    }

                    length += MatchMinLen;
                    long remaining = outputSize - window.TotalPosition;
                    window.CopyMatch(rep0 + 1, (int)Math.Min(length, remaining));
                }

                window.Flush();
                return window.TotalPosition;
            }

            void DecodeLiteral(OutWindow window, int state, uint rep0)
            {
                int previousByte = window.IsEmpty ? 0 : window.GetByte(1);
                int literalState = (int)(((window.TotalPosition & ((1 << lp) - 1)) << lc) + (previousByte >> (8 - lc)));
                int probs = 0x300 * literalState;
                int symbol = 1;
                if (state >= 7)
                {
                    int matchByte = window.GetByte(rep0 + 1);
                    do
                    {
                        int matchBit = (matchByte >> 7) & 1;
                        matchByte <<= 1;
                        int bit = DecodeBit(literalProbs, probs + ((1 + matchBit) << 8) + symbol);
                        symbol = (symbol << 1) | bit;
                        if (matchBit != bit)
                            break;
                    } while (symbol < 0x100);
                }
                while (symbol < 0x100)
                    symbol = (symbol << 1) | DecodeBit(literalProbs, probs + symbol);
                window.PutByte((byte)(symbol - 0x100));
            }

            uint DecodeDistance(int length)
            {
                int lengthState = Math.Min(length, NumLenToPosStates - 1);
                int slot = DecodeBitTree(posSlot[lengthState], 0, 6);
                if (slot < StartPosModelIndex)
                    return (uint)slot;

                int directBits = (slot >> 1) - 1;
                uint distance = (uint)((2 | (slot & 1)) << directBits);
                if (slot < EndPosModelIndex)
                {
                    distance += (uint)DecodeReverseBitTree(posDecoders, (int)distance - slot, directBits);
                }
                else
                {
                    distance += DecodeDirectBits(directBits - NumAlignBits) << NumAlignBits;
                    distance += (uint)DecodeReverseBitTree(align, 0, NumAlignBits);
                }
                return distance;
            }

            int DecodeBit(ushort[] probs, int index)
            {
                uint prob = probs[index];
                uint bound = (range >> 11) * prob;
                int bit;
                if (code < bound)
                {
                    range = bound;
                    probs[index] = (ushort)(prob + ((2048 - prob) >> 5));
                    bit = 0;
                }
                else
                {
                    range -= bound;
                    code -= bound;
                    probs[index] = (ushort)(prob - (prob >> 5));
                    bit = 1;
                }

                if (range < (1u << 24))
                {
                    range <<= 8;
                    code = (code << 8) | input.ReadByte();
                }
                return bit;
            }

            uint DecodeDirectBits(int count)
            {
                uint result = 0;
                for (; count > 0; count--)
                {
                    range >>= 1;
                    code -= range;
                    uint t = 0 - (code >> 31);
                    code += range & t;
                    result = (result << 1) + (t + 1);
                    if (range < (1u << 24))
                    {
                        range <<= 8;
                        code = (code << 8) | input.ReadByte();
                    }
                }
                return result;
            }

            int DecodeBitTree(ushort[] probs, int offset, int bits)
            {
                int m = 1;
                for (int i = 0; i < bits; i++)
                    m = (m << 1) + DecodeBit(probs, offset + m);
                return m - (1 << bits);
            }

            int DecodeReverseBitTree(ushort[] probs, int offset, int bits)
            {
                int m = 1;
                int symbol = 0;
                for (int i = 0; i < bits; i++)
                {
                    int bit = DecodeBit(probs, offset + m);
                    m = (m << 1) + bit;
                    symbol |= bit << i;
                }
                return symbol;
            }

            class LengthDecoder
            {
                readonly ushort[] choice = NewProbs(2);
                readonly ushort[] low = NewProbs(1 << NumPosBitsMax << 3);
                readonly ushort[] mid = NewProbs(1 << NumPosBitsMax << 3);
                readonly ushort[] high = NewProbs(1 << 8);

                public int Decode(LzmaDecoder decoder, int posState)
                {
                    if (decoder.DecodeBit(choice, 0) == 0)
                        return decoder.DecodeBitTree(low, posState << 3, 3);
                    if (decoder.DecodeBit(choice, 1) == 0)
                        return 8 + decoder.DecodeBitTree(mid, posState << 3, 3);
                    return 16 + decoder.DecodeBitTree(high, 0, 8);
                }
            }

            class OutWindow
            {
                readonly Stream output;
                readonly byte[] buffer;
                int position;
                int flushedPosition;
                bool isFull;

                public long TotalPosition { get; private set; }

                public bool IsEmpty
                {
                    get { return position == 0 && !isFull; }
                }

                public OutWindow(Stream output, int size)
                {
                    this.output = output;
                    buffer = new byte[size];
                }

                public void PutByte(byte b)
                {
                    TotalPosition++;
                    buffer[position++] = b;
                    if (position == buffer.Length)
                    {
                        Flush();
                        position = 0;
                        flushedPosition = 0;
                        isFull = true;
                    }
                }

                public byte GetByte(uint distance)
                {
                    return buffer[distance <= position ? position - distance : buffer.Length - distance + position];
                }

                public void CopyMatch(uint distance, int length)
                {
                    for (; length > 0; length--)
                        PutByte(GetByte(distance));
                }

                public bool CheckDistance(uint distance)
                {
                    return distance <= position || isFull;
                }

                public void Flush()
                {
                    output.Write(buffer, flushedPosition, position - flushedPosition);
                    flushedPosition = position;
                }
            }
        }

        #endregion

        #region Binary helpers

        class DataReader
        {
            readonly byte[] data;
            readonly bool bigEndian;

            public int Position { get; set; }

            public int Length
            {
                get { return data.Length; }
            }

            public DataReader(byte[] data, bool bigEndian)
            {
                this.data = data;
                this.bigEndian = bigEndian;
            }

            public byte ReadByte()
            {
                Require(1);
                return data[Position++];
            }

            public void Skip(int count)
            {
                Require(count);
                Position += count;
            }

            // Skips to a multiple of alignment from the start of the data
            public void Align(int alignment)
            {
                int padding = (alignment - Position % alignment) % alignment;
                Skip(Math.Min(padding, data.Length - Position));
            }

            public ushort ReadUInt16()
            {
                return (ushort)ReadValue(2);
            }

            public int ReadInt32()
            {
                return (int)ReadValue(4);
            }

            public uint ReadUInt32()
            {
                return (uint)ReadValue(4);
            }

            public long ReadInt64()
            {
                return (long)ReadValue(8);
            }

            public ulong ReadUInt64()
            {
                return ReadValue(8);
            }

            public float ReadSingle()
            {
                return BitConverter.ToSingle(BitConverter.GetBytes((uint)ReadValue(4)), 0);
            }

            public double ReadDouble()
            {
                return BitConverter.Int64BitsToDouble((long)ReadValue(8));
            }

            public byte[] ReadBytes(int count)
            {
                Require(count);
                byte[] bytes = Slice(Position, Position + count);
                Position += count;
                return bytes;
            }

            // A copy of the data from start up to end
            public byte[] Slice(int start, int end)
            {
                var bytes = new byte[end - start];
                Buffer.BlockCopy(data, start, bytes, 0, bytes.Length);
                return bytes;
            }

            public string ReadCString()
            {
                int end = Array.IndexOf(data, (byte)0, Position);
                if (end < 0)
                    throw new InvalidDataException("Unterminated string");
                string value = Encoding.UTF8.GetString(data, Position, end - Position);
                Position = end + 1;
                return value;
            }

            // A string as Unity serializes it: its length, its UTF-8 bytes, then padding to 4 bytes
            public string ReadAlignedString()
            {
                int length = ReadInt32();
                Require(length);
                string value = Encoding.UTF8.GetString(data, Position, length);
                Position += length;
                Align(4);
                return value;
            }

            ulong ReadValue(int count)
            {
                Require(count);
                ulong value = 0;
                for (int i = 0; i < count; i++)
                {
                    int index = bigEndian ? Position + i : Position + count - 1 - i;
                    value = (value << 8) | data[index];
                }
                Position += count;
                return value;
            }

            void Require(int count)
            {
                if (count < 0 || Position + count > data.Length)
                    throw new InvalidDataException("Read past the end of the data");
            }
        }

        // Writes values with either endianness to a growing buffer
        class DataBuilder
        {
            readonly MemoryStream stream = new MemoryStream();
            readonly bool bigEndian;

            public DataBuilder(bool bigEndian)
            {
                this.bigEndian = bigEndian;
            }

            public int Length
            {
                get { return (int)stream.Length; }
            }

            public byte[] ToArray()
            {
                return stream.ToArray();
            }

            public void Write(byte[] bytes)
            {
                stream.Write(bytes, 0, bytes.Length);
            }

            public void Write(byte[] bytes, int start, int count)
            {
                stream.Write(bytes, start, count);
            }

            public void Write(byte value)
            {
                stream.WriteByte(value);
            }

            public void Write(ushort value)
            {
                WriteValue(value, 2);
            }

            public void Write(int value)
            {
                WriteValue((uint)value, 4);
            }

            public void Write(uint value)
            {
                WriteValue(value, 4);
            }

            public void Write(long value)
            {
                WriteValue((ulong)value, 8);
            }

            public void Write(float value)
            {
                WriteValue(BitConverter.ToUInt32(BitConverter.GetBytes(value), 0), 4);
            }

            public void Write(double value)
            {
                WriteValue((ulong)BitConverter.DoubleToInt64Bits(value), 8);
            }

            public void WriteCString(string value)
            {
                Write(Encoding.UTF8.GetBytes(value));
                stream.WriteByte(0);
            }

            public void WriteAlignedString(string value)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value);
                Write(bytes.Length);
                Write(bytes);
                Align(4);
            }

            // Pads with zeros to a multiple of alignment from the start
            public void Align(int alignment)
            {
                while (stream.Length % alignment != 0)
                    stream.WriteByte(0);
            }

            void WriteValue(ulong value, int count)
            {
                for (int i = 0; i < count; i++)
                    stream.WriteByte((byte)(value >> (8 * (bigEndian ? count - 1 - i : i))));
            }
        }

        // Writes big-endian values, as UnityFS headers use
        class DataWriter
        {
            readonly Stream stream;

            public DataWriter(Stream stream)
            {
                this.stream = stream;
            }

            public void Write(byte[] bytes)
            {
                stream.Write(bytes, 0, bytes.Length);
            }

            public void Write(ushort value)
            {
                WriteBytes(value, 2);
            }

            public void Write(int value)
            {
                WriteBytes((uint)value, 4);
            }

            public void Write(uint value)
            {
                WriteBytes(value, 4);
            }

            public void Write(long value)
            {
                WriteBytes((ulong)value, 8);
            }

            public void WriteCString(string value)
            {
                Write(Encoding.UTF8.GetBytes(value));
                stream.WriteByte(0);
            }

            void WriteBytes(ulong value, int count)
            {
                for (int i = count - 1; i >= 0; i--)
                    stream.WriteByte((byte)(value >> (8 * i)));
            }
        }

        static string ReadCString(Stream stream)
        {
            var bytes = new List<byte>();
            for (int i = 0; i < 256; i++)
            {
                int b = stream.ReadByte();
                if (b <= 0)
                    return Encoding.UTF8.GetString(bytes.ToArray());
                bytes.Add((byte)b);
            }
            throw new InvalidDataException("Unterminated string in assetbundle header");
        }

        static byte[] ReadExactly(Stream stream, int count)
        {
            var bytes = new byte[count];
            int read = 0;
            while (read < count)
            {
                int n = stream.Read(bytes, read, count - read);
                if (n <= 0)
                    throw new EndOfStreamException("Assetbundle is truncated");
                read += n;
            }
            return bytes;
        }

        static void CopyBytes(Stream source, Stream destination, long count, byte[] buffer)
        {
            while (count > 0)
            {
                int n = source.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
                if (n <= 0)
                    throw new EndOfStreamException("Assetbundle is truncated");
                destination.Write(buffer, 0, n);
                count -= n;
            }
        }

        static long Align16(long position)
        {
            return (position + 15) & ~15L;
        }

        static void WritePadding(Stream stream)
        {
            while ((stream.Position & 15) != 0)
                stream.WriteByte(0);
        }

        #endregion
    }
}
