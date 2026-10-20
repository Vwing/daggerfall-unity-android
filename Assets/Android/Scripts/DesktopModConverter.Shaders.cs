using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DaggerfallWorkshop
{
    // Shader conversion for DesktopModConverter.
    //
    // Upstream DFU's Windows and Linux builds include OpenGL Core programs next to their Direct3D ones, and Unity writes
    // the same GLSL (HLSLcc's output) for OpenGL Core and OpenGL ES, so a desktop program becomes an OpenGL ES one by
    // changing its #version line and adding precision statements. Desktop bundles also lay shaders out as Unity 2017.3 to
    // 2021.1 wrote them, which Unity 2022.3 reads without their programs, so each shader is written again in 2022.3's
    // layout: its Shader and ComputeShader types get 2022.3 type trees (DesktopModConverter.Data.cs) and its programs
    // go in a new OpenGL ES blob laid out as Unity 2022.3 writes one.
    public static partial class DesktopModConverter
    {
        const int ShaderPlatformGLES3 = 9;              // ShaderCompilerPlatform
        const int ShaderPlatformGLCore = 15;
        const int GpuProgramGLES31 = 3;                 // ShaderGpuProgramType
        const int GpuProgramGLES3 = 4;
        const int GpuProgramGLCore32 = 6;
        const int GpuProgramGLCore41 = 7;
        const int GpuProgramGLCore43 = 8;
        const int RendererGLES3 = 11;                   // GfxDeviceRenderer
        const int RendererGLCore = 17;
        const int ComputeLevelES31 = 3;                 // the target level Unity 2022.3 gives OpenGL ES 3.1 compute shaders
        const long ComputeKernelRequirements = 0x4001;  // base shaders and compute, as Unity 2022.3 writes them for OpenGL ES
        const int OldestGpuProgramVersion = 201708220;  // Unity 2017.3's shader blob format
        const int LocalKeywordsGpuProgramVersion = 201806140;
        const int GpuProgramVersion2022 = 202012090;    // what Unity 2022.3 writes
        const int MaxBlobSegmentSize = 16000000;        // Unity splits a platform's programs into segments of under 16 MB

        class ShaderConversion
        {
            public byte[] File;         // the rewritten serialized file, or null if none of its shaders were converted
            public int Converted;
            public int Unconverted;
        }

        /// <summary>
        /// Rewrites a serialized file with OpenGL ES programs made from its shaders' OpenGL Core ones.
        /// </summary>
        /// <returns>Null if the file can't be rewritten, in which case its shaders couldn't be counted either.</returns>
        static ShaderConversion ConvertShaders(byte[] data, int targetPlatform)
        {
            SerializedFileData file;
            var sources = new Dictionary<SerializedFileData.ObjectEntry, Dictionary<string, object>>();
            try
            {
                file = SerializedFileData.Read(data);
                if (file.Version < 19 || file.BigEndian || !file.HasTypeTrees)
                    return null;

                // Read every shader before changing anything, since one that can't be read can't be written in the new layout
                for (int typeIndex = 0; typeIndex < file.Types.Count; typeIndex++)
                {
                    SerializedFileData.TypeEntry type = file.Types[typeIndex];
                    if (type.ClassId != ShaderClassId && type.ClassId != ComputeShaderClassId)
                        continue;

                    TypeNode tree = ReadTypeTree(type.Raw, type.TreeOffset, false, file.Version);
                    foreach (SerializedFileData.ObjectEntry entry in file.Objects.Where(entry => entry.TypeIndex == typeIndex))
                        sources[entry] = (Dictionary<string, object>)ReadValue(new DataReader(file.GetObjectData(entry), false), tree);
                }
            }
            catch (Exception)
            {
                return null;
            }

            var conversion = new ShaderConversion();
            bool hasEntryBlobs = IsAtLeast(file.UnityVersion, 2019, 3, 0);
            for (int typeIndex = 0; typeIndex < file.Types.Count; typeIndex++)
            {
                SerializedFileData.TypeEntry type = file.Types[typeIndex];
                if (type.ClassId != ShaderClassId && type.ClassId != ComputeShaderClassId)
                    continue;

                bool isShader = type.ClassId == ShaderClassId;
                TypeNode tree = isShader ? GetShaderTree() : GetComputeShaderTree();
                type.Replacement = BuildTypeEntry(type, isShader ? ShaderTypeTree : ComputeShaderTypeTree, file.Version);
                foreach (SerializedFileData.ObjectEntry entry in file.Objects.Where(entry => entry.TypeIndex == typeIndex))
                {
                    bool converted;
                    Dictionary<string, object> result;
                    try
                    {
                        result = isShader ? ConvertShader(sources[entry], tree, hasEntryBlobs, true, out converted) :
                            ConvertComputeShader(sources[entry], tree, true, out converted);
                    }
                    catch (Exception)
                    {
                        // Keep the shader's properties so materials still load, just without programs
                        result = isShader ? ConvertShader(sources[entry], tree, hasEntryBlobs, false, out converted) :
                            ConvertComputeShader(sources[entry], tree, false, out converted);
                    }

                    var writer = new DataBuilder(false);
                    WriteValue(writer, tree, result);
                    entry.Replacement = writer.ToArray();
                    if (converted)
                        conversion.Converted++;
                    else
                        conversion.Unconverted++;
                }
            }

            if (conversion.Converted > 0)
                conversion.File = file.Write(targetPlatform);
            return conversion;
        }

        // A type table entry for the file with the class's type tree from Unity 2022.3, as embedded: old type hash then tree
        static byte[] BuildTypeEntry(SerializedFileData.TypeEntry original, byte[] embedded, uint version)
        {
            var builder = new DataBuilder(false);
            builder.Write(original.Raw, 0, 4 + 1 + 2);                  // class id, is stripped type, script type index
            builder.Write(embedded);
            if (version >= 21)
                builder.Write(0);                                       // type dependencies
            return builder.ToArray();
        }

        #region Shaders

        static Dictionary<string, object> ConvertShader(Dictionary<string, object> source, TypeNode tree, bool hasEntryBlobs, bool convertPrograms, out bool converted)
        {
            var shader = (Dictionary<string, object>)ConvertTo(tree, source);
            var parsedForm = (Dictionary<string, object>)shader["m_ParsedForm"];
            var sourceForm = (Dictionary<string, object>)source["m_ParsedForm"];
            TypeNode passNode = tree["m_ParsedForm"]["m_SubShaders"].Element["m_Passes"].Element;
            TypeNode playerNode = passNode["progVertex"]["m_PlayerSubPrograms"].Element.Element;

            List<byte[]> glEntries = convertPrograms ? ReadGLBlob(source, hasEntryBlobs) : null;
            var blob = new GLESBlobBuilder();
            var keywords = new KeywordTable();
            var subShaders = (List<object>)parsedForm["m_SubShaders"];
            var sourceSubShaders = (List<object>)sourceForm["m_SubShaders"];
            for (int s = 0; s < subShaders.Count; s++)
            {
                var passes = (List<object>)((Dictionary<string, object>)subShaders[s])["m_Passes"];
                var sourcePasses = (List<object>)((Dictionary<string, object>)sourceSubShaders[s])["m_Passes"];
                for (int p = 0; p < passes.Count; p++)
                {
                    var pass = (Dictionary<string, object>)passes[p];
                    var sourcePass = (Dictionary<string, object>)sourcePasses[p];
                    bool hasPrograms = false;
                    foreach (TypeNode programNode in passNode.Children.Where(child => child.Type == "SerializedProgram"))
                    {
                        // Players only use the per-tier lists of subprograms, with their parameters in the blob
                        var program = (Dictionary<string, object>)pass[programNode.Name];
                        program["m_SubPrograms"] = new List<object>();
                        program["m_PlayerSubPrograms"] = new List<object>();
                        program["m_ParameterBlobIndices"] = new List<object>();
                        program["m_SerializedKeywordStateMask"] = new List<object>();
                        program["m_CommonParameters"] = ConvertTo(programNode["m_CommonParameters"], null);

                        // An OpenGL program holds every stage, so Unity keeps it with the vertex program
                        object sourceProgram;
                        if (glEntries != null && programNode.Name == "progVertex" && sourcePass.TryGetValue(programNode.Name, out sourceProgram))
                            hasPrograms |= ConvertGLPrograms((Dictionary<string, object>)sourceProgram, program, playerNode, glEntries, blob, keywords);
                    }

                    pass["m_EditorDataHash"] = new List<object>();
                    pass["m_Platforms"] = hasPrograms ? new byte[] { ShaderPlatformGLES3 } : new byte[0];
                }
            }

            parsedForm["m_KeywordNames"] = keywords.Names;
            parsedForm["m_KeywordFlags"] = keywords.Flags.ToArray();
            converted = blob.Count > 0;
            if (converted)
            {
                byte[] compressedBlob;
                List<object> offsets, compressedLengths, decompressedLengths;
                blob.Build(out compressedBlob, out offsets, out compressedLengths, out decompressedLengths);
                shader["platforms"] = new List<object> { (long)ShaderPlatformGLES3 };
                shader["offsets"] = new List<object> { offsets };
                shader["compressedLengths"] = new List<object> { compressedLengths };
                shader["decompressedLengths"] = new List<object> { decompressedLengths };
                shader["compressedBlob"] = compressedBlob;
                shader["stageCounts"] = new List<object> { 1L };
            }
            else
            {
                shader["platforms"] = new List<object>();
                shader["offsets"] = new List<object>();
                shader["compressedLengths"] = new List<object>();
                shader["decompressedLengths"] = new List<object>();
                shader["compressedBlob"] = new byte[0];
                shader["stageCounts"] = new List<object>();
            }
            return shader;
        }

        // Adds OpenGL ES versions of a program's OpenGL Core subprograms to program, returning whether it had any
        static bool ConvertGLPrograms(Dictionary<string, object> sourceProgram, Dictionary<string, object> program, TypeNode playerNode,
            List<byte[]> glEntries, GLESBlobBuilder blob, KeywordTable keywords)
        {
            var tiers = new List<object>[4];
            var parameterTiers = new List<object>[4];
            for (int t = 0; t < tiers.Length; t++)
            {
                tiers[t] = new List<object>();
                parameterTiers[t] = new List<object>();
            }

            var keywordMask = new SortedSet<long>();
            foreach (Dictionary<string, object> subProgram in (List<object>)sourceProgram["m_SubPrograms"])
            {
                int esType = GetGLESProgramType((int)ToLong(subProgram["m_GpuProgramType"]));
                if (esType < 0)
                    continue;

                GLESProgram converted = blob.Add(glEntries, (int)ToLong(subProgram["m_BlobIndex"]), esType, keywords);
                object requirements;
                var player = ConvertTo(playerNode, new Dictionary<string, object>
                {
                    { "m_BlobIndex", (long)converted.CodeIndex },
                    { "m_KeywordIndices", converted.KeywordIndices },
                    { "m_ShaderRequirements", subProgram.TryGetValue("m_ShaderRequirements", out requirements) ? requirements : 1L },
                    { "m_GpuProgramType", (long)esType },
                });

                // Unity 2022.3 lists each hardware tier's subprograms separately, where older versions marked
                // a subprogram used by every tier as tier 3
                object tierValue;
                int tier = subProgram.TryGetValue("m_ShaderHardwareTier", out tierValue) ? (int)ToLong(tierValue) : -1;
                for (int t = 0; t < 3; t++)
                {
                    if (tier >= 0 && tier < 3 && tier != t)
                        continue;
                    tiers[t].Add(player);
                    parameterTiers[t].Add((long)converted.ParameterIndex);
                }
                keywordMask.UnionWith(converted.KeywordIndices.Select(ToLong));
            }

            if (tiers.All(list => list.Count == 0))
                return false;

            program["m_PlayerSubPrograms"] = tiers.Cast<object>().ToList();
            program["m_ParameterBlobIndices"] = parameterTiers.Cast<object>().ToList();
            program["m_SerializedKeywordStateMask"] = keywordMask.Select(index => (object)index).ToList();
            return true;
        }

        static int GetGLESProgramType(int glType)
        {
            switch (glType)
            {
                case GpuProgramGLCore32:
                    return GpuProgramGLES3;
                case GpuProgramGLCore41:
                case GpuProgramGLCore43:
                    return GpuProgramGLES31;
                default:
                    return -1;
            }
        }

        // Decompresses a shader's OpenGL Core programs and splits them into the blob's entries, or returns null if it has none
        static List<byte[]> ReadGLBlob(Dictionary<string, object> shader, bool hasEntryBlobs)
        {
            var platforms = (List<object>)shader["platforms"];
            int index = platforms.FindIndex(platform => ToLong(platform) == ShaderPlatformGLCore);
            if (index < 0)
                return null;

            // Unity 2019.3 and later split each platform's blob into segments, which an entry names after its offset and length
            List<long> offsets = AsLongs(((List<object>)shader["offsets"])[index]);
            List<long> compressedLengths = AsLongs(((List<object>)shader["compressedLengths"])[index]);
            List<long> decompressedLengths = AsLongs(((List<object>)shader["decompressedLengths"])[index]);
            var blob = (byte[])shader["compressedBlob"];
            var segments = new List<byte[]>();
            for (int s = 0; s < offsets.Count; s++)
            {
                var compressed = new byte[compressedLengths[s]];
                Buffer.BlockCopy(blob, (int)offsets[s], compressed, 0, compressed.Length);
                segments.Add(Lz4Decode(compressed, (int)decompressedLengths[s]));
            }

            var reader = new DataReader(segments[0], false);
            int count = reader.ReadInt32();
            var entries = new List<byte[]>(count);
            for (int i = 0; i < count; i++)
            {
                int offset = reader.ReadInt32();
                int length = reader.ReadInt32();
                int segment = hasEntryBlobs ? reader.ReadInt32() : 0;
                var entry = new byte[length];
                Buffer.BlockCopy(segments[segment], offset, entry, 0, length);
                entries.Add(entry);
            }
            return entries;
        }

        static List<long> AsLongs(object value)
        {
            var list = value as List<object>;
            return list != null ? list.Select(ToLong).ToList() : new List<long> { ToLong(value) };
        }

        // A subprogram from an OpenGL Core blob of Unity 2017.3 to 2021.1
        class GLSubProgram
        {
            public byte[] Stats;                // 16 bytes of instruction counts
            public readonly List<string> GlobalKeywords = new List<string>();
            public readonly List<string> LocalKeywords = new List<string>();
            public byte[] Code;
            public byte[] Channels;             // source map, bind channel count and bind channels
            public byte[] Parameters;           // constant buffers then textures and buffers

            public static GLSubProgram Parse(byte[] data)
            {
                var reader = new DataReader(data, false);
                int version = reader.ReadInt32();
                if (version < OldestGpuProgramVersion || version >= GpuProgramVersion2022)
                    throw new NotSupportedException($"Shader program version {version} isn't supported");

                reader.ReadInt32();                                     // program type
                var program = new GLSubProgram { Stats = new byte[16] };
                byte[] stats = reader.ReadBytes(16);
                Buffer.BlockCopy(stats, 0, program.Stats, 0, stats.Length);
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                    program.GlobalKeywords.Add(reader.ReadAlignedString());
                if (version >= LocalKeywordsGpuProgramVersion)
                {
                    count = reader.ReadInt32();
                    for (int i = 0; i < count; i++)
                        program.LocalKeywords.Add(reader.ReadAlignedString());
                }

                program.Code = reader.ReadBytes(reader.ReadInt32());
                reader.Align(4);
                int channelsStart = reader.Position;
                reader.ReadInt32();                                     // source map
                reader.Skip(reader.ReadInt32() * 8);                    // bind channels: source and target
                program.Channels = reader.Slice(channelsStart, reader.Position);
                program.Parameters = reader.Slice(reader.Position, reader.Length);
                return program;
            }
        }

        class GLESProgram
        {
            public int CodeIndex;
            public int ParameterIndex;
            public List<object> KeywordIndices;
        }

        /// <summary>
        /// Builds an OpenGL ES blob as Unity 2022.3 lays one out: each subprogram becomes a code entry and a parameters entry,
        /// identical parameters are shared, and the index of entries goes alone in the first segment.
        /// </summary>
        class GLESBlobBuilder
        {
            readonly List<byte[]> entries = new List<byte[]>();
            readonly Dictionary<string, int> parameterEntries = new Dictionary<string, int>();
            readonly Dictionary<long, GLESProgram> programs = new Dictionary<long, GLESProgram>();

            public int Count
            {
                get { return programs.Count; }
            }

            public GLESProgram Add(List<byte[]> glEntries, int blobIndex, int esType, KeywordTable keywords)
            {
                long key = (long)blobIndex << 8 | (uint)esType;
                GLESProgram program;
                if (programs.TryGetValue(key, out program))
                    return program;

                GLSubProgram source = GLSubProgram.Parse(glEntries[blobIndex]);
                List<int> keywordIndices = source.GlobalKeywords.Select(name => keywords.Get(name, false))
                    .Concat(source.LocalKeywords.Select(name => keywords.Get(name, true)))
                    .Distinct().OrderBy(index => index).ToList();

                byte[] parameters = BuildParameterEntry(source.Parameters);
                string parameterKey = System.Convert.ToBase64String(parameters);
                int parameterIndex;
                if (!parameterEntries.TryGetValue(parameterKey, out parameterIndex))
                {
                    parameterIndex = entries.Count;
                    entries.Add(parameters);
                    parameterEntries[parameterKey] = parameterIndex;
                }

                program = new GLESProgram
                {
                    CodeIndex = entries.Count,
                    ParameterIndex = parameterIndex,
                    KeywordIndices = keywordIndices.Select(index => (object)(long)index).ToList(),
                };
                entries.Add(BuildCodeEntry(source, esType));
                programs[key] = program;
                return program;
            }

            public void Build(out byte[] compressedBlob, out List<object> offsets, out List<object> compressedLengths, out List<object> decompressedLengths)
            {
                var index = new DataBuilder(false);
                var segments = new List<DataBuilder> { index };
                index.Write(entries.Count);
                DataBuilder segment = null;
                foreach (byte[] entry in entries)
                {
                    if (segment == null || segment.Length > 0 && segment.Length + entry.Length > MaxBlobSegmentSize)
                    {
                        segment = new DataBuilder(false);
                        segments.Add(segment);
                    }

                    index.Write(segment.Length);
                    index.Write(entry.Length);
                    index.Write(segments.Count - 1);
                    segment.Write(entry);
                    segment.Align(4);
                }

                var blob = new MemoryStream();
                offsets = new List<object>();
                compressedLengths = new List<object>();
                decompressedLengths = new List<object>();
                foreach (DataBuilder builder in segments)
                {
                    byte[] data = builder.ToArray();
                    byte[] compressed = Lz4Encode(data, data.Length);
                    offsets.Add(blob.Length);
                    compressedLengths.Add((long)compressed.Length);
                    decompressedLengths.Add((long)data.Length);
                    blob.Write(compressed, 0, compressed.Length);
                }
                compressedBlob = blob.ToArray();
            }
        }

        // A code entry: the program with its keywords, converted GLSL and vertex channels
        static byte[] BuildCodeEntry(GLSubProgram source, int esType)
        {
            var entry = new DataBuilder(false);
            entry.Write(GpuProgramVersion2022);
            entry.Write(esType);
            entry.Write(source.Stats);
            entry.Write(source.GlobalKeywords.Count + source.LocalKeywords.Count);
            foreach (string keyword in source.GlobalKeywords.Concat(source.LocalKeywords))
                entry.WriteAlignedString(keyword);
            byte[] code = ConvertGlsl(source.Code, esType == GpuProgramGLES3 ? "300 es" : "310 es", false);
            entry.Write(code.Length);
            entry.Write(code);
            entry.Align(4);
            entry.Write(source.Channels);
            return entry.ToArray();
        }

        // A parameters entry: what followed the vertex channels in an older subprogram, in the same format
        static byte[] BuildParameterEntry(byte[] parameters)
        {
            var copy = (byte[])parameters.Clone();
            try
            {
                SetBufferArraySizes(copy);
            }
            catch (Exception)
            {
                copy = parameters;
            }

            var entry = new DataBuilder(false);
            entry.Write(GpuProgramVersion2022);
            entry.Write(copy);
            entry.Align(4);
            return entry.ToArray();
        }

        // Unity 2022.3 gives a buffer parameter an array size, 1 for a single buffer, where older versions wrote 0
        static void SetBufferArraySizes(byte[] parameters)
        {
            var reader = new DataReader(parameters, false);
            int constantBuffers = reader.ReadInt32();
            for (int i = 0; i < constantBuffers; i++)
            {
                reader.ReadAlignedString();                             // name
                reader.ReadInt32();                                     // size
                int count = reader.ReadInt32();
                for (int j = 0; j < count; j++)
                {
                    reader.ReadAlignedString();
                    reader.Skip(6 * 4);                                 // type, rows, columns, is matrix, array size, index
                }
                if (reader.ReadInt32() != 0)
                    return;                                             // struct parameters, whose layout isn't known here
            }

            int resources = reader.ReadInt32();
            for (int i = 0; i < resources; i++)
            {
                reader.ReadAlignedString();
                int type = reader.ReadInt32();
                reader.ReadInt32();                                     // index
                int extraPosition = reader.Position;
                int extra = reader.ReadInt32();
                if (type == 0)
                    reader.ReadInt32();                                 // texture dimension and multisampling
                else if (type == 2 && extra == 0)
                    parameters[extraPosition] = 1;
            }
        }

        class KeywordTable
        {
            public readonly List<object> Names = new List<object>();
            public readonly List<byte> Flags = new List<byte>();
            readonly Dictionary<string, int> indices = new Dictionary<string, int>();

            // Unity 2022.3 flags local keywords, which global keyword state doesn't affect, with 1
            public int Get(string name, bool local)
            {
                int index;
                if (!indices.TryGetValue(name, out index))
                {
                    index = Names.Count;
                    indices[name] = index;
                    Names.Add(name);
                    Flags.Add((byte)(local ? 1 : 0));
                }
                return index;
            }
        }

        #endregion

        #region Compute shaders

        static Dictionary<string, object> ConvertComputeShader(Dictionary<string, object> source, TypeNode tree, bool convertPrograms, out bool converted)
        {
            var shader = (Dictionary<string, object>)ConvertTo(tree, null);
            object name;
            shader["m_Name"] = source.TryGetValue("m_Name", out name) ? name : string.Empty;
            TypeNode variantNode = tree["variants"].Element;
            TypeNode parentNode = variantNode["kernels"].Element;
            TypeNode kernelNode = parentNode["uniqueVariants"].Element;

            var variants = new List<object>();
            object sourceVariants;
            if (convertPrograms && source.TryGetValue("variants", out sourceVariants))
            {
                foreach (Dictionary<string, object> sourceVariant in (List<object>)sourceVariants)
                {
                    if (ToLong(sourceVariant["targetRenderer"]) != RendererGLCore)
                        continue;

                    var variant = (Dictionary<string, object>)ConvertTo(variantNode, sourceVariant);
                    variant["targetRenderer"] = (long)RendererGLES3;
                    variant["targetLevel"] = (long)ComputeLevelES31;
                    var constantBuffers = (List<object>)variant["constantBuffers"];
                    var kernels = new List<object>();
                    foreach (Dictionary<string, object> sourceKernel in (List<object>)sourceVariant["kernels"])
                    {
                        // Unity 2020.1 added keywords to compute shaders, so each kernel is now a parent of its variants
                        var kernel = (Dictionary<string, object>)ConvertTo(kernelNode, sourceKernel);
                        kernel["code"] = ConvertGlsl((byte[])sourceKernel["code"], "310 es", true);
                        kernel["requirements"] = ComputeKernelRequirements;
                        var cbs = (List<object>)kernel["cbs"];
                        kernel["cbVariantIndices"] = cbs.Select((cb, i) =>
                        {
                            string cbName = (string)((Dictionary<string, object>)cb)["name"];
                            int found = constantBuffers.FindIndex(buffer => (string)((Dictionary<string, object>)buffer)["name"] == cbName);
                            return (object)(long)(found >= 0 ? found : i);
                        }).ToList();

                        var parent = (Dictionary<string, object>)ConvertTo(parentNode, null);
                        parent["name"] = sourceKernel["name"];
                        parent["uniqueVariants"] = new List<object> { kernel };
                        parent["variantIndices"] = new List<object> { new Dictionary<string, object> { { "first", string.Empty }, { "second", 0L } } };
                        kernels.Add(parent);
                    }

                    variant["kernels"] = kernels;
                    variants.Add(variant);
                }
            }

            shader["variants"] = variants;
            converted = variants.Count > 0;
            return shader;
        }

        #endregion

        #region GLSL

        // OpenGL ES 3 has no default precision for these, and none for floats in fragment shaders. Declarations shared
        // between stages must match, so everything is highp, which OpenGL ES 3 supports in every stage.
        const string Es3Precision =
            "precision highp float;\nprecision highp int;\n" +
            "precision highp sampler2D;\nprecision highp sampler3D;\nprecision highp samplerCube;\nprecision highp samplerCubeShadow;\n" +
            "precision highp sampler2DShadow;\nprecision highp sampler2DArray;\nprecision highp sampler2DArrayShadow;\n" +
            "precision highp isampler2D;\nprecision highp isampler3D;\nprecision highp isamplerCube;\nprecision highp isampler2DArray;\n" +
            "precision highp usampler2D;\nprecision highp usampler3D;\nprecision highp usamplerCube;\nprecision highp usampler2DArray;\n";

        const string Es31Precision =
            "precision highp sampler2DMS;\nprecision highp isampler2DMS;\nprecision highp usampler2DMS;\n" +
            "precision highp image2D;\nprecision highp image3D;\nprecision highp imageCube;\nprecision highp image2DArray;\n" +
            "precision highp iimage2D;\nprecision highp iimage3D;\nprecision highp iimageCube;\nprecision highp iimage2DArray;\n" +
            "precision highp uimage2D;\nprecision highp uimage3D;\nprecision highp uimageCube;\nprecision highp uimage2DArray;\n";

        static readonly Regex ArbExtension = new Regex(@"^\s*#\s*extension\s+GL_ARB_");
        static readonly Regex ImageUniform = new Regex(@"\buniform\s+(?:(?:highp|mediump|lowp)\s+)?([iu]?)image(?:2D|3D|Cube|2DArray|CubeArray|Buffer)\b");
        static readonly Regex ImageFormat = new Regex(@"\b(?:rgba32f|rgba16f|rg32f|rg16f|r11f_g11f_b10f|r32f|r16f|rgba16|rgb10_a2|rgba8|rg16|rg8|r16|r8|" +
            @"rgba16_snorm|rgba8_snorm|rg16_snorm|rg8_snorm|r16_snorm|r8_snorm|rgba32i|rgba16i|rgba8i|rg32i|rg16i|rg8i|r32i|r16i|r8i|" +
            @"rgba32ui|rgba16ui|rgb10_a2ui|rgba8ui|rg32ui|rg16ui|rg8ui|r32ui|r16ui|r8ui)\b");
        static readonly Regex ImageAccess = new Regex(@"\b(?:readonly|writeonly)\b");
        static readonly Regex Layout = new Regex(@"\blayout\s*\(");

        // Converts GLSL stored as bytes, keeping the terminating zeros Unity stores with it
        static byte[] ConvertGlsl(byte[] code, string esVersion, bool compute)
        {
            int length = code.Length;
            while (length > 0 && code[length - 1] == 0)
                length--;

            byte[] converted = Encoding.UTF8.GetBytes(ConvertGlsl(Encoding.UTF8.GetString(code, 0, length), esVersion, compute));
            var result = new byte[converted.Length + code.Length - length];
            Buffer.BlockCopy(converted, 0, result, 0, converted.Length);
            return result;
        }

        /// <summary>
        /// Turns desktop OpenGL GLSL from HLSLcc into OpenGL ES GLSL: each stage's #version line becomes esVersion followed
        /// by precision statements, and the ARB extensions OpenGL ES doesn't have go, since requiring one fails to compile.
        /// Compute shaders' images also get the format qualifier OpenGL ES requires.
        /// </summary>
        static string ConvertGlsl(string code, string esVersion, bool compute)
        {
            string precision = esVersion == "300 es" ? Es3Precision : Es3Precision + Es31Precision;
            string[] lines = code.Split('\n');
            var output = new StringBuilder(code.Length + 2048);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.TrimStart().StartsWith("#version", StringComparison.Ordinal))
                {
                    output.Append("#version ").Append(esVersion).Append('\n');

                    // #extension directives have to come before any declarations, so the stage's others move above the precision statements
                    for (int j = i + 1; j < lines.Length && !lines[j].TrimStart().StartsWith("#version", StringComparison.Ordinal); j++)
                    {
                        string directive = lines[j].Trim();
                        if (!directive.StartsWith("#extension", StringComparison.Ordinal))
                            continue;
                        if (!ArbExtension.IsMatch(directive))
                            output.Append(directive).Append('\n');
                        lines[j] = string.Empty;
                    }
                    output.Append(precision);
                    continue;
                }

                if (ArbExtension.IsMatch(line))
                    continue;

                output.Append(compute ? AddImageFormat(line) : line);
                if (i < lines.Length - 1)
                    output.Append('\n');
            }
            return output.ToString();
        }

        // Desktop OpenGL can leave out a write-only image's format, OpenGL ES can't. The desktop declaration doesn't say
        // what the texture holds, so this assumes four 32-bit channels, or one if the shader both reads and writes it,
        // which is all OpenGL ES allows for that.
        static string AddImageFormat(string line)
        {
            Match image = ImageUniform.Match(line);
            if (!image.Success || ImageFormat.IsMatch(line))
                return line;

            string channels = ImageAccess.IsMatch(line) ? "rgba32" : "r32";
            string format = channels + (image.Groups[1].Value == "i" ? "i" : image.Groups[1].Value == "u" ? "ui" : "f");
            Match layout = Layout.Match(line);
            if (layout.Success && layout.Index < image.Index)
            {
                int close = line.IndexOf(')', layout.Index);
                if (close > 0)
                    return line.Insert(close, ", " + format);
            }
            return line.Insert(image.Index, "layout(" + format + ") ");
        }

        #endregion

        #region Type trees

        class TypeNode
        {
            public string Type;
            public string Name;
            public int MetaFlag;
            public readonly List<TypeNode> Children = new List<TypeNode>();

            public bool IsAligned
            {
                get { return (MetaFlag & 0x4000) != 0; }
            }

            // vector, map, staticvector and so on: an Array child holding the size then an element
            public bool IsArray
            {
                get { return Children.Count > 0 && Children[0].Type == "Array"; }
            }

            public TypeNode Element
            {
                get { return Children[0].Children[1]; }
            }

            public TypeNode this[string name]
            {
                get
                {
                    foreach (TypeNode child in Children)
                    {
                        if (child.Name == name)
                            return child;
                    }
                    throw new KeyNotFoundException($"{Type} has no field {name}");
                }
            }
        }

        static TypeNode shaderTree;
        static TypeNode computeShaderTree;
        static readonly object typeTreeLock = new object();

        static TypeNode GetShaderTree()
        {
            lock (typeTreeLock)
                return shaderTree ?? (shaderTree = ReadTypeTree(ShaderTypeTree, 16, false, 22));
        }

        static TypeNode GetComputeShaderTree()
        {
            lock (typeTreeLock)
                return computeShaderTree ?? (computeShaderTree = ReadTypeTree(ComputeShaderTypeTree, 16, false, 22));
        }

        // Reads a type tree blob, see UnityPy's TypeTreeNode.parse_blob
        static TypeNode ReadTypeTree(byte[] data, int start, bool bigEndian, uint version)
        {
            var reader = new DataReader(data, bigEndian) { Position = start };
            int nodeCount = reader.ReadInt32();
            reader.ReadInt32();                                         // string buffer size
            bool hasRefTypeHash = version >= 19;
            int stringsStart = reader.Position + nodeCount * (hasRefTypeHash ? 32 : 24);
            var parents = new List<TypeNode>();
            TypeNode root = null;
            for (int i = 0; i < nodeCount; i++)
            {
                reader.ReadUInt16();                                    // version
                int level = reader.ReadByte();
                reader.ReadByte();                                      // type flags
                uint typeOffset = reader.ReadUInt32();
                uint nameOffset = reader.ReadUInt32();
                reader.ReadInt32();                                     // byte size
                reader.ReadInt32();                                     // index
                var node = new TypeNode { MetaFlag = reader.ReadInt32() };
                if (hasRefTypeHash)
                    reader.ReadInt64();
                node.Type = GetTypeTreeString(data, stringsStart, typeOffset);
                node.Name = GetTypeTreeString(data, stringsStart, nameOffset);

                if (level == 0)
                    root = node;
                else
                    parents[level - 1].Children.Add(node);
                if (level == parents.Count)
                    parents.Add(node);
                else
                    parents[level] = node;
            }
            return root;
        }

        static string GetTypeTreeString(byte[] data, int stringsStart, uint offset)
        {
            if ((offset & 0x80000000) != 0)
            {
                int start = (int)(offset & 0x7FFFFFFF);
                return CommonStrings.Substring(start, CommonStrings.IndexOf('\0', start) - start);
            }

            int position = stringsStart + (int)offset;
            return Encoding.UTF8.GetString(data, position, Array.IndexOf(data, (byte)0, position) - position);
        }

        static bool IsPrimitive(string type)
        {
            switch (type)
            {
                case "SInt8":
                case "UInt8":
                case "char":
                case "short":
                case "SInt16":
                case "unsigned short":
                case "UInt16":
                case "int":
                case "SInt32":
                case "unsigned int":
                case "UInt32":
                case "Type*":
                case "long long":
                case "SInt64":
                case "unsigned long long":
                case "UInt64":
                case "FileSize":
                    return true;
                default:
                    return false;
            }
        }

        static bool IsByteElement(TypeNode element)
        {
            return element.Children.Count == 0 && !element.IsAligned && (element.Type == "UInt8" || element.Type == "SInt8" || element.Type == "char");
        }

        // Reads a value by its type tree, as UnityPy's TypeTreeHelper does: classes and pairs become dictionaries, arrays
        // lists (byte[] for bytes), integers long, floating point numbers double and strings string
        static object ReadValue(DataReader reader, TypeNode node)
        {
            object value;
            bool align = node.IsAligned;
            switch (node.Type)
            {
                case "SInt8":
                    value = (long)(sbyte)reader.ReadByte();
                    break;
                case "UInt8":
                case "char":
                    value = (long)reader.ReadByte();
                    break;
                case "short":
                case "SInt16":
                    value = (long)(short)reader.ReadUInt16();
                    break;
                case "unsigned short":
                case "UInt16":
                    value = (long)reader.ReadUInt16();
                    break;
                case "int":
                case "SInt32":
                    value = (long)reader.ReadInt32();
                    break;
                case "unsigned int":
                case "UInt32":
                case "Type*":
                    value = (long)reader.ReadUInt32();
                    break;
                case "long long":
                case "SInt64":
                case "unsigned long long":
                case "UInt64":
                case "FileSize":
                    value = reader.ReadInt64();
                    break;
                case "float":
                    value = (double)reader.ReadSingle();
                    break;
                case "double":
                    value = reader.ReadDouble();
                    break;
                case "bool":
                    value = reader.ReadByte() != 0;
                    break;
                case "string":
                    value = reader.ReadAlignedString();
                    break;
                case "TypelessData":
                    value = reader.ReadBytes(reader.ReadInt32());
                    break;
                case "pair":
                    value = new Dictionary<string, object>
                    {
                        { "first", ReadValue(reader, node.Children[0]) },
                        { "second", ReadValue(reader, node.Children[1]) },
                    };
                    break;
                default:
                    if (node.IsArray)
                    {
                        align |= node.Children[0].IsAligned;
                        int size = reader.ReadInt32();
                        TypeNode element = node.Element;
                        if (IsByteElement(element))
                        {
                            value = reader.ReadBytes(size);
                        }
                        else
                        {
                            var list = new List<object>(Math.Min(size, 0x10000));
                            for (int i = 0; i < size; i++)
                                list.Add(ReadValue(reader, element));
                            value = list;
                        }
                    }
                    else
                    {
                        var fields = new Dictionary<string, object>();
                        foreach (TypeNode child in node.Children)
                            fields[child.Name] = ReadValue(reader, child);
                        value = fields;
                    }
                    break;
            }

            if (align)
                reader.Align(4);
            return value;
        }

        static void WriteValue(DataBuilder writer, TypeNode node, object value)
        {
            bool align = node.IsAligned;
            switch (node.Type)
            {
                case "SInt8":
                case "UInt8":
                case "char":
                case "bool":
                    writer.Write((byte)ToLong(value));
                    break;
                case "short":
                case "SInt16":
                case "unsigned short":
                case "UInt16":
                    writer.Write((ushort)ToLong(value));
                    break;
                case "int":
                case "SInt32":
                case "unsigned int":
                case "UInt32":
                case "Type*":
                    writer.Write((uint)ToLong(value));
                    break;
                case "long long":
                case "SInt64":
                case "unsigned long long":
                case "UInt64":
                case "FileSize":
                    writer.Write(ToLong(value));
                    break;
                case "float":
                    writer.Write((float)ToDouble(value));
                    break;
                case "double":
                    writer.Write(ToDouble(value));
                    break;
                case "string":
                    writer.WriteAlignedString(value as string ?? string.Empty);
                    break;
                case "TypelessData":
                    var data = value as byte[] ?? new byte[0];
                    writer.Write(data.Length);
                    writer.Write(data);
                    break;
                case "pair":
                    var pair = (Dictionary<string, object>)value;
                    WriteValue(writer, node.Children[0], pair["first"]);
                    WriteValue(writer, node.Children[1], pair["second"]);
                    break;
                default:
                    if (node.IsArray)
                    {
                        align |= node.Children[0].IsAligned;
                        TypeNode element = node.Element;
                        var bytes = value as byte[];
                        if (bytes != null && IsByteElement(element))
                        {
                            writer.Write(bytes.Length);
                            writer.Write(bytes);
                        }
                        else
                        {
                            IList<object> items = bytes != null ? bytes.Select(b => (object)(long)b).ToList() : (IList<object>)value;
                            writer.Write(items.Count);
                            foreach (object item in items)
                                WriteValue(writer, element, item);
                        }
                    }
                    else
                    {
                        var fields = (Dictionary<string, object>)value;
                        foreach (TypeNode child in node.Children)
                            WriteValue(writer, child, fields[child.Name]);
                    }
                    break;
            }

            if (align)
                writer.Align(4);
        }

        /// <summary>
        /// Makes a value of node's type from source, a value read with an older version of the type: fields are matched
        /// by name, and ones source doesn't have get zeros, empty strings and empty arrays.
        /// </summary>
        static object ConvertTo(TypeNode node, object source)
        {
            bool isNumber = source is long || source is double || source is bool;
            switch (node.Type)
            {
                case "float":
                case "double":
                    return isNumber ? ToDouble(source) : 0.0;
                case "bool":
                    return isNumber && ToLong(source) != 0;
                case "string":
                    return source as string ?? string.Empty;
                case "TypelessData":
                    return source as byte[] ?? new byte[0];
                case "pair":
                    var sourcePair = source as Dictionary<string, object>;
                    return new Dictionary<string, object>
                    {
                        { "first", ConvertTo(node.Children[0], GetField(sourcePair, "first")) },
                        { "second", ConvertTo(node.Children[1], GetField(sourcePair, "second")) },
                    };
            }

            if (IsPrimitive(node.Type))
                return isNumber ? ToLong(source) : 0L;

            if (node.IsArray)
            {
                TypeNode element = node.Element;
                var bytes = source as byte[];
                var items = source as IList<object>;
                if (IsByteElement(element))
                    return bytes ?? (items != null ? items.Select(item => (byte)ToLong(item)).ToArray() : new byte[0]);

                var list = new List<object>();
                if (bytes != null)
                    list.AddRange(bytes.Select(b => ConvertTo(element, (long)b)));
                else if (items != null)
                    list.AddRange(items.Select(item => ConvertTo(element, item)));
                return list;
            }

            var sourceFields = source as Dictionary<string, object>;
            var fields = new Dictionary<string, object>();
            foreach (TypeNode child in node.Children)
                fields[child.Name] = ConvertTo(child, GetField(sourceFields, child.Name));
            return fields;
        }

        static object GetField(Dictionary<string, object> fields, string name)
        {
            object value;
            return fields != null && fields.TryGetValue(name, out value) ? value : null;
        }

        static long ToLong(object value)
        {
            if (value is long)
                return (long)value;
            if (value is double)
                return (long)(double)value;
            if (value is bool)
                return (bool)value ? 1 : 0;
            return System.Convert.ToInt64(value);
        }

        static double ToDouble(object value)
        {
            if (value is double)
                return (double)value;
            return ToLong(value);
        }

        #endregion
    }
}
