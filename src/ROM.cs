using LIBMIO0;
using Quad64;
using Quad64.JSON;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Quad64
{

	public enum RomRegion
	{
		Japan,
		JapanShindou,
		NorthAmerica,
		Europe
	};

	public enum RomType
	{
		Vanilla, // 8MB Compressed ROM
		Extended // Uncompressed ROM
	};

	public enum RomEndian
	{
		Big, // .z64
		Little, // .n64
		Mixed // .v64
	};

	public class ROM
	{

		private RomRegion region = RomRegion.NorthAmerica;
		private RomEndian endian = RomEndian.Big;
		private RomType type = RomType.Vanilla;
		private static ROM instance; // Singleton
		public static ROM Instance {
			get {
				if (instance == null)
					instance = new ROM();
				return instance;
			}
		}

		public string filepath = "";
		public string FilePath => filepath;
		public string FileName => Path.GetFileName(filepath);
		private byte[] bytes;
		private uint[] segStart = new uint[0x20];
		private bool[] segIsMIO0 = new bool[0x20];
		private byte[][] segData = new byte[0x20][];
		public RomRegion Region => region;
		public RomEndian Endian => endian;
		public RomType Type => type;
		public byte[] Bytes => bytes;

		private void checkROM()
		{
			if (bytes[0] == 0x80 && bytes[1] == 0x37)
				endian = RomEndian.Big;
			else if (bytes[0] == 0x37 && bytes[1] == 0x80)
				endian = RomEndian.Mixed;
			else if (bytes[0] == 0x40 && bytes[1] == 0x12)
				endian = RomEndian.Little;

			if (endian == RomEndian.Mixed)
				swapMixedBig();
			else if (endian == RomEndian.Little)
				swapLittleBig();

			if (bytes[0x3E] == 0x45)
				region = RomRegion.NorthAmerica;
			else if (bytes[0x3E] == 0x50)
				region = RomRegion.Europe;
			else if (bytes[0x3E] == 0x4A)
			{
				if (bytes[0x3F] < 3)
					region = RomRegion.Japan;
				else
					region = RomRegion.JapanShindou;
			}

			// Setup segment 0x02 & segment 0x15 addresses
			if (region == RomRegion.NorthAmerica)
			{
				Globals.macro_preset_table = 0xEC7E0;
				Globals.special_preset_table = 0xED350;
				// Globals.seg02_location = new[] { (uint)0x108A40, (uint)0x114750 };
				Globals.seg15_location = new[] { (uint) 0x2ABCA0, (uint) 0x2AC6B0 };
			}
			else if (region == RomRegion.Europe)
			{
				Globals.macro_preset_table = 0xBD590;
				Globals.special_preset_table = 0xBE100;
				// Globals.seg02_location = new[] { (uint)0xDE190, (uint)0xE49F0 };
				Globals.seg15_location = new[] { (uint) 0x28CEE0, (uint) 0x28D8F0 };
			}
			else if (region == RomRegion.Japan)
			{
				Globals.macro_preset_table = 0xEB6D0;
				Globals.special_preset_table = 0xEC240;
				// Globals.seg02_location = new[] { (uint)0x1076D0, (uint)0x112B50 };
				Globals.seg15_location = new[] { (uint) 0x2AA240, (uint) 0x2AAC50 };
			}
			else if (region == RomRegion.JapanShindou)
			{
				Globals.macro_preset_table = 0xC8D60;
				Globals.special_preset_table = 0xC98D0;
				//Globals.seg02_location = new[] { (uint)0xE42F0, (uint)0xEF770 };
				Globals.seg15_location = new[] { (uint) 0x286AC0, (uint) 0x2874D0 };
			}

			findAndSetSegment02();
			Console.WriteLine("Segment2 location: 0x" + Globals.seg02_location[0].ToString("X8") +
				" to 0x" + Globals.seg02_location[1].ToString("X8"));

			if (bytes[Globals.seg15_location[0]] == 0x17)
				type = RomType.Extended;
			else
				type = RomType.Vanilla;


			Console.WriteLine("ROM = " + filepath);
			Console.WriteLine("ROM Endian = " + endian);
			Console.WriteLine("ROM Region = " + region);
			Console.WriteLine("ROM Type = " + type);
			Console.WriteLine("-----------------------");
		}

		private void swapMixedBig()
		{
			for (int i = 0; i < bytes.Length; i += 2)
			{
				byte temp = bytes[i];
				bytes[i] = bytes[i + 1];
				bytes[i + 1] = temp;
			}
		}

		private void swapLittleBig()
		{
			byte[] temp = new byte[4];
			for (int i = 0; i < bytes.Length; i += 4)
			{
				temp[0] = bytes[i + 0];
				temp[1] = bytes[i + 1];
				temp[2] = bytes[i + 2];
				temp[3] = bytes[i + 3];
				bytes[i + 0] = temp[3];
				bytes[i + 1] = temp[2];
				bytes[i + 2] = temp[1];
				bytes[i + 3] = temp[0];
			}
		}

		public string getRegionText()
		{
			switch (Region)
			{
				case RomRegion.NorthAmerica:
					return "North America";
				case RomRegion.Europe:
					return "Europe";
				case RomRegion.Japan:
					return "Japan";
				case RomRegion.JapanShindou:
					return "Japan (Shindou edition)";
				default:
					return "Unknown";
			}
		}

		public string getEndianText()
		{
			switch (Endian)
			{
				case RomEndian.Big:
					return "Big Endian";
				case RomEndian.Mixed:
					return "Middle Endian";
				case RomEndian.Little:
					return "Little Endian";
				default:
					return "Unknown";
			}
		}

		public string getInternalName()
		{
			return System.Text.Encoding.ASCII.GetString(getSubArray_safe(Bytes, 0x20, 20));
		}

		public void readFile(string filename)
		{
			filepath = filename;
			bytes = File.ReadAllBytes(filename);
			checkROM();
			Globals.pathToAutoLoadROM = filepath;
			Globals.needToSave = false;
			SettingsFile.SaveGlobalSettings("default");
		}

		public void saveFile()
		{
			if (Endian == RomEndian.Mixed)
			{
				swapMixedBig();
				File.WriteAllBytes(filepath, bytes);
				swapMixedBig();
			}
			else if (Endian == RomEndian.Little)
			{
				swapLittleBig();
				File.WriteAllBytes(filepath, bytes);
				swapLittleBig();
			}
			else // Save as big endian by default
			{
				File.WriteAllBytes(filepath, bytes);
			}
			Globals.pathToAutoLoadROM = filepath;
			Globals.needToSave = false;
			SettingsFile.SaveGlobalSettings("default");
		}

		public void saveFileAs(string filename, RomEndian saveType)
		{
			if (saveType == RomEndian.Mixed)
			{
				swapMixedBig();
				File.WriteAllBytes(filename, bytes);
				swapMixedBig();
				endian = RomEndian.Mixed;
			}
			else if (saveType == RomEndian.Little)
			{
				swapLittleBig();
				File.WriteAllBytes(filename, bytes);
				swapLittleBig();
				endian = RomEndian.Little;
			}
			else // Save as big endian by default
			{
				File.WriteAllBytes(filename, bytes);
				endian = RomEndian.Big;
			}
			Globals.needToSave = false;
			filepath = filename;
			Globals.pathToAutoLoadROM = filepath;
			SettingsFile.SaveGlobalSettings("default");
		}

		public void setSegment(uint index, uint segmentStart, uint segmentEnd, bool isMIO0)
		{
			if (segmentStart > segmentEnd)
				return;

			if (!isMIO0)
			{
				segStart[index] = segmentStart;
				segIsMIO0[index] = false;
				uint size = segmentEnd - segmentStart;
				segData[index] = new byte[size];
				for (uint i = 0; i < size; i++)
					segData[index][i] = bytes[segmentStart + i];
			}
			else
			{
				segIsMIO0[index] = true;
				segData[index] = MIO0.mio0_decode(getSubArray(bytes, segmentStart, segmentEnd - segmentStart));
			}
		}

		public byte[] getSegment(ushort seg)
		{
			return segData[seg];
		}

		public uint decodeSegmentAddress(uint segOffset)
		{
			// Console.WriteLine("Decoding segment address: " + segOffset.ToString("X8"));
			byte seg = (byte) (segOffset >> 24);
			if (segIsMIO0[seg])
				throw new System.ArgumentException("Cannot decode segment address from MIO0 data. (decodeSegmentAddress 1)");
			uint off = segOffset & 0x00FFFFFF;
			return segStart[seg] + off;
		}

		public uint decodeSegmentAddress(byte segment, uint offset)
		{
			if (segIsMIO0[segment])
				throw new System.ArgumentException("Cannot decode segment address from MIO0 data. (decodeSegmentAddress 2)");
			return segStart[segment] + offset;
		}

		public byte[] getDataFromSegmentAddress(uint segOffset, uint size)
		{
			byte seg = (byte) (segOffset >> 24);
			uint off = segOffset & 0x00FFFFFF;

			if (segData[seg].Length < off + size)
				return new byte[size];

			return getSubArray(segData[seg], off, size);
		}

		public byte[] getDataFromSegmentAddress_safe(uint segOffset, uint size)
		{
			byte seg = (byte) (segOffset >> 24);
			uint off = segOffset & 0x00FFFFFF;
			if (segData[seg] != null)
				return getSubArray_safe(segData[seg], off, size);
			else
				return new byte[size];
		}


		public byte[] getSubArray_safe(byte[] arr, uint offset, uint size)
		{
			if (arr == null)
				return new byte[size];

			byte[] newArr = new byte[size];
			for (uint i = 0; i < size; i++)
			{
				if (offset + i < arr.Length)
					newArr[i] = arr[offset + i];
				else
					newArr[i] = 0x00;
			}
			return newArr;
		}

		public byte[] getSubArray(byte[] arr, uint offset, uint size)
		{
			byte[] newArr = new byte[size];
			for (uint i = 0; i < size; i++)
				newArr[i] = arr[offset + i];
			return newArr;
		}

		public void printArray(byte[] arr, int size)
		{
			Console.WriteLine(BitConverter.ToString(arr.Take(size).ToArray()).Replace("-", " "));
		}

		public void printArraySection(byte[] arr, int offset, int size)
		{
			Console.WriteLine(BitConverter.ToString(arr.Skip(offset).Take(size).ToArray()).Replace("-", " "));
		}

		public void printROMSection(int start, int end)
		{
			Console.WriteLine(BitConverter.ToString(bytes.Skip(start).Take(end - start).ToArray()).Replace("-", " "));
		}

		public int getLevelIndexById(ushort Id)
		{
			int index = 0;
			foreach (KeyValuePair<string, ushort> entry in levelIDs)
			{
				if (entry.Value == Id)
					return index;
				index++;
			}
			return 0;
		}

		public ushort getLevelIdFromIndex(int index)
		{
			return levelIDs.Values.ElementAt<ushort>(index);
		}

		// From: https://stackoverflow.com/a/26880541
		private int SearchBytes(byte[] haystack, byte[] needle)
		{
			var len = needle.Length;
			var limit = haystack.Length - len;
			for (var i = 0; i <= limit; i++)
			{
				var k = 0;
				for (; k < len; k++)
				{
					if (needle[k] != haystack[i + k]) break;
				}
				if (k == len) return i;
			}
			return -1;
		}


		public void writeWord(uint offset, int word)
		{
			bytes[offset + 0] = (byte) (word >> 24);
			bytes[offset + 1] = (byte) (word >> 16);
			bytes[offset + 2] = (byte) (word >> 8);
			bytes[offset + 3] = (byte) (word);
		}

		public void writeWord(uint offset, uint word)
		{
			writeWord(offset, (int) word);
		}

		public void writeHalfword(uint offset, short half)
		{
			bytes[offset + 0] = (byte) (half >> 8);
			bytes[offset + 1] = (byte) (half);
		}

		public void writeHalfword(uint offset, ushort word)
		{
			writeHalfword(offset, (short) word);
		}

		public void writeByte(uint offset, byte b)
		{
			bytes[offset] = b;
		}

		public byte readByte(uint offset)
		{
			return bytes[offset];
		}
		public short readHalfword(uint offset)
		{
			return (short) (bytes[offset] << 8 | bytes[offset + 1]);
		}
		public ushort readHalfwordUnsigned(uint offset)
		{
			return (ushort) readHalfword(offset);
		}
		public int readWord(uint offset)
		{
			return bytes[0 + offset] << 24 | bytes[1 + offset] << 16
				| bytes[2 + offset] << 8 | bytes[3 + offset];
		}
		public uint readWordUnsigned(uint offset)
		{
			return (uint) (bytes[0 + offset] << 24 | bytes[1 + offset] << 16
				| bytes[2 + offset] << 8 | bytes[3 + offset]);
		}


		public bool isSegmentMIO0(byte seg)
		{
			return segIsMIO0[seg];
		}

		public void findAndSetSegment02()
		{
			AssemblyReader ar = new AssemblyReader();
			List<AssemblyReader.JAL_CALL> func_calls;
			switch (region)
			{
				default:
				case RomRegion.NorthAmerica:
					func_calls = ar.findJALsInFunction(Globals.seg02_init_NA, Globals.RAMtoROM_NA);
					for (int i = 0; i < func_calls.Count; i++)
						if (func_calls[i].JAL_ADDRESS == Globals.seg02_alloc_NA && func_calls[i].a0 == 0x2)
						{
							Globals.seg02_location = new[] { func_calls[i].a1, func_calls[i].a2 };
							if (readWordUnsigned(func_calls[i].a1) == 0x4D494F30)
								segIsMIO0[0x02] = true;
						}
					break;
				case RomRegion.Europe:
					func_calls = ar.findJALsInFunction(Globals.seg02_init_EU, Globals.RAMtoROM_EU);
					for (int i = 0; i < func_calls.Count; i++)
						if (func_calls[i].JAL_ADDRESS == Globals.seg02_alloc_EU && func_calls[i].a0 == 0x2)
						{
							Globals.seg02_location = new[] { func_calls[i].a1, func_calls[i].a2 };
							if (readWordUnsigned(func_calls[i].a1) == 0x4D494F30)
								segIsMIO0[0x02] = true;
						}
					break;
				case RomRegion.Japan:
					func_calls = ar.findJALsInFunction(Globals.seg02_init_JP, Globals.RAMtoROM_JP);
					for (int i = 0; i < func_calls.Count; i++)
						if (func_calls[i].JAL_ADDRESS == Globals.seg02_alloc_JP && func_calls[i].a0 == 0x2)
						{
							Globals.seg02_location = new[] { func_calls[i].a1, func_calls[i].a2 };
							if (readWordUnsigned(func_calls[i].a1) == 0x4D494F30)
								segIsMIO0[0x02] = true;
						}
					break;
				case RomRegion.JapanShindou:
					func_calls = ar.findJALsInFunction(Globals.seg02_init_JS, Globals.RAMtoROM_JS);
					for (int i = 0; i < func_calls.Count; i++)
						if (func_calls[i].JAL_ADDRESS == Globals.seg02_alloc_JS && func_calls[i].a0 == 0x2)
						{
							Globals.seg02_location = new[] { func_calls[i].a1, func_calls[i].a2 };
							if (readWordUnsigned(func_calls[i].a1) == 0x4D494F30)
								segIsMIO0[0x02] = true;
						}
					break;
			}
		}

		public Dictionary<string, ushort> levelIDs = new Dictionary<string, ushort>
		{
			{ "Big Boo's Haunt", 0x04 },
			{ "Cool Cool Mountain", 0x05 },
			{ "Inside Castle", 0x06 },
			{ "Hazy Maze Cave", 0x07 },
			{ "Shifting Sand Land", 0x08 },
			{ "Bob-omb Battlefield", 0x09 },
			{ "Snowman's Land", 0x0A },
			{ "Wet Dry World", 0x0B },
			{ "Jolly Roger Bay", 0x0C },
			{ "Tiny Huge Island", 0x0D },
			{ "Tick Tock Clock", 0x0E },
			{ "Rainbow Ride", 0x0F },
			{ "Castle Grounds", 0x10 },
			{ "Bowser Course 1", 0x11 },
			{ "Vanish Cap", 0x12 },
			{ "Bowser Course 2", 0x13 },
			{ "Secret Aquarium", 0x14 },
			{ "Bowser Course 3", 0x15 },
			{ "Lethal Lava Land", 0x16 },
			{ "Dire Dire Docks", 0x17 },
			{ "Whomp's Fortress", 0x18 },
			{ "End Cake Picture", 0x19 },
			{ "Castle Courtyard", 0x1A },
			{ "Peach's Secret Slide", 0x1B },
			{ "Metal Cap", 0x1C },
			{ "Wing Cap", 0x1D },
			{ "Bowser Battle 1", 0x1E },
			{ "Rainbow Clouds", 0x1F },
			{ "Bowser Battle 2", 0x21 },
			{ "Bowser Battle 3", 0x22 },
			{ "Tall Tall Mountain", 0x24 }
		};

	}
}
