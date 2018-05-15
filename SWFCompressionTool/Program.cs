using System;
using System.IO;
using System.Linq;
using Ionic.Zlib;
using LZMADecoder = SevenZip.Compression.LZMA.Decoder;

namespace SWFCompressionTool
{
	static class Program
	{
		private const int HeaderTypeLength = 3;
		private const int HeaderVersionLength = 1;
		private const int HeaderSizeLength = 4;
		private const int HeaderLZMACompressedSizeLength = 4;
		private const int HeaderLength = HeaderTypeLength + HeaderVersionLength + HeaderSizeLength;
		private const string FileCompressed = "_compressed";
		private const string FileUncompressed = "_uncompressed";

		private static readonly byte[] HeaderCWS = { (byte)'C', (byte)'W', (byte)'S' };
		private static readonly byte[] HeaderFWS = { (byte)'F', (byte)'W', (byte)'S' };
		private static readonly byte[] HeaderZWS = { (byte)'Z', (byte)'W', (byte)'S' };

		private static int Main(string[] args)
		{
			if (args.Length <= 0)
			{
				Console.WriteLine("Fail. No arguments specified. You need to provide the file name.");
				Console.ReadKey();
				return 1;
			}

			string fileName = args[0];

			if (string.IsNullOrWhiteSpace(fileName))
			{
				Console.WriteLine("Fail. Invalid arguments. You need to provide a valid file name.");
				Console.ReadKey();
				return 2;
			}

			if (!File.Exists(fileName))
			{
				Console.WriteLine("Fail. The specified file does not exist.");
				Console.ReadKey();
				return 3;
			}

			byte[] header = new byte[HeaderTypeLength];
			using (BinaryReader reader = new BinaryReader(new FileStream(fileName, FileMode.Open)))
			{
				reader.Read(header, 0, HeaderTypeLength);
			}

			if (header.SequenceEqual(HeaderCWS))
			{
				FileInfo fileInfo = new FileInfo(fileName);
				string targetFileName = Path.Combine(Path.GetDirectoryName(fileInfo.FullName), Path.GetFileNameWithoutExtension(fileInfo.Name) + FileUncompressed + fileInfo.Extension);
				DecompressZLIB(fileName, targetFileName);
				Console.WriteLine("Wrote uncompressed copy to '" + targetFileName + "'.");
			}
			else if (header.SequenceEqual(HeaderZWS))
			{
				FileInfo fileInfo = new FileInfo(fileName);
				string targetFileName = Path.Combine(Path.GetDirectoryName(fileInfo.FullName), Path.GetFileNameWithoutExtension(fileInfo.Name) + FileUncompressed + fileInfo.Extension);
				DecompressLZMA(fileName, targetFileName);
				Console.WriteLine("Wrote uncompressed copy to '" + targetFileName + "'.");
			}
			else if (header.SequenceEqual(HeaderFWS))
			{
				FileInfo fileInfo = new FileInfo(fileName);
				string targetFileName = Path.Combine(Path.GetDirectoryName(fileInfo.FullName), Path.GetFileNameWithoutExtension(fileInfo.Name) + FileCompressed + fileInfo.Extension);
				Compress(fileName, targetFileName);
				Console.WriteLine("Wrote compressed copy to '" + targetFileName + "'.");
			}
			else
			{
				Console.WriteLine("Fail. This is not a valid SWF file (header is not CWS or FWS).");
				Console.ReadKey();
				return 4;
			}

			Console.ReadKey();
			return 0;
		}

		private static void Compress(string fileName, string targetFileName)
		{
			byte[] header = new byte[HeaderLength];
			HeaderCWS.CopyTo(header, 0);

			byte[] compressed;
			using (BinaryReader reader = new BinaryReader(new FileStream(fileName, FileMode.Open)))
			{
				reader.BaseStream.Seek(HeaderTypeLength, SeekOrigin.Begin);
				reader.Read(header, HeaderTypeLength, HeaderVersionLength + HeaderSizeLength);

				reader.BaseStream.Seek(HeaderLength, SeekOrigin.Begin);
				byte[] data = new byte[reader.BaseStream.Length - HeaderLength];
				reader.Read(data, 0, data.Length);
				compressed = ZlibStream.CompressBuffer(data);
			}

			byte[] combined = CombineBytes(header, compressed);

			File.WriteAllBytes(targetFileName, combined);
		}

		private static void DecompressZLIB(string fileName, string targetFileName)
		{
			byte[] header = new byte[HeaderLength];
			HeaderFWS.CopyTo(header, 0);

			byte[] uncompressed;
			using (BinaryReader reader = new BinaryReader(new FileStream(fileName, FileMode.Open)))
			{
				reader.BaseStream.Seek(HeaderTypeLength, SeekOrigin.Begin);
				reader.Read(header, HeaderTypeLength, HeaderVersionLength + HeaderSizeLength);

				reader.BaseStream.Seek(HeaderLength, SeekOrigin.Begin);
				byte[] data = new byte[reader.BaseStream.Length - HeaderLength];
				reader.Read(data, 0, data.Length);
				uncompressed = ZlibStream.UncompressBuffer(data);
			}

			byte[] combined = CombineBytes(header, uncompressed);

			File.WriteAllBytes(targetFileName, combined);
		}

		private static void DecompressLZMA(string fileName, string targetFileName)
		{
			byte[] header = new byte[HeaderLength];
			HeaderFWS.CopyTo(header, 0);

			LZMADecoder coder = new LZMADecoder();
			using (FileStream input = new FileStream(fileName, FileMode.Open))
			{
				input.Seek(HeaderTypeLength, SeekOrigin.Begin);
				input.Read(header, HeaderTypeLength, HeaderVersionLength + HeaderSizeLength);

				// Read the decompressed file size.
				byte[] fileLengthBytes = new byte[HeaderSizeLength];
				input.Seek(HeaderTypeLength + HeaderVersionLength, SeekOrigin.Begin);
				input.Read(fileLengthBytes, 0, fileLengthBytes.Length);
				int fileLength = BitConverter.ToInt32(fileLengthBytes, 0);

				input.Seek(HeaderLength + HeaderLZMACompressedSizeLength, SeekOrigin.Begin);

				using (FileStream output = new FileStream(targetFileName, FileMode.Create))
				{
					output.Write(header, 0, header.Length);

					// Read the decoder properties
					byte[] properties = new byte[5];
					input.Read(properties, 0, 5);


					coder.SetDecoderProperties(properties);
					coder.Code(input, output, input.Length, fileLength, null);
				}
			}
		}

		private static byte[] CombineBytes(params byte[][] arrays)
		{
			byte[] combined = new byte[arrays.Sum(a => a.Length)];
			int offset = 0;
			foreach (byte[] array in arrays)
			{
				Buffer.BlockCopy(array, 0, combined, offset, array.Length);
				offset += array.Length;
			}
			return combined;
		}
	}
}