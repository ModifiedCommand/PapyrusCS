﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using fNbt;
using LevelDB;

namespace Maploader.Source
{
    public class World
    {
        private DB db;

        public void Open(string pathDb)
        {
            var options = new LevelDB.Options();
            options.Compression = CompressionType.ZlibRaw;

            db = new DB(options, pathDb);
        }


        public Chunk GetChunk(int x, int z)
        {
            if (db == null)
                throw new InvalidOperationException("Open Db first");

            var subChunks = new Dictionary<byte, byte[]>();

            var key = CreateKey(x, z);

            for (byte i = 0; i < 15; i++)
            {
                key[9] = i;

                var data = db.Get(key);
                if (data != null)
                {
                    subChunks[i] = data;
                }
            }
            Chunk c = new Chunk(x,z);

            foreach (var subChunkRaw in subChunks)
            {
                CopySubChunkToChunk(c, subChunkRaw);
            }

          
            return c;
        }

        private LookupTable.LookupTable Table { get; } = new LookupTable.LookupTable();

        private void CopySubChunkToChunk(Chunk chunk, KeyValuePair<byte, byte[]> subChunkRawData)
        {
            byte subChunkId = subChunkRawData.Key;
            int yOffset = subChunkId * 16;

            using (MemoryStream ms = new MemoryStream(subChunkRawData.Value))
            using (var bs = new BinaryReader(ms, Encoding.Default))
            {

                int version = bs.ReadByte();
                int storages = 1;
                switch (version)
                {
                    case 0:
                        for (int position = 0; position < 4096; position++)
                        {
                            int blockId = bs.ReadByte();
                            int blockData = 0; // TODO
                            int x = (position >> 8) & 0xF;
                            int y = position & 0xF;
                            int z = (position >> 4) & 0xF;

                            chunk.SetBlockId(x, yOffset + y, z, 
                                new BlockData(Table.Lookups[new Coordinate2D(blockId, 0)].name, 0) {Version = 0},
                                true);
                        }

                        break;

                    case 8:
                        storages = bs.ReadByte();
                        goto case 1;
                    case 1:
                        for (int storage = 0; storage < storages; storage++)
                        {
                            byte paletteAndFlag = bs.ReadByte();
                            bool isRuntime = (paletteAndFlag & 1) != 0;
                            int bitsPerBlock = paletteAndFlag >> 1;
                            int blocksPerWord = (int) Math.Floor(32.0 / bitsPerBlock);
                            int wordCount = (int) Math.Ceiling(4096.0 / blocksPerWord);
                            long blockIndex = ms.Position;


                            ms.Seek(wordCount * 4, SeekOrigin.Current); //4 bytes per word.
                            //Palette localPallete; //To get 'real' data
                            PersistancePalette localPalette = null;
                            if (isRuntime)
                            {
                                /*localPallete = new RuntimePallete(VarNumberSerializer.readSVarInt(bytes));
                                for (int palletId = 0; palletId < localPallete.size(); palletId++)
                                {
                                    localPallete.put(palletId, VarNumberSerializer.readSVarInt(bytes));
                                }*/
                            }
                            else
                            {
                                localPalette = new PersistancePalette(bs.ReadInt32());
                                for (int palletId = 0; palletId < localPalette.Size; palletId++)
                                {
                                    var (name, val) = GetNbtVal(ms);
                                    localPalette.Put(palletId, name, val);
                                }
                            }

                            long afterPaletteIndex = ms.Position;

                            ms.Position = blockIndex;

                            int position = 0;
                            for (int wordi = 0; wordi < wordCount; wordi++)
                            {
                                int word = bs.ReadInt32();
                                for (int block = 0; block < blocksPerWord; block++)
                                {
                                    // Todo ist diese Zeile hier richtig?

                                    int state = (word >> ((position % blocksPerWord) * bitsPerBlock)) & ((1 << bitsPerBlock) - 1);
                                    int x = (position >> 8) & 0xF;
                                    int y = position & 0xF;
                                    int z = (position >> 4) & 0xF;

                                    try
                                    {
                                        // Todo: doppelte keys treten immer noch auf!?
                                        chunk.SetBlockId(x, yOffset + y, z, localPalette.Keys[state], true);
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine(ex.Message);
                                    }
                                    //section.setBlockId(x, y, z, localPallete.getBlockId(state));
                                    //section.setBlockData(x, y, z, localPallete.getBlockData(state));
                                    position++;

                                    // Todo: irgendwas läuft hier noch nicht ganz rund, wir brechen mal ab
                                    if (position >= 4096)
                                        break;
                                }
                                // Todo: irgendwas läuft hier noch nicht ganz rund, wir brechen mal ab
                                if (position >= 4096)
                                    break;
                            }

                            ms.Position = afterPaletteIndex;
                        }

                        break;

                }
            }

        }

        private static (string, int) GetNbtVal(MemoryStream ms)
        {
            int value = 0;
            string name = "";
            var nbt = new NbtReader(ms, false);
            nbt.ReadToFollowing();
            if (!nbt.IsCompound)
                throw new Exception("Could not read nbt");

            if (nbt.ReadToDescendant("name"))
            {
                name = nbt.ReadValueAs<string>();
            }
            if (nbt.ReadToDescendant("val"))
            {
                switch (nbt.TagType)
                {
                    case NbtTagType.Int:
                        value = nbt.ReadValueAs<int>();
                        break;

                    case NbtTagType.Short:
                        value = nbt.ReadValueAs<short>();
                        break;
                    case NbtTagType.Long:
                        value = (int)nbt.ReadValueAs<long>();
                        break;
                }
            }

            while (!nbt.IsAtStreamEnd)
            {
                nbt.ReadToFollowing();
            }


            return (name, value);

        }

        private static byte[] CreateKey(int x, int z)
        {
            // Todo: make it faster
            var key = new byte[10];
            using (var ms = new MemoryStream(key))
            using (var bs = new BinaryWriter(ms))
            {
                bs.Write(x);
                bs.Write(z);
                bs.Write((byte) 47);
            }

            return key;
        }

        public void Close()
        {
            db.Dispose();
            db = null;
        }
    }
}
