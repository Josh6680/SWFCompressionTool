using System;
using System.IO;
using System.Linq;
using SevenZip;
using SharpCompress.Compressors;
using SharpCompress.Compressors.Deflate;
using LZMADecoder = SevenZip.Compression.LZMA.Decoder;
using LZMAEncoder = SevenZip.Compression.LZMA.Encoder;
using SystemCompressionMode = System.IO.Compression.CompressionMode;
using SystemZLibStream = System.IO.Compression.ZLibStream;

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

		private static readonly byte[] HeaderCWS = [(byte)'C', (byte)'W', (byte)'S'];
		private static readonly byte[] HeaderFWS = [(byte)'F', (byte)'W', (byte)'S'];
		private static readonly byte[] HeaderZWS = [(byte)'Z', (byte)'W', (byte)'S'];

		private static int Main(string[] args)
		{
			if (args.Length == 0)
			{
				Console.WriteLine("Fail. No arguments specified. You need to provide the file name.");
				Console.Read();
				return 1;
			}

			string fileName = args[0];

			if (string.IsNullOrWhiteSpace(fileName))
			{
				Console.WriteLine("Fail. Invalid arguments. You need to provide a valid file name.");
				Console.Read();
				return 2;
			}

			if (!File.Exists(fileName))
			{
				Console.WriteLine("Fail. The specified file does not exist.");
				Console.Read();
				return 3;
			}

			FileInfo fileInfo = new(fileName);
			string targetFileName = Path.Combine(Path.GetDirectoryName(fileInfo.FullName), Path.GetFileNameWithoutExtension(fileInfo.Name) + FileUncompressed + fileInfo.Extension);

			byte[] header = new byte[HeaderTypeLength];
			using (BinaryReader reader = new(new FileStream(fileName, FileMode.Open)))
			{
				reader.Read(header, 0, HeaderTypeLength);
			}

			if (header.SequenceEqual(HeaderCWS))
			{
				DecompressZLIB(fileName, targetFileName);
				Console.WriteLine($"[CWS->FWS] Wrote uncompressed copy to '{targetFileName}'.");
			}
			else if (header.SequenceEqual(HeaderZWS))
			{
				DecompressLZMA(fileName, targetFileName);
				Console.WriteLine($"[ZWS->FWS] Wrote uncompressed copy to '{targetFileName}'.");
			}
			else if (header.SequenceEqual(HeaderFWS))
			{
				Console.WriteLine("Do you want the compressed file to use the newer ZWS/LZMA format (Y/N)? [Y]");
				string chosenType = "ZWS";
				string input = Console.ReadLine();
				if (string.IsNullOrWhiteSpace(input) || input.Equals("Y", StringComparison.CurrentCultureIgnoreCase))
				{
					CompressLZMA(fileName, targetFileName);
				}
				else
				{
					chosenType = "CWS";
					Compress(fileName, targetFileName);
				}
				Console.WriteLine($"[FWS->{chosenType}] Wrote compressed copy to '{targetFileName}'.");
			}
			else
			{
				Console.WriteLine("Fail. This is not a valid SWF file (header is not CWS or FWS).");
				Console.Read();
				return 4;
			}

			Console.Read();
			return 0;
		}

		private static void Compress(string fileName, string targetFileName)
		{
			byte[] header = new byte[HeaderLength];
			HeaderCWS.CopyTo(header, 0);

			byte[] compressed;
			using (BinaryReader reader = new(new FileStream(fileName, FileMode.Open)))
			{
				reader.BaseStream.Seek(HeaderTypeLength, SeekOrigin.Begin);
				reader.Read(header, HeaderTypeLength, HeaderVersionLength + HeaderSizeLength);

				byte[] data = new byte[reader.BaseStream.Length - HeaderLength];

				reader.BaseStream.Seek(HeaderLength, SeekOrigin.Begin);
				reader.Read(data, 0, data.Length);

				compressed = CompressBuffer(data);
			}

			byte[] combined = CombineBytes(header, compressed);

			File.WriteAllBytes(targetFileName, combined);
		}

		private static byte[] CompressBuffer(byte[] data)
		{
			using (MemoryStream output = new())
			{
				using (ZlibStream compressor = new(output, CompressionMode.Compress, CompressionLevel.BestCompression))
				{
					compressor.Write(data, 0, data.Length);
				}
				return output.ToArray();
			}
		}

		private static void DecompressZLIB(string fileName, string targetFileName)
		{
			byte[] header = new byte[HeaderLength];
			HeaderFWS.CopyTo(header, 0);

			byte[] uncompressed;
			using (BinaryReader reader = new(new FileStream(fileName, FileMode.Open)))
			{
				reader.BaseStream.Seek(HeaderTypeLength, SeekOrigin.Begin);
				reader.Read(header, HeaderTypeLength, HeaderVersionLength + HeaderSizeLength);

				reader.BaseStream.Seek(HeaderLength, SeekOrigin.Begin);
				byte[] data = new byte[reader.BaseStream.Length - HeaderLength];
				reader.Read(data, 0, data.Length);
				uncompressed = UncompressBuffer(data);
			}

			byte[] combined = CombineBytes(header, uncompressed);

			File.WriteAllBytes(targetFileName, combined);
		}

		private static byte[] UncompressBuffer(byte[] data)
		{
			using (MemoryStream input = new(data))
			using (MemoryStream output = new())
			{
				using (SystemZLibStream decompressor = new(input, SystemCompressionMode.Decompress, true))
				{
					int n = 0;
					byte[] working = new byte[1024];
					while ((n = decompressor.Read(working, 0, working.Length)) != 0)
					{
						output.Write(working, 0, n);
					}
				}
				return output.ToArray();
			}
		}

		private static void CompressLZMA(string fileName, string targetFileName)
		{
			byte[] header = new byte[HeaderLength + HeaderLZMACompressedSizeLength];
			HeaderZWS.CopyTo(header, 0);

			byte[] compressed;
			using (BinaryReader reader = new(new FileStream(fileName, FileMode.Open)))
			{
				reader.BaseStream.Seek(HeaderTypeLength, SeekOrigin.Begin);
				reader.Read(header, HeaderTypeLength, HeaderVersionLength + HeaderSizeLength);

				byte[] data = new byte[reader.BaseStream.Length - HeaderLength];

				reader.BaseStream.Seek(HeaderLength, SeekOrigin.Begin);
				reader.Read(data, 0, data.Length);

				compressed = CompressLZMABuffer(data);
			}

			// Add LZMA header for the compressed size, subtract the coder properties from length.
			byte[] fileLengthBytes = BitConverter.GetBytes(compressed.Length - 5);
			fileLengthBytes.CopyTo(header, HeaderLength);

			byte[] combined = CombineBytes(header, compressed);

			File.WriteAllBytes(targetFileName, combined);
		}

		private static byte[] CompressLZMABuffer(byte[] data)
		{
			using (MemoryStream input = new(data))
			using (MemoryStream output = new())
			{
				LZMAEncoder coder = new();
				coder.SetCoderProperties([CoderPropID.NumFastBytes, CoderPropID.EndMarker, CoderPropID.DictionarySize], [128, true, 2097152]);
				coder.WriteCoderProperties(output);
				coder.Code(input, output, input.Length, -1, null);
				return output.ToArray();
			}
		}

		private static void DecompressLZMA(string fileName, string targetFileName)
		{
			byte[] header = new byte[HeaderLength];
			HeaderFWS.CopyTo(header, 0);

			LZMADecoder coder = new();
			using (FileStream input = new(fileName, FileMode.Open))
			{
				input.Seek(HeaderTypeLength, SeekOrigin.Begin);
				input.Read(header, HeaderTypeLength, HeaderVersionLength + HeaderSizeLength);

				// Read the decompressed file size.
				byte[] fileLengthBytes = new byte[HeaderSizeLength];
				input.Seek(HeaderTypeLength + HeaderVersionLength, SeekOrigin.Begin);
				input.Read(fileLengthBytes, 0, fileLengthBytes.Length);
				int fileLength = BitConverter.ToInt32(fileLengthBytes, 0);

				input.Seek(HeaderLength + HeaderLZMACompressedSizeLength, SeekOrigin.Begin);

				using (FileStream output = new(targetFileName, FileMode.Create))
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