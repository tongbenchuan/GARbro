//! \file       ArcPAK.cs
//! \date       2018 Nov 04
//! \brief      Leaf resource archive.
//
// Copyright (C) 2018 by morkt
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to
// deal in the Software without restriction, including without limitation the
// rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
// sell copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
// IN THE SOFTWARE.
//

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.Leaf
{
    [Export(typeof(ArchiveFormat))]
    public class KcapOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/KCAP"; } }
        public override string Description { get { return "Leaf resource archive"; } }
        public override uint     Signature { get { return 0x5041434B; } } // 'KCAP'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public KcapOpener ()
        {
            ContainedFormats = new[] { "TGA", "BJR", "BMP", "OGG", "WAV", "AMP/LEAF", "SCR" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int version = 0;
            int count = file.View.ReadInt32 (4);
            uint first_offset = file.View.ReadUInt32 (0x24);
            if (!IsSaneCount (count) || count * 0x24 + 8 != first_offset)
            {
                version = 1;
                count = file.View.ReadInt32 (8);
                first_offset = file.View.ReadUInt32 (0x28);
                if (!IsSaneCount (count) || count * 0x24 + 0xC != first_offset)
                {
                    count = file.View.ReadInt32 (12);
                    first_offset = file.View.ReadUInt32 (0x34);
                    if (!IsSaneCount (count) || count * 0x2C + 0x10 != first_offset)
                        return null;
                    version = 2;
                }
            }
            List<Entry> dir;
            if (0 == version)
                dir = ReadIndexV1 (file, count, 8);
            else if (1 == version)
                dir = ReadIndexV1 (file, count, 0xC);
            else
                dir = ReadIndexV2 (file, count);
            if (null == dir)
                return null;
            return new ArcFile (file, this, dir);
        }

        List<Entry> ReadIndexV1 (ArcView file, int count, uint index_offset)
        {
            const uint index_entry_size = 0x24;
            uint index_size = (uint)count * index_entry_size;
            if (file.View.Reserve (index_offset, index_size) < index_size)
                return null;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint size = file.View.ReadUInt32 (index_offset+0x20);
                if (size != 0)
                {
                    var name = file.View.ReadString (index_offset+4, 0x18);
                    var entry = Create<PackedEntry> (name);
                    entry.Offset = file.View.ReadUInt32 (index_offset+0x1C);
                    entry.Size   = size;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    entry.IsPacked = file.View.ReadInt32 (index_offset) != 0;
                    dir.Add (entry);
                }
                index_offset += index_entry_size;
            }
            return dir;
        }

        List<Entry> ReadIndexV2 (ArcView file, int count)
        {
            const uint index_entry_size = 0x2C;
            uint index_offset = 0x10;
            uint index_size = (uint)count * index_entry_size;
            if (file.View.Reserve (index_offset, index_size) < index_size)
                return null;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint size = file.View.ReadUInt32 (index_offset+0x28);
                if (size != 0)
                {
                    var name = file.View.ReadString (index_offset+4, 0x18);
                    var entry = Create<PackedEntry> (name);
                    entry.Offset = file.View.ReadUInt32 (index_offset+0x24);
                    entry.Size   = size;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    entry.IsPacked = file.View.ReadInt32 (index_offset) != 0;
                    dir.Add (entry);
                }
                index_offset += index_entry_size;
            }
            return dir;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return base.OpenEntry (arc, entry);
            if (0 == pent.UnpackedSize)
                pent.UnpackedSize = arc.File.View.ReadUInt32 (entry.Offset+4);
            var input = arc.File.CreateStream (entry.Offset+8, entry.Size-8);
            return new LzssStream (input);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            if (!entry.Name.HasExtension (".tga"))
                return base.OpenImage (arc, entry);
            var input = arc.OpenBinaryEntry (entry);
            try
            {
                var header = input.ReadHeader (18);
                if (0 == header[16])
                    header[16] = 32;
                if (0 == header[17] && 32 == header[16])
                    header[17] = 8;
                Stream tga_input = new StreamRegion (input.AsStream, 18);
                tga_input = new PrefixStream (header.ToArray(), tga_input);
                var tga = new BinaryStream (tga_input, entry.Name);
                var info = ImageFormat.Tga.ReadMetaData (tga);
                if (info != null)
                {
                    tga.Position = 0;
                    return new ImageFormatDecoder (tga, ImageFormat.Tga, info);
                }
            }
            catch { /* ignore errors */ }
            input.Position = 0;
            return ImageFormatDecoder.Create (input);
        }
    }

    [Export(typeof(ScriptFormat))]
    public class AmpFormat : GenericScriptFormat
    {
        public override string        Type { get { return ""; } }
        public override string         Tag { get { return "AMP/LEAF"; } }
        public override string Description { get { return "Leaf engine internal file"; } }
        public override uint     Signature { get { return 0; } }
    }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "SDT")]
    [ExportMetadata("Target", "SCR")]
    public class SdtFormat : ResourceAlias { }
}
