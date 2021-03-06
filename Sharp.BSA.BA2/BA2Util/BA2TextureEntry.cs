﻿using System;
using System.Collections.Generic;
using System.IO;

namespace SharpBSABA2.BA2Util
{
    public class BA2TextureEntry : ArchiveEntry
    {
        public List<BA2TextureChunk> Chunks { get; private set; } = new List<BA2TextureChunk>();

        byte unk8;
        byte numChunks;
        ushort chunkHdrLen;
        ushort height;
        ushort width;
        byte numMips;
        byte format;
        ushort unk16;

        public override bool Compressed
        {
            get
            {
                // After testing it seems like ALL textures are compressed.
                return this.Chunks[0].packSz != 0;
            }
        }
        public override uint DisplaySize
        {
            get
            {
                uint size = 0;
                foreach (var chunk in this.Chunks)
                    size += chunk.fullSz;
                return size;
            }
        }
        public override ulong Offset
        {
            get
            {
                // ToDo: Chunks are NOT always lined up after each other. Find another way to calculate this, or maybe just remove it completely.
                // It's not really usefull information to the user. (I don't think at least)
                return this.Chunks[0].offset;
            }
        }

        public BA2TextureEntry(Archive ba2, int index) : base(ba2, index)
        {
            nameHash = ba2.BinaryReader.ReadUInt32();
            Extension = new string(ba2.BinaryReader.ReadChars(4));
            dirHash = ba2.BinaryReader.ReadUInt32();
            unk8 = ba2.BinaryReader.ReadByte();
            numChunks = ba2.BinaryReader.ReadByte();
            chunkHdrLen = ba2.BinaryReader.ReadUInt16();
            height = ba2.BinaryReader.ReadUInt16();
            width = ba2.BinaryReader.ReadUInt16();
            numMips = ba2.BinaryReader.ReadByte();
            format = ba2.BinaryReader.ReadByte();
            unk16 = ba2.BinaryReader.ReadUInt16();

            for (int i = 0; i < numChunks; i++)
            {
                this.Chunks.Add(new BA2TextureChunk(ba2.BinaryReader));
            }
        }
        
        protected override void WriteDataToStream(Stream stream)
        {
            DDS_HEADER ddsHeader = new DDS_HEADER();
            ddsHeader.dwSize = ddsHeader.GetSize();
            ddsHeader.dwHeaderFlags = DDS.DDS_HEADER_FLAGS_TEXTURE | DDS.DDS_HEADER_FLAGS_LINEARSIZE | DDS.DDS_HEADER_FLAGS_MIPMAP;
            ddsHeader.dwHeight = height;
            ddsHeader.dwWidth = width;
            ddsHeader.dwMipMapCount = numMips;
            ddsHeader.PixelFormat.dwSize = ddsHeader.PixelFormat.GetSize();
            ddsHeader.dwSurfaceFlags = DDS.DDS_SURFACE_FLAGS_TEXTURE | DDS.DDS_SURFACE_FLAGS_MIPMAP;

            switch ((DXGI_FORMAT)format)
            {
                case DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM:
                    ddsHeader.PixelFormat.dwFlags = DDS.DDS_FOURCC;
                    ddsHeader.PixelFormat.dwFourCC = DDS.MAKEFOURCC('D', 'X', 'T', '1');
                    ddsHeader.dwPitchOrLinearSize = (uint)(width * height / 2); // 4bpp
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_BC2_UNORM:
                    ddsHeader.PixelFormat.dwFlags = DDS.DDS_FOURCC;
                    ddsHeader.PixelFormat.dwFourCC = DDS.MAKEFOURCC('D', 'X', 'T', '3');
                    ddsHeader.dwPitchOrLinearSize = (uint)(width * height); // 8bpp
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM:
                    ddsHeader.PixelFormat.dwFlags = DDS.DDS_FOURCC;
                    ddsHeader.PixelFormat.dwFourCC = DDS.MAKEFOURCC('D', 'X', 'T', '5');
                    ddsHeader.dwPitchOrLinearSize = (uint)(width * height); // 8bpp
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_BC5_UNORM:
                    ddsHeader.PixelFormat.dwFlags = DDS.DDS_FOURCC;
                    if ((this.Archive as BA2).UseATIFourCC)
                        ddsHeader.PixelFormat.dwFourCC = DDS.MAKEFOURCC('A', 'T', 'I', '2'); // this is more correct but the only thing I have found that supports it is the nvidia photoshop plugin
                    else
                        ddsHeader.PixelFormat.dwFourCC = DDS.MAKEFOURCC('D', 'X', 'T', '5');
                    ddsHeader.dwPitchOrLinearSize = (uint)(width * height); // 8bpp
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_BC7_UNORM:
                    // totally wrong but not worth writing out the DX10 header
                    ddsHeader.PixelFormat.dwFlags = DDS.DDS_FOURCC;
                    ddsHeader.PixelFormat.dwFourCC = DDS.MAKEFOURCC('B', 'C', '7', '\0');
                    ddsHeader.dwPitchOrLinearSize = (uint)(width * height); // 8bpp
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM:
                    ddsHeader.PixelFormat.dwFlags = DDS.DDS_RGBA;
                    ddsHeader.PixelFormat.dwRGBBitCount = 32;
                    ddsHeader.PixelFormat.dwRBitMask = 0x00FF0000;
                    ddsHeader.PixelFormat.dwGBitMask = 0x0000FF00;
                    ddsHeader.PixelFormat.dwBBitMask = 0x000000FF;
                    ddsHeader.PixelFormat.dwABitMask = 0xFF000000;
                    ddsHeader.dwPitchOrLinearSize = (uint)(width * height * 4); // 32bpp
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R8_UNORM:
                    ddsHeader.PixelFormat.dwFlags = DDS.DDS_RGB;
                    ddsHeader.PixelFormat.dwRGBBitCount = 8;
                    ddsHeader.PixelFormat.dwRBitMask = 0xFF;
                    ddsHeader.dwPitchOrLinearSize = (uint)(width * height); // 8bpp
                    break;
                default:
                    return;
            }

            var temp = new MemoryStream();

            using (var bw = new BinaryWriter(temp))
            {
                bw.Write((uint)DDS.DDS_MAGIC);
                ddsHeader.Write(bw);

                for (int i = 0; i < numChunks; i++)
                {
                    byte[] compressed = new byte[this.Chunks[i].packSz];
                    byte[] full = new byte[this.Chunks[i].fullSz];
                    bool isCompressed = this.Chunks[i].packSz != 0;

                    this.BinaryReader.BaseStream.Seek((long)this.Chunks[i].offset, SeekOrigin.Begin);

                    if (!isCompressed)
                    {
                        this.BinaryReader.Read(full, 0, full.Length);
                    }
                    else
                    {
                        this.BinaryReader.Read(compressed, 0, compressed.Length);
                        // Uncompress
                        this.Archive.Inflater.Reset();
                        this.Archive.Inflater.SetInput(compressed);
                        this.Archive.Inflater.Inflate(full);
                    }

                    bw.Write(full);
                }

                byte[] bytes = temp.GetBuffer();
                stream.Write(bytes, 0, bytes.Length);
            }
        }
    }
}
